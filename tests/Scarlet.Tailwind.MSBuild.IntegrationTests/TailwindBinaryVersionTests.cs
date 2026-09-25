using System.IO.Abstractions;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Scarlet.Tailwind.MSBuild.IntegrationTests;

/// <summary>
/// Asserts that the Tailwind binary staged for this platform really is the version the repository claims.
/// </summary>
/// <remarks>
/// <para>
/// A download script can extract into the project directory and then accidentally find the
/// <em>existing</em> executable before the freshly downloaded one. The resulting move becomes a no-op,
/// while the version marker is still updated, leaving an old binary relabelled as the new version.
/// </para>
/// <para>
/// Nothing else catches that: the marker file agrees with itself, and a binary for another platform cannot
/// be executed to check. Only the host platform's binary can be asked, which is why this runs per CI leg.
/// </para>
/// </remarks>
public class TailwindBinaryVersionTests
{
    /// <summary>
    /// Tailwind prints its version in the banner that heads its help output.
    /// </summary>
    /// <remarks>
    /// There is no <c>--version</c> flag, and asking for one is actively misleading: unrecognised flags are
    /// silently discarded, so <c>tailwindcss --version</c> ignores the flag, reads stdin and prints a
    /// compiled stylesheet. The banner is the only place the version is reported as such.
    ///
    /// The banner is styled with ANSI escapes even when stdout is redirected, so the pattern has to tolerate
    /// them rather than anchoring on surrounding characters.
    /// </remarks>
    private static readonly Regex VersionBanner = new(@"v(?<version>\d+\.\d+\.\d+[^\s\u001b]*)", RegexOptions.Compiled);

    [Fact]
    public void StagedTailwindBinary_ShouldReportTheVersionTheRepositoryPinned()
    {
        // Arrange - resolved through the resolver rather than by composing the path here, because the
        // staged layout does not always use the host's own RID: on Windows ARM64 the binary is the x64 one,
        // stored under win-x64. Going through the resolver also means this test exercises the same lookup a
        // consumer gets.
        var expectedVersion = ReadPinnedTailwindVersion();
        var platform = TailwindRuntimeResolver.GetCurrentPlatform();
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");

        Assert.True(
            Directory.Exists(runtimesDirectory),
            $"No staged runtimes at '{runtimesDirectory}'. Build Scarlet.Tailwind.MSBuild first.");

        var tailwindPath = TailwindRuntimeResolver.ResolveTailwindExecutable(
            new FileSystem(),
            Chmod.CreateProvider(),
            platform,
            runtimesDirectory);

        // Act
        var reportedVersion = ReadReportedVersion(tailwindPath);

        // Assert
        Assert.Equal(expectedVersion, reportedVersion);
    }

    private static string ReadPinnedTailwindVersion()
    {
        var root = XDocument.Load(Path.Combine(RepositoryRoot.Path, "Directory.Build.props")).Root;
        Assert.NotNull(root);

        return Assert.Single(root.Descendants("TailwindVersion")).Value.Trim();
    }

    private static string ReadReportedVersion(string tailwindPath)
    {
        var startInfo = new ProcessStartInfo(tailwindPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--help");

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var match = VersionBanner.Match(output);
        Assert.True(match.Success, $"Could not find a version banner in:\n{output}");

        return match.Groups["version"].Value;
    }
}
