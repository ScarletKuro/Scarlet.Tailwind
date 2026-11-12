using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Scarlet.Tailwind.Testing;

/// <summary>
/// Runs the <c>dotnet</c> CLI in a temporary workspace so tests can assert on what a real build produces
/// rather than on the MSBuild item lists that feed it.
/// </summary>
internal static class DotnetCli
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);

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

    /// <remarks>
    /// The timeout covers the output as well as the exit. A pipe only reaches end-of-file once every process
    /// holding its write end has gone, and <c>dotnet</c> hands its handles to the processes it starts - an
    /// MSBuild task host left behind after a failed connection is enough. Waiting for the exit alone, a test
    /// then hangs on the read indefinitely instead of failing after five minutes.
    /// </remarks>
    public static async Task<DotnetResult> Run(
        string workingDirectory,
        string arguments,
        CancellationToken cancellationToken = default,
        TimeSpan? timeoutOverride = null)
    {
        var limit = timeoutOverride ?? DefaultTimeout;

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

        // Collected incrementally rather than with ReadToEndAsync, so a timeout can still report what was written.
        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();
        var readers = Task.WhenAll(
            Collect(process.StandardOutput, standardOutput),
            Collect(process.StandardError, standardError));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await readers.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            var exited = process.HasExited;
            KillTree(process);

            // The readers may never finish - whatever holds the pipe can outlive the kill - so they are abandoned,
            // and the output captured so far is the only clue as to where the build hung.
            throw new TimeoutException(
                (exited
                    ? $"'dotnet {arguments}' exited, but its output was still open after {limit}; " +
                      "a process it started is probably still holding the pipe."
                    : $"'dotnet {arguments}' did not finish within {limit}.") +
                Environment.NewLine + Snapshot(standardOutput) + Environment.NewLine + Snapshot(standardError));
        }

        return new DotnetResult(process.ExitCode, Snapshot(standardOutput), Snapshot(standardError));
    }

    private static async Task Collect(StreamReader reader, StringBuilder target)
    {
        var buffer = new char[4096];
        int read;

        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            lock (target)
            {
                target.Append(buffer, 0, read);
            }
        }
    }

    private static string Snapshot(StringBuilder source)
    {
        lock (source)
        {
            return source.ToString();
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone, or a descendant exited mid-walk. Nothing more this method can do either way.
        }
    }
}

internal sealed record DotnetResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string Output => $"{StandardOutput}{Environment.NewLine}{StandardError}";
}
