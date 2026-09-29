using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Scarlet.Tailwind.Core;

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Starts every configured Tailwind watcher in the current foreground console and owns their lifetime.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class WatchProcessLauncher : IWatchProcessLauncher
{
    private readonly ITailwindLogger _log;

    public WatchProcessLauncher(ITailwindLogger log) => _log = log;

    public int Run(IEnumerable<TailwindWatchInvocation> invocations)
    {
        var processes = new List<Process>();

        try
        {
            foreach (var invocation in invocations)
            {
                var startInfo = CreateStartInfo(invocation);

                var process = new Process { StartInfo = startInfo };
                ProcessStartRetry.Start(process, _log);
                processes.Add(process);
            }

            using var signals = new ProcessSignalBridge(processes);
            var exits = processes.Select(static process => process.WaitForExitAsync()).ToArray();
            var firstExit = Task.WhenAny(exits).GetAwaiter().GetResult();
            var firstIndex = Array.IndexOf(exits, firstExit);

            // One watcher ending means the session is no longer complete. Take down the rest so the command
            // never leaves a partially functioning or orphaned watch session behind.
            signals.KillRunningProcesses();
            Task.WhenAll(exits).GetAwaiter().GetResult();

            return processes[firstIndex].ExitCode;
        }
        finally
        {
            foreach (var process in processes)
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
                    // Already gone or inaccessible; disposal is still safe.
                }

                process.Dispose();
            }
        }
    }

    internal static ProcessStartInfo CreateStartInfo(TailwindWatchInvocation invocation)
    {
        var request = invocation.Request;
        var startInfo = new ProcessStartInfo(request.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = invocation.WorkingDirectory
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
