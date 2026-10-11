using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Scarlet.Tailwind.Core;

namespace Scarlet.Tailwind.MSBuild;

/// <summary>
/// Starts the project-aware Scarlet CLI watcher without making the build own its lifetime.
/// </summary>
public sealed class TailwindStartWatchTask : Task
{
    internal const string PassthroughEnvironmentVariable = "SCARLET_TAILWIND_PASSTHROUGH";

    private readonly ITailwindWatchProcessRunner _processRunner;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public TailwindStartWatchTask()
        : this(new TailwindWatchProcessRunner(), Environment.GetEnvironmentVariable)
    {
    }

    internal TailwindStartWatchTask(
        ITailwindWatchProcessRunner processRunner,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    [Required]
    public string ProjectPath { get; set; } = string.Empty;

    public string Configuration { get; set; } = "Debug";

    public override bool Execute()
    {
        try
        {
            if (IsEnabled(_getEnvironmentVariable(PassthroughEnvironmentVariable)))
            {
                Log.LogError(
                    "Scarlet.Tailwind: TailwindWatch cannot use the project-aware CLI command while {0} is enabled.",
                    PassthroughEnvironmentVariable);
                return false;
            }

            var fullProjectPath = Path.GetFullPath(ProjectPath);
            var workingDirectory = Path.GetDirectoryName(fullProjectPath);

            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                Log.LogError("Scarlet.Tailwind: could not determine the project directory for '{0}'.", ProjectPath);
                return false;
            }

            var probe = _processRunner.Probe(CreateStartInfo(
                workingDirectory!,
                new[] { "tailwind", "--scarlet-info", "--json" },
                useShellExecute: false));

            var capabilityToken = $"\"{TailwindCliCapabilities.ProjectWatchV1}\"";
            if (probe.ExitCode != 0 || probe.Output.IndexOf(capabilityToken, StringComparison.Ordinal) < 0)
            {
                Log.LogError(
                    "Scarlet.Tailwind: TailwindWatch requires a compatible Scarlet.Tailwind.Cli dotnet tool. "
                    + "Install or restore it before starting the watch session.{0}{1}",
                    string.IsNullOrWhiteSpace(probe.Output) ? string.Empty : Environment.NewLine,
                    probe.Output);
                return false;
            }

            var arguments = new[]
            {
                "tailwind",
                "watch",
                "--project",
                fullProjectPath,
                "--configuration",
                string.IsNullOrWhiteSpace(Configuration) ? "Debug" : Configuration
            };

            _processRunner.Start(CreateStartInfo(workingDirectory!, arguments, useShellExecute: true));
            Log.LogMessage(
                MessageImportance.High,
                "Scarlet.Tailwind: started the Tailwind watcher for '{0}'. Close its window or process to stop it.",
                fullProjectPath);

            return true;
        }
        catch (Exception exception) when (exception is Win32Exception
            || exception is IOException
            || exception is UnauthorizedAccessException
            || exception is TimeoutException
            || exception is ArgumentException
            || exception is InvalidOperationException
            || exception is NotSupportedException
            || exception is System.Security.SecurityException)
        {
            Log.LogError("Scarlet.Tailwind: could not start the Tailwind watcher: {0}", exception.Message);
            return false;
        }
    }

    private ProcessStartInfo CreateStartInfo(
        string workingDirectory,
        string[] arguments,
        bool useShellExecute)
    {
        var configuredDotnetPath = _getEnvironmentVariable("DOTNET_HOST_PATH");
        var dotnetPath = string.IsNullOrWhiteSpace(configuredDotnetPath) ? "dotnet" : configuredDotnetPath!;

        return new ProcessStartInfo(dotnetPath)
        {
            Arguments = TailwindCommandLine.BuildProcessArguments(arguments),
            WorkingDirectory = workingDirectory,
            UseShellExecute = useShellExecute,
            CreateNoWindow = !useShellExecute,
            RedirectStandardOutput = !useShellExecute,
            RedirectStandardError = !useShellExecute,
            WindowStyle = ProcessWindowStyle.Normal
        };
    }

    internal static bool IsEnabled(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value!.Trim();

        return trimmed == "1"
            || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
