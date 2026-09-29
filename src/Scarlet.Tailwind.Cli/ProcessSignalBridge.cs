using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Keeps wrapper and child process lifetimes together across console and operating-system termination.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class ProcessSignalBridge : IDisposable
{
    private readonly IReadOnlyList<(Process Process, bool SharesProcessGroup)> _processes;
    private readonly List<PosixSignalRegistration> _registrations = new();
    private readonly EventHandler _processExitHandler;

    public ProcessSignalBridge(IReadOnlyList<Process> processes)
    {
        _processes = processes
            .Select(process => (process, PosixInterop.SharesProcessGroup(process.Id)))
            .ToArray();

        Register(PosixSignal.SIGINT, OnInterrupt);
        Register(PosixSignal.SIGQUIT, OnInterrupt);
        Register(PosixSignal.SIGTERM, OnTerminate);
        Register(PosixSignal.SIGHUP, OnTerminate);

        _processExitHandler = (_, _) => KillRunningProcesses();
        AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
    }

    public void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;

        foreach (var registration in _registrations)
        {
            registration.Dispose();
        }

        _registrations.Clear();
    }

    public void KillRunningProcesses()
    {
        foreach (var (process, _) in _processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Already gone, or we lost the right to signal it. Either way there is nothing to do.
            }
        }
    }

    private void Register(PosixSignal signal, Action<PosixSignalContext> handler)
    {
        try
        {
            _registrations.Add(PosixSignalRegistration.Create(signal, handler));
        }
        catch (Exception)
        {
            // Not every signal is supported on every host.
        }
    }

    private void OnInterrupt(PosixSignalContext context)
    {
        context.Cancel = true;

        foreach (var (process, sharesProcessGroup) in _processes)
        {
            if (!sharesProcessGroup)
            {
                PosixInterop.Send(process.Id, PosixInterop.SIGINT);
            }
        }
    }

    private void OnTerminate(PosixSignalContext context)
    {
        if (OperatingSystem.IsWindows())
        {
            KillRunningProcesses();
            return;
        }

        context.Cancel = true;
        var signal = context.Signal == PosixSignal.SIGHUP ? PosixInterop.SIGHUP : PosixInterop.SIGTERM;

        foreach (var (process, _) in _processes)
        {
            PosixInterop.Send(process.Id, signal);
        }
    }
}
