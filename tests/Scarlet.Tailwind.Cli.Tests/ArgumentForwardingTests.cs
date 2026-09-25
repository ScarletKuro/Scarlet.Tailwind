using System.IO.Abstractions.TestingHelpers;
using Scarlet.Tailwind.Cli.Tests.Mock;

namespace Scarlet.Tailwind.Cli.Tests;

/// <summary>
/// The contract that matters most: every argument reaches Tailwind unchanged, in order.
/// </summary>
/// <remarks>
/// <c>dotnet tailwind --version</c> has to print Tailwind's version, not the tool's, and a flag Tailwind adds tomorrow has
/// to work without a release of this package. Anything that parses, reorders, trims or re-quotes arguments
/// breaks that.
/// </remarks>
public class ArgumentForwardingTests
{
    public static TheoryData<string[]> Arguments =>
    [
        [],
        ["--version"],
        ["--help"],
        ["install"],
        ["run", "build.mjs"],
        ["-e", "console.log('a b')"],
        ["run", "x", "--", "--watch"],
        ["--"],
        ["--define", "X=\"y\""],
        ["a b"],
        [string.Empty],
        ["trailing\\"],
        ["日本語"],
        ["--flag=va lue", "second", string.Empty, "-"]
    ];

    [Theory]
    [MemberData(nameof(Arguments))]
    public void Run_ShouldForwardEveryArgumentVerbatim(string[] args)
    {
        // Arrange
        var launcher = new RecordingProcessLauncher();
        var application = CreateApplication(launcher, out _);

        // Act
        application.Run(args);

        // Assert
        Assert.Equal(args, launcher.ReceivedArguments);
    }

    [Fact]
    public void Run_ShouldReturnTailwindsExitCodeUnchanged()
    {
        // Arrange - 130 is the shell's "terminated by SIGINT"; it must survive as-is
        foreach (var exitCode in new[] { 0, 1, 2, 3, 130, 255 })
        {
            var application = CreateApplication(new RecordingProcessLauncher(exitCode), out _);

            // Act
            var result = application.Run(["run", "build.mjs"]);

            // Assert
            Assert.Equal(exitCode, result);
        }
    }

    [Fact]
    public void Run_ShouldPassTheResolvedExecutableToTheLauncher()
    {
        // Arrange
        var launcher = new RecordingProcessLauncher();
        var application = CreateApplication(launcher, out var embeddedPath);

        // Act
        application.Run(["--version"]);

        // Assert
        Assert.NotNull(launcher.Received);
        Assert.Equal(embeddedPath, launcher.Received!.Value.ExecutablePath);
    }

    [Fact]
    public void Run_WhenNothingCanBeResolved_ShouldReportAndNotLaunch()
    {
        // Arrange - no embedded binary, and a downloader that would fail the test if it were used
        var fileSystem = new MockFileSystem();
        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [TailwindCliOptions.CacheVariable] = "/cache"
        });
        var launcher = new RecordingProcessLauncher();
        var stderr = new StringWriter();

        var resolver = new TailwindCliResolver(
            fileSystem,
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            "/tool",
            (_, _) => throw new InvalidOperationException("boom"));

        var application = new TailwindCliApplication(
            resolver,
            launcher,
            TailwindCliOptions.FromEnvironment(environment, TailwindBuildInfo.PinnedTailwindVersion),
            new StringWriter(),
            stderr);

        // Act
        var result = application.Run(["--version"]);

        // Assert
        Assert.Equal(127, result);
        Assert.Null(launcher.Received);
        Assert.Contains("Scarlet.Tailwind:", stderr.ToString());
    }

    private static TailwindCliApplication CreateApplication(IProcessLauncher launcher, out string embeddedPath)
    {
        const string toolDirectory = "/tool";
        embeddedPath = Path.Combine(toolDirectory, "tailwindcss");

        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(embeddedPath, new MockFileData("fake tailwind"));

        var environment = new FakeEnvironmentProvider(new Dictionary<string, string>
        {
            [TailwindCliOptions.CacheVariable] = "/cache"
        });

        var resolver = new TailwindCliResolver(
            fileSystem,
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            toolDirectory,
            (_, _) => throw new InvalidOperationException("The embedded binary must be used without downloading."));

        return new TailwindCliApplication(
            resolver,
            launcher,
            TailwindCliOptions.FromEnvironment(environment, TailwindBuildInfo.PinnedTailwindVersion),
            new StringWriter(),
            new StringWriter());
    }
}
