using System.ComponentModel;

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Runs every Tailwind entry point resolved from an MSBuild project as a foreground watcher.
/// </summary>
internal sealed class TailwindWatchCommand : ITailwindWatchCommand
{
    private readonly ITailwindWatchConfigurationProvider _configurationProvider;
    private readonly IWatchProcessLauncher _launcher;
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly Func<string> _getCurrentDirectory;

    public TailwindWatchCommand(
        ITailwindWatchConfigurationProvider configurationProvider,
        IWatchProcessLauncher launcher,
        TextWriter stdout,
        TextWriter stderr,
        Func<string> getCurrentDirectory)
    {
        _configurationProvider = configurationProvider;
        _launcher = launcher;
        _stdout = stdout;
        _stderr = stderr;
        _getCurrentDirectory = getCurrentDirectory;
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

            _stderr.WriteLine(
                $"Scarlet.Tailwind: watching {configuration.Count} Tailwind entry point(s)");

            foreach (var invocation in configuration)
            {
                _stderr.WriteLine($"Scarlet.Tailwind:   {invocation.InputPath} -> {invocation.OutputPath}");
            }

            return _launcher.Run(configuration.Select(static item => item.Request));
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

    private bool TryParse(string[] args, out TailwindWatchOptions options)
    {
        string? project = null;
        var configuration = "Debug";
        var help = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (argument is "--help" or "-h")
            {
                help = true;
                continue;
            }

            if (TryReadOption(args, ref index, argument, "--project", "-p", out var projectValue))
            {
                if (projectValue is null)
                {
                    options = default;
                    return false;
                }

                project = projectValue;
                continue;
            }

            if (TryReadOption(args, ref index, argument, "--configuration", "-c", out var configurationValue))
            {
                if (configurationValue is null)
                {
                    options = default;
                    return false;
                }

                configuration = configurationValue;
                continue;
            }

            _stderr.WriteLine($"Scarlet.Tailwind: unrecognised watch option '{argument}'.");
            options = default;
            return false;
        }

        options = new TailwindWatchOptions(project, configuration, help);
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
        _stdout.WriteLine("  -h, --help                 Show this help.");
    }

    private readonly record struct TailwindWatchOptions(string? Project, string Configuration, bool Help);
}