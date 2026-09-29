namespace Scarlet.Tailwind.Cli;

internal interface IWatchProcessLauncher
{
    int Run(IEnumerable<TailwindWatchInvocation> invocations);
}
