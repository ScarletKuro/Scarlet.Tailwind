namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Reserves project-aware Scarlet commands while preserving the CLI's raw Tailwind passthrough contract.
/// </summary>
internal sealed class TailwindCommandDispatcher
{
    public const string WatchCommand = "watch";

    private readonly TailwindCliApplication _tailwind;
    private readonly ITailwindWatchCommand _watch;
    private readonly bool _purePassthrough;

    public TailwindCommandDispatcher(
        TailwindCliApplication tailwind,
        ITailwindWatchCommand watch,
        bool purePassthrough)
    {
        _tailwind = tailwind;
        _watch = watch;
        _purePassthrough = purePassthrough;
    }

    public int Run(string[] args)
    {
        if (!_purePassthrough
            && args.Length > 0
            && string.Equals(args[0], WatchCommand, StringComparison.Ordinal))
        {
            return _watch.Run(args.Skip(1).ToArray());
        }

        return _tailwind.Run(args);
    }
}