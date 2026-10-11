using System.Diagnostics;

namespace Scarlet.Tailwind.MSBuild;

internal interface ITailwindWatchProcessRunner
{
    TailwindWatchProbeResult Probe(ProcessStartInfo startInfo);

    void Start(ProcessStartInfo startInfo);
}

internal readonly struct TailwindWatchProbeResult
{
    public TailwindWatchProbeResult(int exitCode, string output)
    {
        ExitCode = exitCode;
        Output = output;
    }

    public int ExitCode { get; }

    public string Output { get; }
}
