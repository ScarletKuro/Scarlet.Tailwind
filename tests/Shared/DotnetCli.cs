using System.Diagnostics;
using System.Reflection;

namespace Scarlet.Tailwind.Testing;

/// <summary>
/// Runs the <c>dotnet</c> CLI in a temporary workspace so tests can assert on what a real build produces
/// rather than on the MSBuild item lists that feed it.
/// </summary>
internal static class DotnetCli
{
    /// <summary>
    /// The configuration this test assembly was built in.
    /// </summary>
    /// <remarks>
    /// Spawned builds must use the same configuration as the test run. The development targets resolve the
    /// task assembly from <c>bin\$(Configuration)\netstandard2.0</c>, so hardcoding <c>Debug</c> would make a
    /// <c>--configuration Release</c> CI run quietly produce a second, Debug-configured build of the task
    /// project underneath <c>src/</c>.
    /// </remarks>
    public static string Configuration { get; } =
        typeof(DotnetCli).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";

    public static async Task<DotnetResult> Run(
        string workingDirectory,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment =
            {
                // Node reuse keeps MSBuild worker processes (and the task assembly they loaded) alive after the build,
                // which blocks the workspace cleanup below on Windows.
                ["MSBUILDDISABLENODEREUSE"] = "1"
            }
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet.");

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);

            // Await the readers before reporting: the output captured up to the kill is the only clue as to
            // where the build hung, and abandoning the tasks would discard it.
            throw new TimeoutException(
                $"'dotnet {arguments}' did not finish within five minutes.{Environment.NewLine}" +
                await DrainAsync(standardOutput, standardError));
        }

        return new DotnetResult(process.ExitCode, await standardOutput, await standardError);
    }

    private static async Task<string> DrainAsync(Task<string> standardOutput, Task<string> standardError)
    {
        try
        {
            return $"{await standardOutput}{Environment.NewLine}{await standardError}";
        }
        catch
        {
            return "(no output was captured before the process was killed)";
        }
    }
}

internal sealed record DotnetResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Output => $"{StandardOutput}{Environment.NewLine}{StandardError}";
}
