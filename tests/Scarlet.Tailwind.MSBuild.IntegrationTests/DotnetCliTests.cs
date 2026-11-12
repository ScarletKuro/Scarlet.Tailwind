using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Scarlet.Tailwind.MSBuild.IntegrationTests;

/// <summary>
/// Covers the test harness itself, where a regression does not fail a test - it hangs the run.
/// </summary>
public class DotnetCliTests
{
    private readonly ITestOutputHelper _output;

    public DotnetCliTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// A process that outlives <c>dotnet</c> and inherited its output keeps the pipe open, so reading to the end
    /// never finishes. This shipped once: orphaned MSBuild task hosts held the pipe and a test sat for 15 minutes
    /// with the five-minute timeout never firing, because it only covered the exit. Here a file-based app starts a
    /// copy of itself that lingers - portable, unlike a shell's background job - and exits straight away.
    /// </summary>
    [Fact]
    public async Task Run_WhenADescendantKeepsTheOutputOpen_TimesOutWithTheCapturedOutput()
    {
        using var workspace = TempWorkspace.Create("dotnet-cli-pipe");
        workspace.WriteFile("linger.cs", """
            using System.Diagnostics;

            if (args is ["linger"])
            {
                Thread.Sleep(TimeSpan.FromMinutes(2));
                return;
            }

            // A file-based app normally runs through its apphost, but fall back to the muxer if it does not.
            var self = Environment.ProcessPath!;
            var start = Path.GetFileNameWithoutExtension(self) == "dotnet"
                ? new ProcessStartInfo(self, $"\"{typeof(Program).Assembly.Location}\" linger")
                : new ProcessStartInfo(self, "linger");
            start.UseShellExecute = false;

            using var child = Process.Start(start)!;
            Console.WriteLine($"lingering-pid={child.Id}");
            """);

        // Compile outside the measured run, so the short timeout below only has the pipe to wait for.
        var build = await DotnetCli.Run(workspace.RootDirectory, "build linger.cs");
        _output.WriteLine(build.Output);
        Assert.Equal(0, build.ExitCode);

        var stopwatch = Stopwatch.StartNew();
        var exception = await Assert.ThrowsAsync<TimeoutException>(() => DotnetCli.Run(
            workspace.RootDirectory, "run linger.cs", timeoutOverride: TimeSpan.FromSeconds(20)));
        stopwatch.Stop();
        _output.WriteLine(exception.Message);

        try
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"Run took {stopwatch.Elapsed} to time out.");
            Assert.Contains("still holding the pipe", exception.Message, StringComparison.Ordinal);

            // Output written before the hang is the only clue to where it happened, so it must survive.
            Assert.Contains("lingering-pid=", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            KillLingeringProcess(exception.Message);
        }
    }

    private static void KillLingeringProcess(string message)
    {
        var match = Regex.Match(message, @"lingering-pid=(\d+)");
        if (!match.Success)
        {
            return;
        }

        try
        {
            using var lingering = Process.GetProcessById(int.Parse(match.Groups[1].Value));
            lingering.Kill();
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }
}
