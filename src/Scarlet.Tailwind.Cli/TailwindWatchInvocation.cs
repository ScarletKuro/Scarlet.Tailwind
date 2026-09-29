namespace Scarlet.Tailwind.Cli;

internal readonly record struct TailwindWatchInvocation(
    TailwindLaunchRequest Request,
    string InputPath,
    string OutputPath);