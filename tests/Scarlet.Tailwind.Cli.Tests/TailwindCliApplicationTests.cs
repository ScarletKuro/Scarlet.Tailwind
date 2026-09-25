using System.ComponentModel;
using System.IO.Abstractions.TestingHelpers;
using Scarlet.Tailwind.Cli.Tests.Mock;

namespace Scarlet.Tailwind.Cli.Tests;

/// <summary>
/// The paths taken when something goes wrong, plus the diagnostics line the CLI e2e depends on.
/// </summary>
public class TailwindCliApplicationTests
{
    private const string ToolDirectory = "/tool";

    [Fact]
    public void Run_WithAnExplicitPathThatDoesNotExist_ShouldReportItAndNotLaunch()
    {
        // Arrange - the one way resolution reports failure without throwing
        var launcher = new RecordingProcessLauncher();
        var stderr = new StringWriter();

        var application = Create(
            new MockFileSystem(),
            launcher,
            stderr: stderr,
            variables: new Dictionary<string, string>
            {
                [TailwindCliOptions.CacheVariable] = "/cache",
                [TailwindCliOptions.PathVariable] = "/nowhere/tailwindcss"
            });

        // Act
        var result = application.Run(["--version"]);

        // Assert
        Assert.Equal(127, result);
        Assert.Null(launcher.Received);
        Assert.Contains(TailwindCliOptions.PathVariable, stderr.ToString());
        Assert.Contains("/nowhere/tailwindcss", stderr.ToString());
    }

    [Fact]
    public void Run_WithDiagnosticsEnabled_ShouldReportTheResolvedTailwindOnStderr()
    {
        // Arrange - tests/e2e/cli-tool/verify.sh greps for this exact wording to prove which Tailwind ran
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/tool/tailwindcss", new MockFileData("tailwind"));

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var application = Create(
            fileSystem,
            new RecordingProcessLauncher(),
            stdout,
            stderr,
            new Dictionary<string, string>
            {
                [TailwindCliOptions.CacheVariable] = "/cache",
                [TailwindCliOptions.DiagnosticsVariable] = "1"
            });

        // Act
        application.Run(["--version"]);

        // Assert
        Assert.Contains("Scarlet.Tailwind: using Tailwind at ", stderr.ToString());
        Assert.Contains("tailwind", stderr.ToString());

        // Nothing the tool says may reach stdout: `dotnet tailwind ... | jq` has to keep working
        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void Run_WithDiagnosticsDisabled_ShouldSayNothing()
    {
        // Arrange
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/tool/tailwindcss", new MockFileData("tailwind"));

        var stderr = new StringWriter();
        var application = Create(fileSystem, new RecordingProcessLauncher(), stderr: stderr);

        // Act
        application.Run(["--version"]);

        // Assert
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void Run_WhenTailwindCannotBeStarted_ShouldReportItAsNotExecutable()
    {
        // Arrange - the file exists but the OS refuses to exec it: a missing exec bit, a corrupt download,
        // or a binary for the wrong architecture
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile("/tool/tailwindcss", new MockFileData("tailwind"));

        var stderr = new StringWriter();
        var application = Create(fileSystem, new ThrowingProcessLauncher(), stderr: stderr);

        // Act
        var result = application.Run(["--version"]);

        // Assert
        Assert.Equal(126, result);
        Assert.Contains("failed to start", stderr.ToString());
    }

    [Fact]
    public void Run_WhenTheDownloadThrows_ShouldReportItRatherThanCrash()
    {
        // Arrange
        var stderr = new StringWriter();

        var resolver = new TailwindCliResolver(
            new MockFileSystem(),
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            ToolDirectory,
            (_, _) => throw new InvalidOperationException("github is down"));

        var application = new TailwindCliApplication(
            resolver,
            new RecordingProcessLauncher(),
            TailwindCliOptions.FromEnvironment(
                new FakeEnvironmentProvider(new Dictionary<string, string> { [TailwindCliOptions.CacheVariable] = "/cache" }),
                TailwindBuildInfo.PinnedTailwindVersion),
            new StringWriter(),
            stderr);

        // Act
        var result = application.Run(["--version"]);

        // Assert
        Assert.Equal(127, result);
        Assert.Contains("could not obtain Tailwind", stderr.ToString());
        Assert.Contains("github is down", stderr.ToString());
    }

    private static TailwindCliApplication Create(
        MockFileSystem fileSystem,
        IProcessLauncher launcher,
        TextWriter? stdout = null,
        TextWriter? stderr = null,
        IDictionary<string, string>? variables = null)
    {
        variables ??= new Dictionary<string, string> { [TailwindCliOptions.CacheVariable] = "/cache" };

        var resolver = new TailwindCliResolver(
            fileSystem,
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            ToolDirectory,
            (_, _) => throw new InvalidOperationException("The downloader must not be used in this test."));

        return new TailwindCliApplication(
            resolver,
            launcher,
            TailwindCliOptions.FromEnvironment(new FakeEnvironmentProvider(variables), TailwindBuildInfo.PinnedTailwindVersion),
            stdout ?? new StringWriter(),
            stderr ?? new StringWriter());
    }

    private sealed class ThrowingProcessLauncher : IProcessLauncher
    {
        public int Run(TailwindLaunchRequest request) => throw new Win32Exception(13, "Permission denied");
    }
}
