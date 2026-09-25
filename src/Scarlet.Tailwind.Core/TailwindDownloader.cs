using System;
using System.IO;
using System.IO.Abstractions;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Scarlet.Tailwind.Core.Extensions;
using Scarlet.Tailwind.Core.Providers;

namespace Scarlet.Tailwind.Core;

/// <summary>
/// Handles downloading Tailwind runtimes from GitHub releases.
/// </summary>
/// <remarks>
/// Tailwind publishes bare, per-platform executables rather than archives, so there is no extraction step
/// anywhere below: the response body <em>is</em> the binary, and it is streamed straight into the staged
/// file. Everything else - staging plus an atomic move, the cross-process mutex, and the version marker -
/// works exactly as it would for a runtime that does ship an archive.
/// </remarks>
public sealed class TailwindDownloader
{
    private const string GithubReleasesUrl = "https://github.com/tailwindlabs/tailwindcss/releases";

    /// <summary>The checksums asset Tailwind publishes alongside every release.</summary>
    private const string ChecksumsFileName = "sha256sums.txt";

    private readonly Platform _platform;
    private readonly HttpClient _httpClient;
    private readonly IFileSystem _fileSystem;
    private readonly IChmodProvider _chmodProvider;
    private readonly ITailwindLogger _log;
    private readonly ILatestVersionResolver _latestVersionResolver;

    public TailwindDownloader(
        HttpClient httpClient,
        ILatestVersionResolver latestVersionResolver,
        IFileSystem fileSystem,
        IChmodProvider chmodProvider,
        Platform platform,
        ITailwindLogger log)
    {
        _platform = platform;
        _httpClient = httpClient;
        _fileSystem = fileSystem;
        _chmodProvider = chmodProvider;
        _log = log;
        _latestVersionResolver = latestVersionResolver;
    }

    /// <summary>
    /// Downloads the Tailwind runtime with cross-process synchronization.
    /// Uses a named mutex to prevent concurrent downloads when multiple MSBuild projects
    /// target the same runtime directory (e.g., in monorepo scenarios).
    /// </summary>
    /// <param name="runtimeDirectory">Directory where the runtime should be downloaded.</param>
    /// <param name="version">Specific version to download (e.g., "4.3.3"). If null or empty, downloads latest.</param>
    /// <param name="mutexTimeoutSeconds">Maximum seconds to wait for the download mutex. Defaults to 300 (5 minutes).</param>
    /// <returns>Path to the downloaded Tailwind executable.</returns>
    public string DownloadRuntime(string runtimeDirectory, string? version = null, int mutexTimeoutSeconds = 300)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
        {
            throw new ArgumentException("Runtime directory must be specified when using TailwindRuntimeDownload", nameof(runtimeDirectory));
        }

        var runtimeId = TailwindRuntimeResolver.GetRuntimeIdentifier(_platform);
        var assetName = TailwindRuntimeResolver.GetDownloadName(_platform);
        var executableName = TailwindRuntimeResolver.GetExecutableName(_platform);

        var fullRuntimePath = Path.Combine(runtimeDirectory, runtimeId, "native");
        var executablePath = Path.Combine(fullRuntimePath, executableName);
        var versionMarkerPath = GetVersionMarkerPath(executablePath);
        var hasExplicitVersion = !string.IsNullOrWhiteSpace(version);

        // Fast path: only trustworthy for a pinned version - "latest" must always ask GitHub whether it moved.
        // The executable is published atomically only after the download and chmod complete.
        if (hasExplicitVersion && IsCacheValidForVersion(executablePath, versionMarkerPath, version!))
        {
            _log.LogMessage($"Tailwind {version} is already cached at {executablePath}. Skipping download.");
            _chmodProvider.EnsureExecutablePermissions(executablePath);
            return executablePath;
        }

        _fileSystem.Directory.CreateDirectory(fullRuntimePath);

        var mutexName = CreateMutexName(executablePath);
        using var mutex = new Mutex(false, mutexName, out var createdNew);

        if (!createdNew)
        {
            _log.LogMessage("Another process is downloading the Tailwind runtime. Waiting...");
        }

        bool acquired;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.FromSeconds(mutexTimeoutSeconds));
        }
        catch (AbandonedMutexException)
        {
            // Previous owner crashed — we now own the mutex, proceed normally
            acquired = true;
        }

        if (!acquired)
        {
            throw new TimeoutException(
                "Timed out waiting for another process to finish downloading the Tailwind runtime.");
        }

        if (!createdNew)
        {
            _log.LogMessage("Finished waiting. Resuming Tailwind runtime setup.");
        }

        try
        {
            // Double-check: another process may have completed the download while we waited
            if (hasExplicitVersion && IsCacheValidForVersion(executablePath, versionMarkerPath, version!))
            {
                _log.LogMessage($"Tailwind {version} was downloaded by another process while waiting. Skipping download.");
                _chmodProvider.EnsureExecutablePermissions(executablePath);
                return executablePath;
            }

            var stagedExecutablePath = CreateStagedExecutablePath(fullRuntimePath, executableName);

            if (hasExplicitVersion)
            {
                var (downloadUrl, checksumsUrl) = BuildDownloadUrls(assetName, version);
                DownloadToStagedExecutableAsync(downloadUrl, checksumsUrl, stagedExecutablePath, assetName)
                    .GetAwaiter().GetResult();

                var publishedPath = PublishStagedExecutable(stagedExecutablePath, executablePath);
                WriteVersionMarker(versionMarkerPath, version!);
                return publishedPath;
            }

            var (latestDownloadUrl, latestChecksumsUrl) = BuildDownloadUrls(assetName, null);
            return ResolveAndDownloadLatestAsync(latestDownloadUrl, latestChecksumsUrl, executablePath, versionMarkerPath, stagedExecutablePath, assetName)
                .GetAwaiter().GetResult();
        }
        finally
        {
            // ReleaseMutex is thread-affine: it must run on the exact thread that called WaitOne. Everything
            // in this try block is a blocking .GetAwaiter().GetResult() rather than a real `await`, so this
            // method never yields its thread back to the pool - unlike an `async` method spanning the same
            // awaits, where a post-await continuation resuming on a different pooled thread would make this
            // throw ApplicationException instead of releasing the lock.
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Downloads the Tailwind runtime for the current platform, with the same cross-process mutex synchronization
    /// as <see cref="DownloadRuntime"/>.
    /// </summary>
    /// <param name="runtimeDirectory">Directory where the runtime should be downloaded.</param>
    /// <param name="version">Specific version to download (e.g., "4.3.3"). If null or empty, downloads latest.</param>
    /// <param name="mutexTimeoutSeconds">Maximum seconds to wait for the download mutex. Defaults to 300 (5 minutes).</param>
    /// <returns>Path to the downloaded Tailwind executable.</returns>
    /// <remarks>
    /// Delegates to <see cref="DownloadRuntime"/> on a dedicated pooled thread via <c>Task.Run</c>, rather than
    /// reimplementing it as a genuinely asynchronous method. The mutex this acquires is thread-affine - released
    /// only by the thread that acquired it - so the whole critical section must run start-to-finish on one
    /// thread. <c>Task.Run</c> gives it exactly that thread for the mutex's entire lifetime while still freeing
    /// the caller's thread, which a truly `async` version spanning real `await`s could not guarantee.
    /// </remarks>
    public Task<string> DownloadRuntimeAsync(string runtimeDirectory, string? version = null, int mutexTimeoutSeconds = 300)
        => Task.Run(() => DownloadRuntime(runtimeDirectory, version, mutexTimeoutSeconds));

    /// <summary>
    /// Creates an HttpClient configured for downloading Tailwind runtimes.
    /// </summary>
    public static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.Add("User-Agent", "Scarlet.Tailwind");
        return client;
    }

    internal static string CreateMutexName(string executablePath)
    {
        var normalizedPath = Path.GetFullPath(executablePath).ToUpperInvariant();
        var hashString = HashUtilities.ComputeSha256Hex(normalizedPath).ToUpperInvariant();
        return $"Global\\ScarletTailwind_{hashString}";
    }

    /// <summary>
    /// Builds the executable download URL and the matching upstream SHA-256 checksums URL for a Tailwind
    /// release.
    /// </summary>
    /// <remarks>
    /// Both URLs are always plain string formatting from a known version (or "latest"), never derived from
    /// an HTTP response. GitHub's release-asset redirect chain has two hops: the first
    /// (".../releases/download/v4.3.3/tailwindcss-linux-x64") carries the version; the second - a signed,
    /// time-limited "release-assets.githubusercontent.com" blob URL - does not, and no adjacent
    /// sha256sums.txt exists there. A client that follows redirects automatically only observes that second
    /// hop, so deriving the checksums URL from the downloaded response's resolved URI (as opposed to
    /// building it here, before any request is made) would point at a checksums file that does not exist.
    /// See <see cref="GitHubLatestVersionResolver"/> for the same two-hop concern.
    ///
    /// <paramref name="assetName"/> is the full release asset filename, so it already carries the
    /// platform-specific ".exe" on Windows and none elsewhere.
    /// </remarks>
    private static (string DownloadUrl, string ChecksumsUrl) BuildDownloadUrls(string assetName, string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return ($"{GithubReleasesUrl}/latest/download/{assetName}",
                    $"{GithubReleasesUrl}/latest/download/{ChecksumsFileName}");
        }

        return ($"{GithubReleasesUrl}/download/v{version}/{assetName}",
                $"{GithubReleasesUrl}/download/v{version}/{ChecksumsFileName}");
    }

    /// <summary>
    /// Downloads the Tailwind executable into <paramref name="stagedExecutablePath"/>.
    /// </summary>
    private async Task DownloadToStagedExecutableAsync(string downloadUrl, string checksumsUrl, string stagedExecutablePath, string assetName)
    {
        // ResponseHeadersRead avoids buffering the whole ~110 MB executable in memory before it can be
        // streamed to disk, which the default ResponseContentRead would do.
        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        await WriteResponseToStagedExecutableAsync(response, downloadUrl, checksumsUrl, stagedExecutablePath, assetName);
    }

    /// <summary>
    /// Resolves the concrete version behind "latest" via <see cref="_latestVersionResolver"/> and downloads
    /// it only if it differs from what is already cached.
    /// </summary>
    private async Task<string> ResolveAndDownloadLatestAsync(
        string latestUrl,
        string latestChecksumsUrl,
        string executablePath,
        string versionMarkerPath,
        string stagedExecutablePath,
        string assetName)
    {
        var resolvedVersion = await _latestVersionResolver.TryResolveVersionAsync(latestUrl);

        if (resolvedVersion is not null && IsCacheValidForVersion(executablePath, versionMarkerPath, resolvedVersion))
        {
            _log.LogMessage($"Tailwind 'latest' still resolves to {resolvedVersion}, which is already cached at {executablePath}. Skipping download.");
            _chmodProvider.EnsureExecutablePermissions(executablePath);
            return executablePath;
        }

        // A resolved version can be downloaded directly by its tag, skipping the redirect we already
        // followed once to resolve it. If resolution failed, fall back to letting the main (redirect
        // following) client resolve "latest" itself; the checksums URL falls back the same way, which
        // means (rarely, only when resolution fails) it is re-resolved independently of the executable's
        // own "latest" redirect and could theoretically land on a different release cut in between the two
        // requests. Deriving it from the executable's response instead is not an option - see
        // BuildDownloadUrls.
        var (downloadUrl, checksumsUrl) = resolvedVersion is not null
            ? BuildDownloadUrls(assetName, resolvedVersion)
            : (latestUrl, latestChecksumsUrl);

        if (resolvedVersion is not null)
        {
            _log.LogMessage($"Tailwind 'latest' resolved to {resolvedVersion}.");
        }

        await DownloadToStagedExecutableAsync(downloadUrl, checksumsUrl, stagedExecutablePath, assetName);
        var publishedPath = PublishStagedExecutable(stagedExecutablePath, executablePath);

        if (resolvedVersion is not null)
        {
            WriteVersionMarker(versionMarkerPath, resolvedVersion);
        }
        else
        {
            // Could not determine what version was just downloaded (e.g. GitHub changed the redirect
            // shape). Clear any stale marker rather than leave it pointing at a different version.
            _fileSystem.File.TryDeleteFile(versionMarkerPath);
        }

        return publishedPath;
    }

    /// <summary>
    /// Streams an already-obtained response body into <paramref name="stagedExecutablePath"/> and verifies it
    /// against upstream's published SHA-256 sums.
    /// </summary>
    /// <remarks>
    /// The body is written directly to the staged path rather than by way of a temp file, because a Tailwind
    /// release asset is the executable itself - there is nothing to unpack, and an intermediate copy would
    /// mean writing ~110 MB twice. The staged file is still never the file consumers run: publication is a
    /// separate, atomic step that happens only after this method returns successfully.
    /// </remarks>
    private async Task WriteResponseToStagedExecutableAsync(HttpResponseMessage response, string downloadUrl, string checksumsUrl, string stagedExecutablePath, string assetName)
    {
        EnsureSuccessOrThrow(response, downloadUrl);

        try
        {
            // Hashed in the same pass as the write, via CryptoStream, rather than reading the ~110 MB
            // executable back off disk afterward just to hash it. That second full read would double the
            // disk I/O for every download.
            string actualHash;
            using (var sha256 = SHA256.Create())
            {
                using (var fileStream = _fileSystem.File.Create(stagedExecutablePath))
                using (var hashingStream = new CryptoStream(fileStream, sha256, CryptoStreamMode.Write))
                {
                    await response.Content.CopyToAsync(hashingStream);
                    hashingStream.FlushFinalBlock();
                }

                actualHash = BitConverter.ToString(sha256.Hash).Replace("-", "");
            }

            // Verify against upstream's published SHA-256 sums before the staged file can be published.
            // Tailwind publishes sha256sums.txt alongside every release; checking it catches a corrupt or
            // truncated download before it reaches a consumer build as a working-looking executable.
            await VerifyChecksumAsync(actualHash, checksumsUrl, assetName);
        }
        catch
        {
            // The staged file belongs to this method until it returns successfully - only then does the
            // caller take ownership, and only then does its own finally start cleaning up. Without this,
            // every failed or mismatched download would strand a ~110 MB file in the runtime directory,
            // where nothing ever looks for it again.
            _fileSystem.File.TryDeleteFile(stagedExecutablePath);
            throw;
        }
    }

    private static void EnsureSuccessOrThrow(HttpResponseMessage response, string downloadUrl)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Failed to download Tailwind runtime from {downloadUrl}. Status: {response.StatusCode}");
        }
    }

    public static string GetVersionMarkerPath(string executablePath) => executablePath + ".version";

    private bool IsCacheValidForVersion(string executablePath, string versionMarkerPath, string expectedVersion)
    {
        if (!_fileSystem.File.Exists(executablePath) || !_fileSystem.File.Exists(versionMarkerPath))
        {
            return false;
        }

        var storedVersion = _fileSystem.File.ReadAllText(versionMarkerPath).Trim();
        return string.Equals(storedVersion, expectedVersion, StringComparison.Ordinal);
    }

    private void WriteVersionMarker(string versionMarkerPath, string version)
    {
        _fileSystem.File.WriteAllText(versionMarkerPath, version);
    }

    /// <summary>
    /// Verifies an already-computed hash against the SHA-256 sum GitHub publishes alongside each Tailwind
    /// release.
    /// </summary>
    /// <remarks>
    /// Tailwind names each asset in sha256sums.txt with a <c>./</c> prefix:
    /// <code>55fd0b24...398195  ./tailwindcss-linux-arm64</code>
    /// so the filename field is trimmed of that prefix before it is compared. Comparing the raw field would
    /// match nothing, and every download would be rejected as having no checksum entry.
    /// </remarks>
    private async Task VerifyChecksumAsync(string actualHash, string checksumsUrl, string assetName)
    {
        string checksumsText;
        try
        {
            // Buffered, unlike the executable download above: sha256sums.txt is well under a kilobyte, so
            // there is nothing to stream, and ResponseHeadersRead would scope HttpClient.Timeout to the
            // headers and leave the body read unbounded. GetStringAsync also honours the response charset and
            // raises a status failure itself, so it needs no separate check to wrap.
            checksumsText = await _httpClient.GetStringAsync(checksumsUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidDataException($"Failed to download checksums from {checksumsUrl}.", ex);
        }

        string? expectedHash = null;
        foreach (var line in checksumsText.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && StripLeadingDotSlash(parts[parts.Length - 1]).Equals(assetName, StringComparison.Ordinal))
            {
                expectedHash = parts[0];
                break;
            }
        }

        if (expectedHash is null)
        {
            throw new InvalidDataException($"No checksum entry for '{assetName}' in {checksumsUrl}.");
        }

        if (!expectedHash.Equals(actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Checksum mismatch for '{assetName}'. Expected {expectedHash}, got {actualHash}.");
        }
    }

    /// <summary>
    /// Removes the <c>./</c> that Tailwind's checksums file prefixes every filename with, leaving anything
    /// else untouched.
    /// </summary>
    internal static string StripLeadingDotSlash(string fileName)
        => fileName.StartsWith("./", StringComparison.Ordinal) ? fileName.Substring(2) : fileName;

    internal string PublishStagedExecutable(string stagedExecutablePath, string executablePath)
    {
        try
        {
            if (!_fileSystem.File.Exists(stagedExecutablePath))
            {
                throw new FileNotFoundException(
                    $"Tailwind executable was not found after download before publication. Final path: {executablePath}. Staging path: {stagedExecutablePath}");
            }

            _chmodProvider.EnsureExecutablePermissions(stagedExecutablePath);

            // Reaching this point means the caller already determined any existing cache is stale
            // (missing/mismatched version marker) or absent, so a pre-existing file here is leftover
            // content that must be replaced, not a valid cache hit to preserve. Unlike the staged-file
            // cleanup below, a failed delete here must not be swallowed: silently keeping the stale file
            // while the caller goes on to write the new version marker would make the marker lie about
            // what is actually on disk.
            _fileSystem.File.TryDeleteFile(executablePath);

            try
            {
                _fileSystem.File.Move(stagedExecutablePath, executablePath);
            }
            catch (IOException) when (_fileSystem.File.Exists(executablePath))
            {
                // Another concurrent caller already published a file at this path - e.g. a mutex abandoned
                // by a crashed process, or two hosts sharing a network path where the named mutex (which is
                // process/session scoped) does not reach across machines.
                return executablePath;
            }

            if (!_fileSystem.File.Exists(executablePath))
            {
                throw new FileNotFoundException(
                    $"Tailwind executable was not found after publication at expected path: {executablePath}");
            }

            return executablePath;
        }
        finally
        {
            _fileSystem.File.TryDeleteFile(stagedExecutablePath);
        }
    }

    private static string CreateStagedExecutablePath(string directoryPath, string executableName)
    {
        return Path.Combine(directoryPath, $".{executableName}.{Guid.NewGuid():N}.tmp");
    }
}
