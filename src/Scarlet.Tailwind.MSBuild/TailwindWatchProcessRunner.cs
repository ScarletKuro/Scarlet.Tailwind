using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Scarlet.Tailwind.MSBuild;

/// <summary>
/// Thin operating-system process adapter. Behaviour is exercised through <see cref="TailwindStartWatchTask"/>
/// with a recording runner; starting and detaching real processes belongs to the integration boundary.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class TailwindWatchProcessRunner : ITailwindWatchProcessRunner
{
    private const int ProbeTimeoutMilliseconds = 30000;

    public TailwindWatchProbeResult Probe(ProcessStartInfo startInfo)
    {
        using (var process = Process.Start(startInfo)
            ?? throw new Win32Exception("The dotnet host did not start."))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(ProbeTimeoutMilliseconds))
            {
                process.Kill();
                throw new TimeoutException(
                    $"The Tailwind watch command did not respond within {ProbeTimeoutMilliseconds / 1000} seconds.");
            }

            return new TailwindWatchProbeResult(
                process.ExitCode,
                string.Join(Environment.NewLine, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult()).Trim());
        }
    }

    public void Start(ProcessStartInfo startInfo)
    {
        using (var process = Process.Start(startInfo)
            ?? throw new Win32Exception("The Tailwind watch command did not start."))
        {
            // Disposing the Process wrapper does not terminate the independently owned process.
        }
    }
}
