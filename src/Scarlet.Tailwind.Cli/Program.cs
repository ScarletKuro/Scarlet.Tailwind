using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Scarlet.Tailwind.Core;
using Scarlet.Tailwind.Core.Providers;

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Composition root. Everything interesting lives in <see cref="TailwindCliApplication"/>.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class Program
{
    // Returning the code from Main rather than assigning Environment.ExitCode means it is set after
    // finalizers have run, so a slow finalizer can never race the process exit.
    private static int Main(string[] args)
    {
        var fileSystem = new FileSystem();
        var environment = new SystemEnvironmentProvider();
        var chmodProvider = Chmod.CreateProvider();

        Platform platform;
        try
        {
            platform = TailwindRuntimeResolver.GetCurrentPlatform();
        }
        catch (PlatformNotSupportedException exception)
        {
            Console.Error.WriteLine($"Scarlet.Tailwind: {exception.Message}");

            return ExitCodes.TailwindNotFound;
        }

        var options = TailwindCliOptions.FromEnvironment(environment, TailwindBuildInfo.PinnedTailwindVersion);

        var resolver = new TailwindCliResolver(
            fileSystem,
            chmodProvider,
            platform,
            AppContext.BaseDirectory,
            (targetPlatform, log) => new TailwindDownloader(
                TailwindDownloader.CreateHttpClient(),
                new GitHubLatestVersionResolver(),
                fileSystem,
                chmodProvider,
                targetPlatform,
                log));

        var application = new TailwindCliApplication(
            resolver,
            new ProcessLauncher(new ConsoleTailwindLogger(Console.Error)),
            options,
            Console.Out,
            Console.Error);

        var log = new ConsoleTailwindLogger(Console.Error);
        var dispatcher = new TailwindCommandDispatcher(
            application,
            new TailwindWatchCommand(
                new MsBuildWatchConfigurationProvider(),
                new WatchProcessLauncher(log),
                new WatchSessionLockProvider(options.CacheRoot),
                Console.Out,
                Console.Error,
                Directory.GetCurrentDirectory),
            options.PurePassthrough);

        return dispatcher.Run(args);
    }
}
