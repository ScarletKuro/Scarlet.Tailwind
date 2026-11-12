using System.Diagnostics;

namespace Scarlet.Tailwind.Cli.Tests;

/// <summary>
/// Runs the real tool, because the claim is about what happens when it is killed without any of its code
/// running. That cannot be observed from inside the process under test.
/// </summary>
public sealed class WindowsJobObjectTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "scarlet-tailwind-job-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [WindowsFact]
    public void ForcefullyTerminatingTheTool_ShouldTerminateTheProcessesItStarted()
    {
        Directory.CreateDirectory(_directory);
        var marker = Path.Combine(_directory, "started.txt");
        var probe = Path.Combine(_directory, "tailwindcss.bat");

        // Stands in for Tailwind: announces itself, then outlives any reasonable test unless something ends it.
        File.WriteAllText(probe, $"""
            @echo off
            echo started> "{marker}"
            ping -n 600 127.0.0.1 > nul
            """);

        var existingPings = PingProcessIds();
        var tool = StartTool(probe);
        var started = new List<Process>();

        try
        {
            Assert.True(
                SpinWait.SpinUntil(() => File.Exists(marker), TimeSpan.FromSeconds(30)),
                "The tool did not start the probe.");
            Assert.True(
                SpinWait.SpinUntil(
                    () =>
                    {
                        started = Process.GetProcessesByName("PING")
                            .Where(process => !existingPings.Contains(process.Id))
                            .ToList();
                        return started.Count > 0;
                    },
                    TimeSpan.FromSeconds(10)),
                "The probe did not start its long-running child.");

            // TerminateProcess on the tool alone: no finalizer, ProcessExit or signal handler runs, so only
            // the job object can take its children down.
            tool.Kill(entireProcessTree: false);
            Assert.True(tool.WaitForExit(10_000), "The tool did not terminate.");

            foreach (var ping in started)
            {
                Assert.True(ping.WaitForExit(10_000), $"Process {ping.Id} outlived the tool that started it.");
            }
        }
        finally
        {
            foreach (var ping in started)
            {
                KillQuietly(ping);
                ping.Dispose();
            }

            KillQuietly(tool);
            tool.Dispose();
        }
    }

    private Process StartTool(string probe)
    {
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Scarlet.Tailwind.Cli.dll"));
        startInfo.ArgumentList.Add("--version");
        startInfo.Environment[TailwindCliOptions.PathVariable] = probe;
        startInfo.Environment[TailwindCliOptions.CacheVariable] = Path.Combine(_directory, "cache");

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the tool.");
    }

    private static HashSet<int> PingProcessIds() =>
        Process.GetProcessesByName("PING").Select(static process => process.Id).ToHashSet();

    private static void KillQuietly(Process process)
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
            // Already gone.
        }
    }
}

/// <summary>A fact that only means something on Windows, and is reported as skipped elsewhere.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows only.";
        }
    }
}
