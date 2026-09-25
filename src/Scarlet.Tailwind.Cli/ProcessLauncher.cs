using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Scarlet.Tailwind.Core;

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Starts Tailwind as a child process and waits for it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is redirected. Tailwind inherits this process's stdin, stdout and stderr <em>handles</em>, so
/// <c>isatty</c> is true, its colours and timing line render as they normally would, <c>--output -</c> can
/// write a stylesheet to a pipe, and piping behaves exactly as it would when invoking Tailwind directly.
/// Redirecting in order to re-emit the output would break all of that.
/// </para>
/// <para>
/// The working directory is deliberately left unset so the child inherits ours verbatim. Tailwind resolves
/// <c>--cwd</c>, <c>--input</c> and <c>--output</c> against it, and scans for class names from it, so
/// round-tripping the path through .NET's normalisation could genuinely change which files are found under
/// symlinks - and therefore change the generated CSS.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed class ProcessLauncher : IProcessLauncher
{
    private readonly ITailwindLogger _log;

    public ProcessLauncher(ITailwindLogger log) => _log = log;

    /// <inheritdoc />
    public int Run(TailwindLaunchRequest request)
    {
        var startInfo = new ProcessStartInfo(request.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        // ArgumentList, never a concatenated string: the runtime applies the platform's quoting rules, which
        // is the only way arguments containing spaces, quotes or trailing backslashes survive intact.
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process();
        process.StartInfo = startInfo;

        // Shared with Scarlet.Tailwind.MSBuild.TailwindCompileTask: both exec a Tailwind binary that may have just been
        // downloaded, or just been run, by a step moments earlier, and can race the same ETXTBSY window.
        ProcessStartRetry.Start(process, _log);

        using var signals = new SignalBridge(process);

        process.WaitForExit();

        return process.ExitCode;
    }

    /// <summary>
    /// Keeps the wrapper alive until Tailwind exits, and makes sure Tailwind hears about termination.
    /// </summary>
    /// <remarks>
    /// Two failure modes this exists to prevent: the wrapper dying first, which returns the shell prompt
    /// while Tailwind is still drawing to the terminal; and Tailwind being orphaned, which .NET 10 made easier by
    /// removing the runtime's default SIGTERM and SIGHUP handling.
    /// </remarks>
    private sealed class SignalBridge : IDisposable
    {
        private readonly Process _process;
        private readonly bool _childSharesProcessGroup;
        private readonly List<PosixSignalRegistration> _registrations = new();
        private readonly EventHandler _processExitHandler;

        public SignalBridge(Process process)
        {
            _process = process;
            _childSharesProcessGroup = PosixInterop.SharesProcessGroup(process.Id);

            Register(PosixSignal.SIGINT, OnInterrupt);
            Register(PosixSignal.SIGQUIT, OnInterrupt);
            Register(PosixSignal.SIGTERM, OnTerminate);
            Register(PosixSignal.SIGHUP, OnTerminate);

            // Last resort for paths that bypass the handlers entirely.
            _processExitHandler = (_, _) => TryKill();
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

        private void Register(PosixSignal signal, Action<PosixSignalContext> handler)
        {
            try
            {
                _registrations.Add(PosixSignalRegistration.Create(signal, handler));
            }
            catch (Exception)
            {
                // Not every signal is supported on every host. Losing one handler must not stop the tool
                // from running Tailwind at all.
            }
        }

        // Ctrl+C and Ctrl+Break are delivered by the OS to the whole console / foreground process group, so
        // Tailwind already got it. Killing it here would rob it of a graceful shutdown; exiting here would hand
        // the prompt back while it is still running. So: cancel our own termination and keep waiting.
        private void OnInterrupt(PosixSignalContext context)
        {
            context.Cancel = true;

            if (!_childSharesProcessGroup)
            {
                PosixInterop.Send(_process.Id, PosixInterop.SIGINT);
            }
        }

        // SIGTERM and SIGHUP are sent to this process alone, so they have to be forwarded explicitly.
        private void OnTerminate(PosixSignalContext context)
        {
            if (OperatingSystem.IsWindows())
            {
                // Cancellation is not honoured for SIGTERM on Windows; this is the last chance to take Tailwind
                // down with us rather than orphan it.
                TryKill();

                return;
            }

            context.Cancel = true;

            PosixInterop.Send(
                _process.Id,
                context.Signal == PosixSignal.SIGHUP ? PosixInterop.SIGHUP : PosixInterop.SIGTERM);
        }

        private void TryKill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception)
            {
                // Already gone, or we lost the right to signal it. Either way there is nothing to do.
            }
        }
    }
}
