using System.IO.Abstractions.TestingHelpers;
using Scarlet.Tailwind.Cli.Tests.Mock;

namespace Scarlet.Tailwind.Cli.Tests;

public class TailwindCommandDispatcherTests
{
    [Fact]
    public void Run_WithWatchFirst_ShouldRunTheProjectAwareCommand()
    {
        var rawLauncher = new RecordingProcessLauncher();
        var watch = new RecordingWatchCommand();
        var dispatcher = new TailwindCommandDispatcher(CreateRawApplication(rawLauncher), watch, purePassthrough: false);

        var result = dispatcher.Run(["watch", "--project", "App.csproj"]);

        Assert.Equal(23, result);
        Assert.NotNull(watch.Arguments);
        Assert.Equal(["--project", "App.csproj"], watch.Arguments!);
        Assert.Null(rawLauncher.Received);
    }

    [Fact]
    public void Run_WithPassthroughEnabled_ShouldForwardWatchToTailwind()
    {
        var rawLauncher = new RecordingProcessLauncher();
        var watch = new RecordingWatchCommand();
        var dispatcher = new TailwindCommandDispatcher(CreateRawApplication(rawLauncher), watch, purePassthrough: true);
        var arguments = new[] { "watch", "--project", "App.csproj" };

        dispatcher.Run(arguments);

        Assert.Equal(arguments, rawLauncher.ReceivedArguments);
        Assert.Null(watch.Arguments);
    }

    private static TailwindCliApplication CreateRawApplication(IProcessLauncher launcher)
    {
        const string toolDirectory = "/tool";
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(Path.Combine(toolDirectory, "tailwindcss"), new MockFileData("tailwind"));
        var resolver = new TailwindCliResolver(
            fileSystem,
            new RecordingChmodProvider(),
            Platform.LinuxX64,
            toolDirectory,
            (_, _) => throw new InvalidOperationException());

        return new TailwindCliApplication(
            resolver,
            launcher,
            TailwindCliOptions.FromEnvironment(
                new FakeEnvironmentProvider(new Dictionary<string, string>
                {
                    [TailwindCliOptions.CacheVariable] = "/cache"
                }),
                TailwindBuildInfo.PinnedTailwindVersion),
            new StringWriter(),
            new StringWriter());
    }

    private sealed class RecordingWatchCommand : ITailwindWatchCommand
    {
        public string[]? Arguments { get; private set; }

        public int Run(string[] args)
        {
            Arguments = args;
            return 23;
        }
    }
}
