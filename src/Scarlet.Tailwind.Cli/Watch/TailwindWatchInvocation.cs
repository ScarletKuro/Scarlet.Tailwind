namespace Scarlet.Tailwind.Cli.Watch;

internal readonly record struct TailwindWatchInvocation(
    TailwindLaunchRequest Request,
    string WorkingDirectory,
    string InputPath,
    string OutputPath,
    IReadOnlyList<string> GeneratedPaths);
