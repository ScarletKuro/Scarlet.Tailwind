namespace Scarlet.Tailwind.Cli;

internal interface ITailwindWatchConfigurationProvider
{
    IReadOnlyList<TailwindWatchInvocation> Resolve(string? project, string configuration, string currentDirectory);
}
