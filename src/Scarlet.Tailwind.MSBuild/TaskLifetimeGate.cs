using System;

namespace Scarlet.Tailwind.MSBuild;

/// <summary>
/// Runs work only while the task that owns it is still executing, and guarantees that once
/// <see cref="Close"/> has returned, nothing it guards is running or will start.
/// </summary>
/// <remarks>
/// A process's output handlers can outlive the task whenever the output drain gives up waiting for a stream
/// to close, and two things they do must not happen afterwards: logging, because MSBuild asserts that its
/// task is still active and throws rather than dropping the event; and signalling the drain's events, which
/// the task disposes on its way out.
/// <para>
/// A plain flag is not enough. A handler could read it as open, be preempted, and resume after the task had
/// returned - so the check and the work have to be atomic with respect to <see cref="Close"/>, which is what
/// the shared lock buys. The cost is one uncontended lock per line, alongside the one
/// <see cref="OutputCollector"/> already takes; the failure it prevents is not a lost message but a dead
/// build: <c>AsyncStreamReader</c> rethrows a handler exception on a thread-pool thread, where unhandled
/// terminates the process.
/// </para>
/// </remarks>
internal sealed class TaskLifetimeGate
{
    private readonly object _sync = new();
    private bool _closed;

    /// <summary>
    /// Runs <paramref name="work"/> unless the gate has closed.
    /// </summary>
    /// <returns><see langword="true"/> if it ran.</returns>
    public bool TryRun(Action work)
    {
        lock (_sync)
        {
            if (_closed)
            {
                return false;
            }

            work();
            return true;
        }
    }

    /// <summary>
    /// Closes the gate, blocking until any work already in flight has finished.
    /// </summary>
    public void Close()
    {
        lock (_sync)
        {
            _closed = true;
        }
    }
}
