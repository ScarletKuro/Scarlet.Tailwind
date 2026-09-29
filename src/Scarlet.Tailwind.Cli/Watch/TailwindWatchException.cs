namespace Scarlet.Tailwind.Cli;

internal sealed class TailwindWatchException : Exception
{
    public TailwindWatchException(string message, int exitCode = ExitCodes.UsageError)
        : base(message)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}
