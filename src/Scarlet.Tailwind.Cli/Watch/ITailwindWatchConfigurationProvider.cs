namespace Scarlet.Tailwind.Cli.Watch;

internal interface ITailwindWatchConfigurationProvider
{
    IReadOnlyList<TailwindWatchInvocation> Resolve(string? project, string configuration, string currentDirectory);
}
