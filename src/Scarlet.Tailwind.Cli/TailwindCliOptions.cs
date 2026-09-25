using System.Globalization;

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Everything the tool can be configured with.
/// </summary>
/// <remarks>
/// Configuration is environment variables rather than command-line flags because every argument belongs to
/// Tailwind: <c>dotnet tailwind --version</c> has to print Tailwind's version, and a flag Tailwind adds tomorrow has to keep
/// working without a release of this package. The <c>SCARLET_TAILWIND_</c> prefix cannot collide with Tailwind's own
/// <c>BUN_*</c> or <c>NODE_*</c> variables.
/// </remarks>
internal sealed class TailwindCliOptions
{
    /// <summary>Name of the variable holding an explicit Tailwind executable path.</summary>
    public const string PathVariable = "SCARLET_TAILWIND_PATH";

    /// <summary>Name of the variable selecting which Tailwind version to resolve.</summary>
    public const string VersionVariable = "SCARLET_TAILWIND_VERSION";

    /// <summary>Name of the variable overriding the cache root.</summary>
    public const string CacheVariable = "SCARLET_TAILWIND_CACHE";

    /// <summary>Name of the variable that suppresses use of the embedded binary.</summary>
    public const string NoEmbeddedVariable = "SCARLET_TAILWIND_NO_EMBEDDED";

    /// <summary>Name of the variable that disables the reserved diagnostic flag entirely.</summary>
    public const string PassthroughVariable = "SCARLET_TAILWIND_PASSTHROUGH";

    /// <summary>Name of the variable that reports the resolved Tailwind on stderr before running it.</summary>
    public const string DiagnosticsVariable = "SCARLET_TAILWIND_DIAGNOSTICS";

    /// <summary>Name of the variable overriding how long to wait for a concurrent download.</summary>
    public const string DownloadTimeoutVariable = "SCARLET_TAILWIND_DOWNLOAD_TIMEOUT";

    /// <summary>The version token meaning "whatever GitHub currently marks as latest".</summary>
    public const string LatestVersion = "latest";

    private const int DefaultDownloadTimeoutSeconds = 300;

    private TailwindCliOptions(
        string? explicitTailwindPath,
        string? requestedVersionOverride,
        string requestedVersion,
        string? cacheRootOverride,
        string cacheRoot,
        bool ignoreEmbedded,
        bool purePassthrough,
        bool diagnostics,
        string? downloadTimeoutOverride,
        int downloadTimeoutSeconds)
    {
        ExplicitTailwindPath = explicitTailwindPath;
        RequestedVersionOverride = requestedVersionOverride;
        RequestedVersion = requestedVersion;
        CacheRootOverride = cacheRootOverride;
        CacheRoot = cacheRoot;
        IgnoreEmbedded = ignoreEmbedded;
        PurePassthrough = purePassthrough;
        Diagnostics = diagnostics;
        DownloadTimeoutOverride = downloadTimeoutOverride;
        DownloadTimeoutSeconds = downloadTimeoutSeconds;
    }

    /// <summary>An explicit Tailwind executable supplied by the user, or <see langword="null"/>.</summary>
    public string? ExplicitTailwindPath { get; }

    /// <summary>
    /// The raw value of <see cref="VersionVariable"/> as set in the environment, or <see langword="null"/>
    /// if it was not set. Unlike <see cref="RequestedVersion"/>, this does not fall back to the pinned
    /// version, so diagnostics can tell an explicit override apart from the default.
    /// </summary>
    public string? RequestedVersionOverride { get; }

    /// <summary>The Tailwind version to resolve. Either a concrete version or <see cref="LatestVersion"/>.</summary>
    public string RequestedVersion { get; }

    /// <summary>
    /// The raw value of <see cref="CacheVariable"/> as set in the environment, or <see langword="null"/>
    /// if it was not set. Unlike <see cref="CacheRoot"/>, this does not fall back to the OS default, so
    /// diagnostics can tell an explicit override apart from the default.
    /// </summary>
    public string? CacheRootOverride { get; }

    /// <summary>Root directory for downloaded Tailwind runtimes.</summary>
    public string CacheRoot { get; }

    /// <summary>Whether the binary embedded in the package should be ignored.</summary>
    public bool IgnoreEmbedded { get; }

    /// <summary>Whether the reserved diagnostic flag is disabled, making argument forwarding absolute.</summary>
    public bool PurePassthrough { get; }

    /// <summary>Whether to report the resolved Tailwind on stderr before running it.</summary>
    public bool Diagnostics { get; }

    /// <summary>
    /// The raw value of <see cref="DownloadTimeoutVariable"/> as set in the environment, or
    /// <see langword="null"/> if it was not set. Unlike <see cref="DownloadTimeoutSeconds"/>, this does not
    /// fall back to the default, so diagnostics can tell an explicit override apart from the default.
    /// </summary>
    public string? DownloadTimeoutOverride { get; }

    /// <summary>How long to wait for another process that is already downloading Tailwind.</summary>
    public int DownloadTimeoutSeconds { get; }

    /// <summary>Whether the requested version is the floating "latest".</summary>
    public bool UseLatest => string.Equals(RequestedVersion, LatestVersion, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The directory handed to the downloader. It is version-scoped on purpose.
    /// </summary>
    /// <remarks>
    /// Keeping the CLI cache version-scoped makes manual inspection and cleanup straightforward, and keeps
    /// older tool versions from accidentally reusing a runtime downloaded for a different requested version.
    /// </remarks>
    public string RuntimeDirectory => Path.Combine(CacheRoot, "runtimes", RequestedVersion);

    /// <summary>
    /// The version to pass to the downloader: <see langword="null"/> asks it for the latest release.
    /// </summary>
    public string? DownloadVersion => UseLatest ? null : RequestedVersion;

    /// <summary>
    /// Reads the configuration from the environment.
    /// </summary>
    /// <param name="environment">The environment to read.</param>
    /// <param name="pinnedVersion">The Tailwind version this package was built against, used when none is requested.</param>
    /// <returns>The resolved options.</returns>
    public static TailwindCliOptions FromEnvironment(IEnvironmentProvider environment, string pinnedVersion)
    {
        var requestedVersion = environment.GetVariable(VersionVariable)?.Trim();
        var requestedVersionOverride = string.IsNullOrEmpty(requestedVersion) ? null : requestedVersion;

        var cacheRootOverride = environment.GetVariable(CacheVariable)?.Trim();
        cacheRootOverride = string.IsNullOrEmpty(cacheRootOverride) ? null : cacheRootOverride;

        var downloadTimeoutOverride = environment.GetVariable(DownloadTimeoutVariable)?.Trim();
        downloadTimeoutOverride = string.IsNullOrEmpty(downloadTimeoutOverride) ? null : downloadTimeoutOverride;

        return new TailwindCliOptions(
            explicitTailwindPath: environment.GetVariable(PathVariable)?.Trim(),
            requestedVersionOverride: requestedVersionOverride,
            requestedVersion: requestedVersionOverride ?? pinnedVersion,
            cacheRootOverride: cacheRootOverride,
            cacheRoot: ResolveCacheRoot(environment),
            ignoreEmbedded: IsEnabled(environment.GetVariable(NoEmbeddedVariable)),
            purePassthrough: IsEnabled(environment.GetVariable(PassthroughVariable)),
            diagnostics: IsEnabled(environment.GetVariable(DiagnosticsVariable)),
            downloadTimeoutOverride: downloadTimeoutOverride,
            downloadTimeoutSeconds: ReadTimeout(downloadTimeoutOverride));
    }

    /// <summary>
    /// Picks the per-user cache root for the host platform.
    /// </summary>
    /// <remarks>
    /// Written out per OS rather than using <see cref="Environment.SpecialFolder.LocalApplicationData"/>
    /// everywhere, because .NET maps that to <c>~/.local/share</c> on macOS, which is not where a macOS
    /// user expects a cache to live.
    /// </remarks>
    internal static string ResolveCacheRoot(IEnvironmentProvider environment)
    {
        var overridden = environment.GetVariable(CacheVariable)?.Trim();
        if (!string.IsNullOrEmpty(overridden))
        {
            return overridden;
        }

        var home = environment.HomeDirectory;

        if (environment.IsWindows)
        {
            var localAppData = environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            return Combine(localAppData) ?? Combine(home) ?? Fallback(environment);
        }

        if (environment.IsMacOs)
        {
            return Combine(home, "Library", "Caches") ?? Fallback(environment);
        }

        var xdgCache = environment.GetVariable("XDG_CACHE_HOME")?.Trim();

        return Combine(xdgCache) ?? Combine(home, ".cache") ?? Fallback(environment);
    }

    private static string? Combine(string? root, params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var parts = new List<string> { root };
        parts.AddRange(segments);
        parts.Add("ScarletKuro");
        parts.Add("Scarlet.Tailwind");

        return Path.Combine(parts.ToArray());
    }

    // Containers frequently run without HOME set. Falling back to temp keeps the tool usable there instead
    // of failing on a path it could not build.
    private static string Fallback(IEnvironmentProvider environment) =>
        Path.Combine(environment.TempDirectory, "ScarletKuro", "Scarlet.Tailwind");

    private static bool IsEnabled(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        return trimmed is "1"
            || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadTimeout(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0)
        {
            return seconds;
        }

        return DefaultDownloadTimeoutSeconds;
    }
}
