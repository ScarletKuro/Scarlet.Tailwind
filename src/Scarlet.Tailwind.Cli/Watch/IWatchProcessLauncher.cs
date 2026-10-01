namespace Scarlet.Tailwind.Cli.Watch;

internal interface IWatchProcessLauncher
{
    int Run(IEnumerable<TailwindWatchInvocation> invocations);
}
