using System.Collections;
using System.Diagnostics;
using Microsoft.Build.Framework;

namespace Scarlet.Tailwind.MSBuild.Tests;

public sealed class TailwindStartWatchTaskTests
{
    [Fact]
    public void Execute_ShouldProbeThenShellLaunchTheProjectAwareWatcher()
    {
        var runner = new RecordingProcessRunner();
        var buildEngine = new RecordingBuildEngine();
        var projectPath = Path.Combine(Path.GetTempPath(), "project with spaces", "App.csproj");
        var task = new TailwindStartWatchTask(runner, name => name == "DOTNET_HOST_PATH" ? "/custom/dotnet" : null)
        {
            BuildEngine = buildEngine,
            ProjectPath = projectPath,
            Configuration = "Release"
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.NotNull(runner.ProbeStartInfo);
        Assert.Equal("/custom/dotnet", runner.ProbeStartInfo.FileName);
        Assert.Equal(Path.GetDirectoryName(projectPath), runner.ProbeStartInfo.WorkingDirectory);
        Assert.False(runner.ProbeStartInfo.UseShellExecute);
        Assert.True(runner.ProbeStartInfo.CreateNoWindow);
        Assert.True(runner.ProbeStartInfo.RedirectStandardOutput);
        Assert.True(runner.ProbeStartInfo.RedirectStandardError);
        Assert.Equal(
            TailwindCommandLine.BuildProcessArguments(["tailwind", "--scarlet-info", "--json"]),
            runner.ProbeStartInfo.Arguments);

        Assert.NotNull(runner.WatchStartInfo);
        Assert.Equal("/custom/dotnet", runner.WatchStartInfo.FileName);
        Assert.True(runner.WatchStartInfo.UseShellExecute);
        Assert.False(runner.WatchStartInfo.CreateNoWindow);
        Assert.False(runner.WatchStartInfo.RedirectStandardOutput);
        Assert.False(runner.WatchStartInfo.RedirectStandardError);
        Assert.Equal(
            TailwindCommandLine.BuildProcessArguments(
                ["tailwind", "watch", "--project", projectPath, "--configuration", "Release"]),
            runner.WatchStartInfo.Arguments);
        Assert.Empty(buildEngine.Errors);
        Assert.Contains(buildEngine.Messages, message => message.Message?.Contains(projectPath, StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Execute_WithBlankConfigurationAndDotnetHostPath_ShouldUseDefaults()
    {
        var runner = new RecordingProcessRunner();
        var task = CreateTask(runner, name => name == "DOTNET_HOST_PATH" ? "  " : null);
        task.Configuration = " ";

        var result = task.Execute();

        Assert.True(result);
        Assert.Equal("dotnet", runner.ProbeStartInfo!.FileName);
        Assert.Contains("--configuration Debug", runner.WatchStartInfo!.Arguments, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "command was not found")]
    [InlineData(0, "{}")]
    [InlineData(0, "")]
    public void Execute_WhenTheCliIsIncompatible_ShouldFailWithoutStartingAWatcher(int exitCode, string output)
    {
        var runner = new RecordingProcessRunner
        {
            ProbeResult = new TailwindWatchProbeResult(exitCode, output)
        };
        var buildEngine = new RecordingBuildEngine();
        var task = CreateTask(runner, buildEngine: buildEngine);

        var result = task.Execute();

        Assert.False(result);
        Assert.Null(runner.WatchStartInfo);
        var error = Assert.Single(buildEngine.Errors);
        Assert.Contains("requires a compatible Scarlet.Tailwind.Cli dotnet tool", error.Message, StringComparison.Ordinal);
        if (output.Length > 0)
        {
            Assert.Contains(output, error.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("1")]
    [InlineData(" true ")]
    [InlineData("YES")]
    public void Execute_WhenPassthroughIsEnabled_ShouldFailBeforeProbing(string value)
    {
        var runner = new RecordingProcessRunner();
        var buildEngine = new RecordingBuildEngine();
        var task = CreateTask(
            runner,
            name => name == TailwindStartWatchTask.PassthroughEnvironmentVariable ? value : null,
            buildEngine);

        var result = task.Execute();

        Assert.False(result);
        Assert.Null(runner.ProbeStartInfo);
        Assert.Contains(
            TailwindStartWatchTask.PassthroughEnvironmentVariable,
            Assert.Single(buildEngine.Errors).Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("off")]
    [InlineData("on")]
    public void IsEnabled_ShouldRejectDisabledValues(string? value)
    {
        Assert.False(TailwindStartWatchTask.IsEnabled(value));
    }

    [Fact]
    public void Execute_WhenProjectHasNoDirectory_ShouldFailBeforeProbing()
    {
        var runner = new RecordingProcessRunner();
        var buildEngine = new RecordingBuildEngine();
        var task = CreateTask(runner, buildEngine: buildEngine);
        task.ProjectPath = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;

        var result = task.Execute();

        Assert.False(result);
        Assert.Null(runner.ProbeStartInfo);
        Assert.Contains("could not determine the project directory", Assert.Single(buildEngine.Errors).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("win32")]
    [InlineData("io")]
    [InlineData("unauthorized")]
    [InlineData("timeout")]
    [InlineData("argument")]
    [InlineData("invalid-operation")]
    [InlineData("not-supported")]
    [InlineData("security")]
    public void Execute_WhenProcessInteractionFails_ShouldLogTheFailure(string exceptionKind)
    {
        var runner = new RecordingProcessRunner
        {
            ProbeException = exceptionKind switch
            {
                "win32" => new System.ComponentModel.Win32Exception("probe failed"),
                "io" => new IOException("probe failed"),
                "unauthorized" => new UnauthorizedAccessException("probe failed"),
                "timeout" => new TimeoutException("probe failed"),
                "argument" => new ArgumentException("probe failed"),
                "invalid-operation" => new InvalidOperationException("probe failed"),
                "not-supported" => new NotSupportedException("probe failed"),
                "security" => new System.Security.SecurityException("probe failed"),
                _ => throw new ArgumentOutOfRangeException(nameof(exceptionKind))
            }
        };
        var buildEngine = new RecordingBuildEngine();
        var task = CreateTask(runner, buildEngine: buildEngine);

        var result = task.Execute();

        Assert.False(result);
        Assert.Contains("could not start the Tailwind watcher: probe failed", Assert.Single(buildEngine.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_WhenTheCompatibleCliCannotBeStarted_ShouldFail()
    {
        var runner = new RecordingProcessRunner
        {
            StartException = new InvalidOperationException("watch failed")
        };
        var buildEngine = new RecordingBuildEngine();
        var task = CreateTask(runner, buildEngine: buildEngine);

        var result = task.Execute();

        Assert.False(result);
        Assert.Contains("could not start the Tailwind watcher: watch failed", Assert.Single(buildEngine.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructors_ShouldValidateDependenciesAndExposeMsBuildDefaults()
    {
        var task = new TailwindStartWatchTask();

        Assert.Equal(string.Empty, task.ProjectPath);
        Assert.Equal("Debug", task.Configuration);
        Assert.Throws<ArgumentNullException>(() => new TailwindStartWatchTask(null!));

        var runner = new RecordingProcessRunner();
        var injectedTask = new TailwindStartWatchTask(runner, getEnvironmentVariable: null);
        Assert.NotNull(injectedTask);
    }

    private static TailwindStartWatchTask CreateTask(
        RecordingProcessRunner runner,
        Func<string, string?>? getEnvironmentVariable = null,
        RecordingBuildEngine? buildEngine = null)
    {
        return new TailwindStartWatchTask(runner, getEnvironmentVariable ?? (_ => null))
        {
            BuildEngine = buildEngine ?? new RecordingBuildEngine(),
            ProjectPath = Path.Combine(Path.GetTempPath(), "App.csproj")
        };
    }

    private sealed class RecordingProcessRunner : ITailwindWatchProcessRunner
    {
        public TailwindWatchProbeResult ProbeResult { get; set; } =
            new(0, $"{{\"capabilities\":[\"{TailwindCliCapabilities.ProjectWatchV1}\"]}}");

        public Exception? ProbeException { get; set; }

        public Exception? StartException { get; set; }

        public ProcessStartInfo? ProbeStartInfo { get; private set; }

        public ProcessStartInfo? WatchStartInfo { get; private set; }

        public TailwindWatchProbeResult Probe(ProcessStartInfo startInfo)
        {
            ProbeStartInfo = startInfo;
            if (ProbeException is not null)
            {
                throw ProbeException;
            }

            return ProbeResult;
        }

        public void Start(ProcessStartInfo startInfo)
        {
            WatchStartInfo = startInfo;
            if (StartException is not null)
            {
                throw StartException;
            }
        }
    }

    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];

        public List<BuildMessageEventArgs> Messages { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => string.Empty;

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }

        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            IDictionary globalProperties,
            IDictionary targetOutputs) => throw new NotSupportedException();
    }
}
