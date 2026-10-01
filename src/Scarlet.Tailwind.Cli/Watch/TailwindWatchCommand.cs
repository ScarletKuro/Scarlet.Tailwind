using System.ComponentModel;

namespace Scarlet.Tailwind.Cli.Watch;

/// <summary>
/// Runs every Tailwind entry point resolved from an MSBuild project as a foreground watcher.
/// </summary>
internal sealed class TailwindWatchCommand : ITailwindWatchCommand
{
    /// <summary>The .NET switch for hosts where file-system events are unreliable, honoured by dotnet watch.</summary>
    public const string PollingFileWatcherVariable = "DOTNET_USE_POLLING_FILE_WATCHER";

    private readonly ITailwindWatchConfigurationProvider _configurationProvider;
    private readonly IWatchProcessLauncher _launcher;
    private readonly IWatchSessionLockProvider _sessionLocks;
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly Func<string> _getCurrentDirectory;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public TailwindWatchCommand(
        ITailwindWatchConfigurationProvider configurationProvider,
        IWatchProcessLauncher launcher,
        IWatchSessionLockProvider sessionLocks,
        TextWriter stdout,
        TextWriter stderr,
        Func<string> getCurrentDirectory,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        _configurationProvider = configurationProvider;
        _launcher = launcher;
        _sessionLocks = sessionLocks;
        _stdout = stdout;
        _stderr = stderr;
        _getCurrentDirectory = getCurrentDirectory;
        _getEnvironmentVariable = getEnvironmentVariable ?? (static _ => null);
    }

    public int Run(string[] args)
    {
        if (!TryParse(args, out var options))
        {
            return ExitCodes.UsageError;
        }

        if (options.Help)
        {
            WriteHelp();
            return ExitCodes.Success;
        }

        try
        {
            var configuration = _configurationProvider.Resolve(
                options.Project,
                options.Configuration,
                _getCurrentDirectory());

            if (configuration.Count == 0)
            {
                _stderr.WriteLine("Scarlet.Tailwind: the project has no enabled TailwindBeforeStaticWebAssets entry points.");
                return ExitCodes.UsageError;
            }

            // --poll is rejected in TailwindAdditionalArguments because it is only valid alongside --watch, so
            // the watcher is the one place it can be added. An explicit option wins over the environment.
            var poll = options.Poll ?? (IsPollingRequestedByEnvironment() ? "--poll" : null);
            if (poll is not null)
            {
                configuration = configuration
                    .Select(invocation => invocation with
                    {
                        Request = invocation.Request with
                        {
                            Arguments = [.. invocation.Request.Arguments, poll]
                        }
                    })
                    .ToArray();
            }

            if (!_sessionLocks.TryAcquire(configuration, out var sessionLock))
            {
                _stderr.WriteLine("Scarlet.Tailwind: a watcher is already running for one or more configured outputs.");
                return ExitCodes.Success;
            }

            using var sessionLease = sessionLock;
            _stderr.WriteLine($"Scarlet.Tailwind: watching {configuration.Count} Tailwind entry point(s)");

            foreach (var invocation in configuration)
            {
                _stderr.WriteLine($"Scarlet.Tailwind:   {invocation.InputPath} -> {invocation.OutputPath}");
            }

            return _launcher.Run(configuration);
        }
        catch (TailwindWatchException exception)
        {
            _stderr.WriteLine($"Scarlet.Tailwind: {exception.Message}");
            return exception.ExitCode;
        }
        catch (Win32Exception exception)
        {
            _stderr.WriteLine($"Scarlet.Tailwind: failed to start a watch process: {exception.Message}");
            return ExitCodes.TailwindNotExecutable;
        }
    }

    private bool IsPollingRequestedByEnvironment()
    {
        var value = _getEnvironmentVariable(PollingFileWatcherVariable);

        return string.Equals(value, "1", StringComparison.Ordinal)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryParse(string[] args, out TailwindWatchOptions options)
    {
        string? project = null;
        var configuration = "Debug";
        string? poll = null;
        var help = false;
        options = default;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (argument is "--help" or "-h")
            {
                help = true;
                continue;
            }

            if (argument == "--poll")
            {
                poll = "--poll";
                continue;
            }

            if (argument.StartsWith("--poll=", StringComparison.Ordinal))
            {
                var interval = argument["--poll=".Length..];
                if (!int.TryParse(interval, System.Globalization.NumberStyles.None, null, out var milliseconds)
                    || milliseconds <= 0)
                {
                    _stderr.WriteLine($"Scarlet.Tailwind: --poll interval '{interval}' must be a positive number of milliseconds.");
                    return false;
                }

                poll = $"--poll={milliseconds}";
                continue;
            }

            if (TryReadOption(args, ref index, argument, "--project", "-p", out var projectValue))
            {
                if (projectValue is null)
                {
                    return false;
                }

                project = projectValue;
                continue;
            }

            if (TryReadOption(args, ref index, argument, "--configuration", "-c", out var configurationValue))
            {
                if (configurationValue is null)
                {
                    return false;
                }

                configuration = configurationValue;
                continue;
            }

            _stderr.WriteLine($"Scarlet.Tailwind: unrecognised watch option '{argument}'.");
            return false;
        }

        options = new TailwindWatchOptions(project, configuration, poll, help);
        return true;
    }

    private bool TryReadOption(
        string[] args,
        ref int index,
        string argument,
        string longName,
        string shortName,
        out string? value)
    {
        value = null;

        if (argument.StartsWith(longName + "=", StringComparison.Ordinal))
        {
            value = argument[(longName.Length + 1)..];
        }
        else if (argument == longName || argument == shortName)
        {
            if (index + 1 >= args.Length)
            {
                _stderr.WriteLine($"Scarlet.Tailwind: {argument} requires a value.");
                return true;
            }

            value = args[++index];
        }
        else
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            _stderr.WriteLine($"Scarlet.Tailwind: {longName} requires a non-empty value.");
            value = null;
        }

        return true;
    }

    private void WriteHelp()
    {
        _stdout.WriteLine("Usage: dotnet tailwind watch [options]");
        _stdout.WriteLine();
        _stdout.WriteLine("Runs Tailwind watch mode using entry points and settings from Scarlet.Tailwind.MSBuild.");
        _stdout.WriteLine();
        _stdout.WriteLine("Options:");
        _stdout.WriteLine("  -p, --project <path>        Project file or directory. Defaults to the current directory.");
        _stdout.WriteLine("  -c, --configuration <name> MSBuild configuration. Defaults to Debug.");
        _stdout.WriteLine("      --poll[=ms]            Poll instead of using file-system events. Implied when");
        _stdout.WriteLine($"                             {PollingFileWatcherVariable} is 1 or true.");
        _stdout.WriteLine("  -h, --help                 Show this help.");
    }

    private readonly record struct TailwindWatchOptions(
        string? Project,
        string Configuration,
        string? Poll,
        bool Help);
}
