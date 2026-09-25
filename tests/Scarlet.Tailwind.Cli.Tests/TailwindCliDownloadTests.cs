using System.IO.Abstractions.TestingHelpers;
using System.Security.Cryptography;
using RichardSzalay.MockHttp;
using Scarlet.Tailwind.Cli.Tests.Mock;

namespace Scarlet.Tailwind.Cli.Tests;

/// <summary>
/// Covers the resolver's download path, which is the portable <c>any</c> package's only route to a Tailwind.
/// </summary>
/// <remarks>
/// Driven through a real <c>TailwindDownloader</c> over a mocked transport rather than a stubbed one, because
/// what is worth checking is that the resolver hands it the right directory and version - and the
/// directory is version-scoped precisely so a later version request is not served the earlier binary.
/// </remarks>
public class TailwindCliDownloadTests
{
    // Rooted through GetFullPath so the paths are drive-qualified on Windows. A drive-less "/cache" is
    // ambiguous: MockFileSystem resolves it against its own default drive, while Path.GetFullPath - which
    // the resolver applies to the downloaded path - resolves it against the current directory's drive.
    // Those agree on a machine whose working directory is on C:, and disagree on CI, where the checkout
    // lives on D:.
    private static readonly string TestRoot = Path.GetFullPath("scarlet-tailwind-download-tests");
    private static readonly string ToolDirectory = Path.Combine(TestRoot, "tool");
    private static readonly string CacheRoot = Path.Combine(TestRoot, "cache");

    // Platform.LinuxX64's archive name (TailwindRuntimeResolver.GetDownloadName) - checksum verification looks
    // up this exact filename in the mocked sha256sums.txt.
    private const string ArchiveFileName = "tailwindcss-linux-x64";
    private static readonly byte[] ArchiveBytes = [1, 2, 3];
    private static readonly string ArchiveSha256 = Convert.ToHexString(SHA256.HashData(ArchiveBytes)).ToLowerInvariant();

    [Fact]
    public void Resolve_WithNothingCached_ShouldDownloadAndReportItAsDownloaded()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        MockChecksums(handler, "https://github.com/tailwindlabs/tailwindcss/releases/download/v1.4.2/sha256sums.txt");
        handler.When("https://github.com/tailwindlabs/tailwindcss/releases/download/v1.4.2/*")
            .Respond("application/zip", new MemoryStream(ArchiveBytes));

        // Act
        var resolution = Resolve(fileSystem, handler, version: "1.4.2");

        // Assert
        Assert.Equal(TailwindSource.Downloaded, resolution.Source);
        Assert.Equal(
            TailwindRuntimeResolver.GetExecutablePath(Path.Combine(CacheRoot, "runtimes", "1.4.2"), Platform.LinuxX64),
            resolution.ExecutablePath);
        Assert.True(fileSystem.File.Exists(resolution.ExecutablePath!));
    }

    [Fact]
    public void Resolve_WithCachedExecutableButNoVersionMarker_ShouldRedownloadInsteadOfUsingIt()
    {
        // Arrange - the marker is written last, so the executable alone is not a complete cache entry.
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        var cached = TailwindRuntimeResolver.GetExecutablePath(Path.Combine(CacheRoot, "runtimes", "1.4.2"), Platform.LinuxX64);
        fileSystem.AddFile(cached, new MockFileData("partially published"));

        MockChecksums(handler, "https://github.com/tailwindlabs/tailwindcss/releases/download/v1.4.2/sha256sums.txt");
        handler.Expect("https://github.com/tailwindlabs/tailwindcss/releases/download/v1.4.2/tailwindcss-linux-x64")
            .Respond("application/zip", new MemoryStream(ArchiveBytes));

        // Act
        var resolution = Resolve(fileSystem, handler, version: "1.4.2");

        // Assert
        Assert.Equal(TailwindSource.Downloaded, resolution.Source);
        Assert.True(fileSystem.File.Exists(TailwindDownloader.GetVersionMarkerPath(resolution.ExecutablePath!)));
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public void Resolve_ShouldRequestTheVersionScopedDirectory()
    {
        // Arrange - the CLI intentionally asks TailwindDownloader to use the cache directory scoped to the
        // requested version.
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        MockChecksums(handler, "https://github.com/tailwindlabs/tailwindcss/releases/download/v1.3.6/sha256sums.txt");
        handler.When("https://github.com/tailwindlabs/tailwindcss/releases/download/v1.3.6/*")
            .Respond("application/zip", new MemoryStream(ArchiveBytes));

        // Act
        var resolution = Resolve(fileSystem, handler, version: "1.3.6");

        // Assert
        Assert.Contains("1.3.6", resolution.RuntimeDirectory);
        Assert.DoesNotContain("1.4.2", resolution.RuntimeDirectory);
        Assert.Equal(TailwindSource.Downloaded, resolution.Source);
    }

    [Fact]
    public void Resolve_WithLatestRequested_ShouldAskForTheLatestRelease()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        MockChecksums(handler, "https://github.com/tailwindlabs/tailwindcss/releases/latest/download/sha256sums.txt");

        // Expect, not When: this asserts the URL shape rather than merely tolerating it
        handler.Expect("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64")
            .Respond("application/zip", new MemoryStream(ArchiveBytes));

        // Act
        var resolution = Resolve(fileSystem, handler, version: TailwindCliOptions.LatestVersion);

        // Assert
        handler.VerifyNoOutstandingExpectation();
        Assert.Equal(TailwindSource.Downloaded, resolution.Source);
    }

    private static void MockChecksums(MockHttpMessageHandler handler, string checksumsUrl)
    {
        handler.When(checksumsUrl).Respond("text/plain", $"{ArchiveSha256}  {ArchiveFileName}\n");
    }

    [Fact]
    public void Resolve_WhenTheDownloadFails_ShouldSurfaceTheFailure()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        using var handler = new MockHttpMessageHandler();

        handler.When("https://github.com/tailwindlabs/tailwindcss/releases/download/*")
            .Respond(System.Net.HttpStatusCode.NotFound);

        // Act & Assert - the application turns this into exit code 127 with the message attached
        var exception = Assert.ThrowsAny<Exception>(() => Resolve(fileSystem, handler, version: "9.9.9"));
        Assert.Contains("9.9.9", exception.Message);
    }

    private static TailwindResolution Resolve(MockFileSystem fileSystem, MockHttpMessageHandler handler, string version)
    {
        var options = TailwindCliOptions.FromEnvironment(
            new FakeEnvironmentProvider(new Dictionary<string, string>
            {
                [TailwindCliOptions.CacheVariable] = CacheRoot,
                [TailwindCliOptions.VersionVariable] = version
            }),
            TailwindBuildInfo.PinnedTailwindVersion);

        var resolver = new TailwindCliResolver(
            fileSystem,
            NoOpChmodProvider.Instance,
            Platform.LinuxX64,
            ToolDirectory,
            (platform, log) => new TailwindDownloader(
                new HttpClient(handler),
                new FakeLatestVersionResolver(resolvedVersion: null),
                fileSystem,
                NoOpChmodProvider.Instance,
                platform,
                log));

        return resolver.Resolve(options, allowDownload: true, new RecordingTailwindLogger());
    }

    private sealed class RecordingTailwindLogger : ITailwindLogger
    {
        public void LogMessage(string message)
        {
        }
    }
}
