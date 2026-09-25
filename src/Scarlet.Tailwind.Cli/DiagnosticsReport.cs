using System.Text;
using System.Text.Json;
using Scarlet.Tailwind.Core;

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Renders what the tool resolved and why.
/// </summary>
internal static class DiagnosticsReport
{
    /// <summary>
    /// Renders the human-readable report.
    /// </summary>
    /// <param name="resolution">The resolution to describe.</param>
    /// <param name="options">The configuration in effect.</param>
    /// <returns>The report, ending in a newline.</returns>
    public static string ToText(TailwindResolution resolution, TailwindCliOptions options)
    {
        var report = new StringBuilder();

        report.AppendLine();
        Append(report, "Scarlet.Tailwind.Cli", DescribePackage());
        Append(report, "Pinned Tailwind version", TailwindBuildInfo.PinnedTailwindVersion);
        Append(report, "Host platform", $"{resolution.Platform} ({resolution.RuntimeIdentifier})");
        Append(report, "Tool directory", AppContext.BaseDirectory);
        report.AppendLine();

        Append(report, "Tailwind executable", resolution.ExecutablePath ?? "(not present)");
        Append(report, "Source", DescribeSource(resolution));
        Append(report, "Requested version", resolution.RequestedVersion);
        Append(report, "Embedded probe path", resolution.EmbeddedProbePath);
        Append(report, "Cache root", resolution.CacheRoot);
        Append(report, "Runtime directory", resolution.RuntimeDirectory);
        Append(report, "Download URL", DescribeDownloadUrl(resolution));
        report.AppendLine();

        report.AppendLine("Environment");
        foreach (var (name, value) in DescribeEnvironment(options))
        {
            report.AppendLine($"  {name.PadRight(EnvironmentLabelWidth)}{value}");
        }

        return report.ToString();
    }

    /// <summary>
    /// Renders the same facts as a single JSON object, for scripting.
    /// </summary>
    /// <param name="resolution">The resolution to describe.</param>
    /// <param name="options">The configuration in effect.</param>
    /// <returns>Indented JSON, ending in a newline.</returns>
    public static string ToJson(TailwindResolution resolution, TailwindCliOptions options)
    {
        var payload = new Dictionary<string, object?>
        {
            ["package"] = DescribePackage(),
            ["pinnedTailwindVersion"] = TailwindBuildInfo.PinnedTailwindVersion,
            ["platform"] = resolution.Platform.ToString(),
            ["runtimeIdentifier"] = resolution.RuntimeIdentifier,
            ["toolDirectory"] = AppContext.BaseDirectory,
            ["tailwindExecutable"] = resolution.ExecutablePath,
            ["source"] = resolution.Source.ToString().ToLowerInvariant(),
            ["requestedVersion"] = resolution.RequestedVersion,
            ["embeddedProbePath"] = resolution.EmbeddedProbePath,
            ["cacheRoot"] = resolution.CacheRoot,
            ["runtimeDirectory"] = resolution.RuntimeDirectory,
            ["downloadUrl"] = resolution.IsResolved ? null : BuildDownloadUrl(resolution),
            ["failureReason"] = resolution.FailureReason,
            ["environment"] = new Dictionary<string, object?>
            {
                [TailwindCliOptions.PathVariable] = options.ExplicitTailwindPath,
                [TailwindCliOptions.VersionVariable] = options.RequestedVersionOverride,
                [TailwindCliOptions.CacheVariable] = options.CacheRootOverride,
                [TailwindCliOptions.NoEmbeddedVariable] = options.IgnoreEmbedded,
                [TailwindCliOptions.PassthroughVariable] = options.PurePassthrough,
                [TailwindCliOptions.DiagnosticsVariable] = options.Diagnostics,
                [TailwindCliOptions.DownloadTimeoutVariable] = options.DownloadTimeoutOverride
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private static string DescribePackage() => DescribePackage(TailwindBuildInfo.PackagedRuntimeIdentifier);

    /// <summary>
    /// Names the package this build came from.
    /// </summary>
    /// <param name="packagedRuntimeIdentifier">The RID baked in at build time, empty for the portable package.</param>
    /// <returns>For example <c>Scarlet.Tailwind.Cli.win-x64</c>.</returns>
    /// <remarks>
    /// Takes the identifier rather than reading the constant so both branches are reachable from a test:
    /// the test build has no runtime identifier, so it could only ever exercise the portable one.
    /// </remarks>
    internal static string DescribePackage(string packagedRuntimeIdentifier)
    {
        return string.IsNullOrEmpty(packagedRuntimeIdentifier)
            ? "Scarlet.Tailwind.Cli (portable)"
            : $"Scarlet.Tailwind.Cli.{packagedRuntimeIdentifier}";
    }

    private static string DescribeSource(TailwindResolution resolution)
    {
        return resolution.Source switch
        {
            TailwindSource.Embedded => "embedded - shipped in this package, no network required",
            TailwindSource.Cache => "cache - downloaded by an earlier run",
            TailwindSource.Downloaded => "downloaded - fetched during this run",
            TailwindSource.Explicit => $"explicit - {TailwindCliOptions.PathVariable}",
            _ => "not found - would download on the next run"
        };
    }

    private static string DescribeDownloadUrl(TailwindResolution resolution)
    {
        return resolution.IsResolved ? "(not needed)" : BuildDownloadUrl(resolution);
    }

    private static string BuildDownloadUrl(TailwindResolution resolution)
    {
        // The asset IS the executable - Tailwind publishes no archives - so this is the whole URL, with no
        // extension appended.
        var asset = TailwindRuntimeResolver.GetDownloadName(resolution.Platform);

        return string.Equals(resolution.RequestedVersion, TailwindCliOptions.LatestVersion, StringComparison.OrdinalIgnoreCase)
            ? $"https://github.com/tailwindlabs/tailwindcss/releases/latest/download/{asset}"
            : $"https://github.com/tailwindlabs/tailwindcss/releases/download/v{resolution.RequestedVersion}/{asset}";
    }

    private static IEnumerable<(string Name, string Value)> DescribeEnvironment(TailwindCliOptions options)
    {
        yield return (TailwindCliOptions.PathVariable, options.ExplicitTailwindPath ?? "(unset)");
        yield return (TailwindCliOptions.VersionVariable, options.RequestedVersionOverride ?? "(unset)");
        yield return (TailwindCliOptions.CacheVariable, options.CacheRootOverride ?? "(unset)");
        yield return (TailwindCliOptions.NoEmbeddedVariable, options.IgnoreEmbedded ? "enabled" : "(unset)");
        yield return (TailwindCliOptions.PassthroughVariable, options.PurePassthrough ? "enabled" : "(unset)");
        yield return (TailwindCliOptions.DiagnosticsVariable, options.Diagnostics ? "enabled" : "(unset)");
        yield return (TailwindCliOptions.DownloadTimeoutVariable, options.DownloadTimeoutOverride ?? "(unset)");
    }

    /// <summary>
    /// Column width for the labels in the top two blocks.
    /// </summary>
    /// <remarks>
    /// Must exceed the longest label ("Pinned Tailwind version", 23) or the label and its value run
    /// together with no separator. DiagnosticsReportTests asserts that for every line rather than trusting
    /// this number to be kept in step by hand.
    /// </remarks>
    private const int LabelWidth = 25;

    /// <summary>
    /// Column width for the environment block, whose names are longer than any other label
    /// ("SCARLET_TAILWIND_DOWNLOAD_TIMEOUT", 33).
    /// </summary>
    private const int EnvironmentLabelWidth = 35;

    private static void Append(StringBuilder report, string label, string value) =>
        report.AppendLine($"{label.PadRight(LabelWidth)}{value}");
}
