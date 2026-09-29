using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Scarlet.Tailwind.Cli.Watch;

namespace Scarlet.Tailwind.Cli.Tests.Watch;

public class TailwindWatchCommandTests
{
    [Fact]
    public void Run_ShouldUseTheCurrentProjectAndDebugConfigurationByDefault()
    {
        var provider = new RecordingConfigurationProvider();
        var launcher = new RecordingWatchLauncher();
        var stderr = new StringWriter();
        var command = new TailwindWatchCommand(
            provider,
            launcher,
            new RecordingSessionLockProvider(),
            new StringWriter(),
            stderr,
            () => "/repo/app");

        var result = command.Run([]);

        Assert.Equal(0, result);
        Assert.Null(provider.Project);
        Assert.Equal("Debug", provider.Configuration);
        Assert.Equal("/repo/app", provider.CurrentDirectory);
        Assert.NotNull(launcher.Invocations);
        Assert.Equal(provider.Invocations, launcher.Invocations);
        Assert.Contains("watching 1 Tailwind entry point", stderr.ToString());
    }

    [Fact]
    public void Run_ShouldPassProjectAndConfigurationOptionsToMsBuildResolution()
    {
        var provider = new RecordingConfigurationProvider();
        var command = new TailwindWatchCommand(
            provider,
            new RecordingWatchLauncher(),
            new RecordingSessionLockProvider(),
            new StringWriter(),
            new StringWriter(),
            () => "/repo");

        var result = command.Run(["--project", "src/App.csproj", "-c", "Release"]);

        Assert.Equal(0, result);
        Assert.Equal("src/App.csproj", provider.Project);
        Assert.Equal("Release", provider.Configuration);
    }

    [Fact]
    public void Run_ShouldAcceptInlineLongOptions()
    {
        var provider = new RecordingConfigurationProvider();
        var command = new TailwindWatchCommand(
            provider,
            new RecordingWatchLauncher(),
            new RecordingSessionLockProvider(),
            new StringWriter(),
            new StringWriter(),
            () => "/repo");

        var result = command.Run(["--project=src/App.csproj", "--configuration=Release"]);

        Assert.Equal(ExitCodes.Success, result);
        Assert.Equal("src/App.csproj", provider.Project);
        Assert.Equal("Release", provider.Configuration);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Run_WithHelp_ShouldPrintUsageWithoutResolvingTheProject(string option)
    {
        var provider = new RecordingConfigurationProvider();
        var stdout = new StringWriter();
        var command = new TailwindWatchCommand(
            provider,
            new RecordingWatchLauncher(),
            new RecordingSessionLockProvider(),
            stdout,
            new StringWriter(),
            () => "/repo");

        var result = command.Run([option]);

        Assert.Equal(ExitCodes.Success, result);
        Assert.Null(provider.Configuration);
        Assert.Contains("Usage: dotnet tailwind watch [options]", stdout.ToString());
    }

    [Fact]
    public void Run_WithNoConfiguredEntries_ShouldNotStartAWatchProcess()
    {
        var provider = new RecordingConfigurationProvider { Invocations = [] };
        var launcher = new RecordingWatchLauncher();
        var stderr = new StringWriter();
        var command = new TailwindWatchCommand(
            provider,
            launcher,
            new RecordingSessionLockProvider(),
            new StringWriter(),
            stderr,
            () => "/repo");

        var result = command.Run([]);

        Assert.Equal(ExitCodes.UsageError, result);
        Assert.Null(launcher.Invocations);
        Assert.Contains("no enabled", stderr.ToString());
    }

    [Theory]
    [InlineData("--project")]
    [InlineData("--configuration")]
    [InlineData("--unknown")]
    public void Run_WithAnInvalidOption_ShouldReturnAUsageError(string option)
    {
        var provider = new RecordingConfigurationProvider();
        var command = new TailwindWatchCommand(
            provider,
            new RecordingWatchLauncher(),
            new RecordingSessionLockProvider(),
            new StringWriter(),
            new StringWriter(),
            () => "/repo");

        var result = command.Run([option]);

        Assert.Equal(ExitCodes.UsageError, result);
        Assert.Null(provider.Configuration);
    }

    [Theory]
    [InlineData("--project=")]
    [InlineData("--configuration=   ")]
    public void Run_WithAnEmptyInlineOption_ShouldReturnAUsageError(string option)
    {
        var provider = new RecordingConfigurationProvider();
        var stderr = new StringWriter();
        var command = new TailwindWatchCommand(
            provider,
            new RecordingWatchLauncher(),
            new RecordingSessionLockProvider(),
            new StringWriter(),
            stderr,
            () => "/repo");

        var result = command.Run([option]);

        Assert.Equal(ExitCodes.UsageError, result);
        Assert.Null(provider.Configuration);
        Assert.Contains("requires a non-empty value", stderr.ToString());
    }

    [Fact]
    public void Run_WhenConfigurationResolutionFails_ShouldReportTheWatchError()
    {
        var stderr = new StringWriter();
        var command = new TailwindWatchCommand(
            new ThrowingConfigurationProvider(
                new TailwindWatchException("could not evaluate the project", ExitCodes.TailwindNotFound)),
            new RecordingWatchLauncher(),
            new RecordingSessionLockProvider(),
            new StringWriter(),
            stderr,
            () => "/repo");

        var result = command.Run([]);

        Assert.Equal(ExitCodes.TailwindNotFound, result);
        Assert.Contains("Scarlet.Tailwind: could not evaluate the project", stderr.ToString());
    }

    [Fact]
    public void Run_WhenAWatchProcessCannotStart_ShouldReportTheNativeError()
    {
        var stderr = new StringWriter();
        var command = new TailwindWatchCommand(
            new RecordingConfigurationProvider(),
            new ThrowingWatchLauncher(new Win32Exception(13, "Permission denied")),
            new RecordingSessionLockProvider(),
            new StringWriter(),
            stderr,
            () => "/repo");

        var result = command.Run([]);

        Assert.Equal(ExitCodes.TailwindNotExecutable, result);
        Assert.Contains("failed to start a watch process: Permission denied", stderr.ToString());
    }

    [Fact]
    public void Run_WhenAnOutputIsAlreadyWatched_ShouldExitWithoutStartingAnotherProcess()
    {
        var launcher = new RecordingWatchLauncher();
        var stderr = new StringWriter();
        var command = new TailwindWatchCommand(
            new RecordingConfigurationProvider(),
            launcher,
            new RecordingSessionLockProvider { AcquiredLock = null },
            new StringWriter(),
            stderr,
            () => "/repo");

        var result = command.Run([]);

        Assert.Equal(ExitCodes.Success, result);
        Assert.Null(launcher.Invocations);
        Assert.Contains("already running", stderr.ToString());
    }

    private sealed class RecordingConfigurationProvider : ITailwindWatchConfigurationProvider
    {
        public IReadOnlyList<TailwindWatchInvocation> Invocations { get; set; } =
        [
            new(
                new TailwindLaunchRequest("tailwindcss", ["--input=/repo/Styles/app.css", "--watch=always"]),
                "/repo",
                "/repo/Styles/app.css",
                "/repo/wwwroot/css/app.css",
                ["/repo/wwwroot/css/app.css"])
        ];

        public string? Project { get; private set; }
        public string? Configuration { get; private set; }
        public string? CurrentDirectory { get; private set; }

        public IReadOnlyList<TailwindWatchInvocation> Resolve(
            string? project,
            string configuration,
            string currentDirectory)
        {
            Project = project;
            Configuration = configuration;
            CurrentDirectory = currentDirectory;
            return Invocations;
        }
    }

    private sealed class RecordingWatchLauncher : IWatchProcessLauncher
    {
        public IEnumerable<TailwindWatchInvocation>? Invocations { get; private set; }

        public int Run(IEnumerable<TailwindWatchInvocation> invocations)
        {
            Invocations = invocations;
            return 0;
        }
    }

    private sealed class ThrowingConfigurationProvider(TailwindWatchException exception)
        : ITailwindWatchConfigurationProvider
    {
        public IReadOnlyList<TailwindWatchInvocation> Resolve(
            string? project,
            string configuration,
            string currentDirectory) => throw exception;
    }

    private sealed class ThrowingWatchLauncher(Win32Exception exception) : IWatchProcessLauncher
    {
        public int Run(IEnumerable<TailwindWatchInvocation> invocations) => throw exception;
    }

    private sealed class RecordingSessionLockProvider : IWatchSessionLockProvider
    {
        public IDisposable? AcquiredLock { get; set; } = new NoopDisposable();

        public bool TryAcquire(
            IReadOnlyList<TailwindWatchInvocation> invocations,
            [NotNullWhen(true)] out IDisposable? lease)
        {
            lease = AcquiredLock;
            return lease is not null;
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
