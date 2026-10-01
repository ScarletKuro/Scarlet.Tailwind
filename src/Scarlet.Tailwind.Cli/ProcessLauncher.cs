using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

        using var signals = new ProcessSignalBridge([process]);

        process.WaitForExit();

        return process.ExitCode;
    }
}
