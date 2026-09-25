namespace Scarlet.Tailwind.Cli.Tests.Mock;

/// <summary>
/// Captures what would have been handed to Tailwind, so argument fidelity can be asserted without a process.
/// </summary>
internal sealed class RecordingProcessLauncher : IProcessLauncher
{
    private readonly int _exitCode;

    public RecordingProcessLauncher(int exitCode = 0) => _exitCode = exitCode;

    /// <summary>The request the launcher received, or <see langword="null"/> when it was never invoked.</summary>
    public TailwindLaunchRequest? Received { get; private set; }

    /// <summary>The arguments the launcher received, or an empty list when it was never invoked.</summary>
    public IReadOnlyList<string> ReceivedArguments => Received?.Arguments ?? Array.Empty<string>();

    public int Run(TailwindLaunchRequest request)
    {
        Received = request;

        return _exitCode;
    }
}
