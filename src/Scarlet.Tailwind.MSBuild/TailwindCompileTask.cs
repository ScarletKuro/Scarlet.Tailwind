using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Scarlet.Tailwind.Core;
using Scarlet.Tailwind.Core.Extensions;
using Scarlet.Tailwind.Core.Providers;

namespace Scarlet.Tailwind.MSBuild;

/// <summary>
/// Compiles Tailwind entry points before static web asset discovery.
/// </summary>
public sealed class TailwindCompileTask : Task
{
    private const int DiagnosticTailLineCount = 50;
    private const int OutputDrainGraceMilliseconds = 5000;

    [Required]
    public ITaskItem[] Compilations { get; set; } = Array.Empty<ITaskItem>();

    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    public string Configuration { get; set; } = "Debug";
    public string Minify { get; set; } = "Auto";
    public string Optimize { get; set; } = "false";
    public string Map { get; set; } = "Auto";
    public string Silent { get; set; } = "false";
    public string Cwd { get; set; } = string.Empty;
    public string AdditionalArguments { get; set; } = string.Empty;
    public string ManifestDirectory { get; set; } = string.Empty;
    public string? RuntimeDirectory { get; set; }
    public bool TailwindRuntimeDownload { get; set; }
    public string? TailwindVersionDownload { get; set; }
    public int DownloadMutexTimeoutSeconds { get; set; } = 300;
    public ITaskItem[]? RuntimePacks { get; set; }

    /// <summary>
    /// Maximum time, in milliseconds, to wait for each `tailwindcss` invocation before killing it. Zero (the
    /// default) waits indefinitely.
    /// </summary>
    public int TimeoutMilliseconds { get; set; }

    [Output]
    public ITaskItem[] GeneratedFiles { get; private set; } = Array.Empty<ITaskItem>();

    [Output]
    public ITaskItem[] RemovedFiles { get; private set; } = Array.Empty<ITaskItem>();

    public override bool Execute()
    {
        // Closed on every exit path. A handler firing after the task has returned - possible whenever the
        // drain grace expires - would otherwise log into a finished task, which MSBuild turns into an
        // exception that AsyncStreamReader rethrows on a thread-pool thread, killing the build process.
        var gate = new TaskLifetimeGate();

        try
        {
            var fileSystem = new FileSystem();

            // Validated up front so a bad property value is reported once, rather than once per entry point.
            ValidateAdditionalArguments(AdditionalArguments, "TailwindAdditionalArguments");
            ResolveSettings(Minify, Optimize, Map, Silent, Cwd, AdditionalArguments);
            if (Log.HasLoggedErrors)
            {
                return false;
            }

            var entries = DiscoverEntries(fileSystem);
            if (Log.HasLoggedErrors)
            {
                return false;
            }

            if (entries.Count == 0)
            {
                Log.LogMessage(MessageImportance.Low, "No Tailwind entry points were found.");
                return true;
            }

            var tailwindPath = ResolveTailwind(fileSystem);
            var manifestDirectory = ResolveManifestDirectory();
            var manifestPath = Path.Combine(manifestDirectory, "Tailwind.generated.txt");
            var expectedFiles = entries.SelectMany(static entry => entry.ExpectedOutputs).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var previousFiles = ReadManifest(fileSystem, manifestPath);
            var removedFiles = previousFiles.Except(expectedFiles, StringComparer.OrdinalIgnoreCase).ToArray();

            foreach (var stale in removedFiles)
            {
                fileSystem.File.TryDeleteFile(stale);
            }

            Log.LogMessage(MessageImportance.High, $"Using Tailwind at: {tailwindPath}");

            // Tailwind's argument parser collapses repeated --input arguments to the last value, so one
            // invocation can compile only one stylesheet. Run one process per entry point rather than
            // silently dropping all but the final input.
            foreach (var entry in entries)
            {
                var processArguments = TailwindCommandLine.BuildProcessArguments(BuildArguments(entry));
                Log.LogMessage(MessageImportance.High, $"Executing: {TailwindCommandLine.FormatProcessCommand(tailwindPath, processArguments)}");

                var result = RunProcess(tailwindPath, processArguments, gate);
                if (result is null)
                {
                    return false;
                }

                if (result.ExitCode != 0)
                {
                    Log.LogError($"Tailwind command failed with exit code {result.ExitCode}");
                    LogOutputDetail(result.OutputTail, result.ErrorTail);

                    return false;
                }
            }

            fileSystem.Directory.CreateDirectory(manifestDirectory);
            fileSystem.File.WriteAllLines(manifestPath, expectedFiles);

            GeneratedFiles = expectedFiles.Select(CreateGeneratedFileItem).ToArray();
            RemovedFiles = removedFiles.Select(static path => (ITaskItem)new TaskItem(path)).ToArray();

            return !Log.HasLoggedErrors;
        }
        catch (FileNotFoundException ex)
        {
            Log.LogError(ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, true);
            return false;
        }
        finally
        {
            gate.Close();
        }
    }

    private TailwindSettings ResolveSettings(
        string minifyValue,
        string optimizeValue,
        string mapValue,
        string silentValue,
        string cwdValue,
        string additionalArguments)
    {
        var debug = string.Equals(Configuration, "Debug", StringComparison.OrdinalIgnoreCase);

        var minify = ParseAutoBoolean(minifyValue, !debug, "TailwindMinify");
        var optimize = ParseBoolean(optimizeValue, "TailwindOptimize");
        var silent = ParseBoolean(silentValue, "TailwindSilent");
        var map = ResolveMap(mapValue, debug);

        return new TailwindSettings(minify, optimize, map, silent, EmptyToNull(cwdValue), additionalArguments);
    }

    /// <summary>
    /// Resolves <c>TailwindMap</c>, which is four-valued rather than boolean.
    /// </summary>
    /// <remarks>
    /// <c>Auto</c> resolves to inline in Debug and off in Release. Inline rather than a path is deliberate:
    /// it keeps the build to one output file, so the manifest has one entry, static web assets have one
    /// asset to fingerprint, and Clean has one file to remove. Anything that is not Auto, true or false is
    /// taken as the path to write an external map to, which is the only form that produces a second file.
    /// </remarks>
    private static TailwindMapMode ResolveMap(string value, bool debug)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return debug ? TailwindMapMode.Inlined() : TailwindMapMode.None();
        }

        if (bool.TryParse(value, out var boolean))
        {
            return boolean ? TailwindMapMode.Inlined() : TailwindMapMode.None();
        }

        return TailwindMapMode.External(value.Trim());
    }

    private IReadOnlyList<TailwindEntry> DiscoverEntries(IFileSystem fileSystem)
    {
        var entries = new List<TailwindEntry>();
        var projectDirectory = Path.GetFullPath(ProjectDirectory);

        foreach (var item in Compilations)
        {
            var itemAdditionalArguments = item.GetMetadata("AdditionalArguments");
            ValidateAdditionalArguments(
                itemAdditionalArguments,
                $"AdditionalArguments metadata on TailwindBeforeStaticWebAssets item '{item.ItemSpec}'");

            var input = ResolvePath(projectDirectory, item.ItemSpec);
            var outputMetadata = item.GetMetadata("OutputPath");
            if (string.IsNullOrWhiteSpace(outputMetadata))
            {
                Log.LogError("TailwindBeforeStaticWebAssets item '{0}' must specify OutputPath metadata.", item.ItemSpec);
                continue;
            }

            if (!fileSystem.File.Exists(input))
            {
                Log.LogError("Tailwind input '{0}' was not found.", item.ItemSpec);
                continue;
            }

            var output = ResolvePath(projectDirectory, outputMetadata);
            var itemSettings = ResolveSettings(
                Override(item.GetMetadata("Minify"), Minify),
                Override(item.GetMetadata("Optimize"), Optimize),
                Override(item.GetMetadata("Map"), Map),
                Override(item.GetMetadata("Silent"), Silent),
                Override(item.GetMetadata("Cwd"), Cwd),
                string.Join(" ", new[] { AdditionalArguments, itemAdditionalArguments }.Where(static value => !string.IsNullOrWhiteSpace(value))));

            // The working directory Tailwind scans from. Defaulting to the project directory is what makes
            // the generated CSS depend on the project rather than on where `dotnet build` was invoked from.
            var workingDirectory = itemSettings.Cwd is null
                ? projectDirectory
                : ResolvePath(projectDirectory, itemSettings.Cwd);

            // Resolved against the working directory, because that is what Tailwind itself would do with a
            // relative --map path. Making it absolute here means the task and Tailwind cannot disagree about
            // where the file lands, which is what the manifest and Clean depend on.
            var mapPath = itemSettings.Map.Path is null
                ? null
                : ResolvePath(workingDirectory, itemSettings.Map.Path!);

            var expected = mapPath is null
                ? new[] { output }
                : new[] { output, mapPath };

            entries.Add(new TailwindEntry(input, output, mapPath, workingDirectory, expected, itemSettings));
        }

        return entries;
    }

    private string ResolveTailwind(IFileSystem fileSystem)
    {
        var chmodProvider = Chmod.CreateProvider();

        if (TailwindRuntimeDownload)
        {
            if (string.IsNullOrWhiteSpace(RuntimeDirectory))
            {
                throw new FileNotFoundException("TailwindRuntimeDirectory is required when TailwindRuntimeDownload is true.");
            }

            using var httpClient = TailwindDownloader.CreateHttpClient();
            var downloader = new TailwindDownloader(
                httpClient,
                new GitHubLatestVersionResolver(),
                fileSystem,
                chmodProvider,
                TailwindRuntimeResolver.GetCurrentPlatform(),
                new MsBuildTailwindLogger(Log));

            return downloader.DownloadRuntime(RuntimeDirectory!, TailwindVersionDownload, DownloadMutexTimeoutSeconds);
        }

        return TailwindRuntimeResolver.ResolveTailwindExecutable(
            fileSystem,
            chmodProvider,
            TailwindRuntimeResolver.GetCurrentPlatform(),
            RuntimeDirectory,
            TailwindRuntimePackFactory.FromTaskItems(RuntimePacks, warning => Log.LogWarning(warning)),
            message => Log.LogMessage(MessageImportance.Normal, message));
    }

    /// <summary>
    /// Renders the argument list for one entry point.
    /// </summary>
    /// <remarks>
    /// Order matters in two places. A bare <c>--map</c> takes an optional value, and Tailwind's parser
    /// consumes the next token as that value unless it looks like a flag - so <c>--map</c> is emitted before
    /// <c>--input</c>, which is always present and always starts with a dash. <see cref="AdditionalArguments"/>
    /// goes last so future Tailwind options retain the order the user wrote. Arguments that would override
    /// task-owned paths or maps are rejected because the manifest and Clean must describe the actual files.
    /// </remarks>
    private IReadOnlyList<string> BuildArguments(TailwindEntry entry)
    {
        var settings = entry.Settings;
        var args = new List<string>();

        if (settings.Minify)
        {
            // --minify is a superset of --optimize: the CLI describes it as "Optimize and minify the output",
            // and both enter the same branch with --minify additionally setting minify: true. Passing both
            // would say the same thing twice.
            args.Add("--minify");
        }
        else if (settings.Optimize)
        {
            args.Add("--optimize");
        }

        if (settings.Silent)
        {
            args.Add("--silent");
        }

        if (entry.MapPath is not null)
        {
            args.Add($"--map={entry.MapPath}");
        }
        else if (settings.Map.Inline)
        {
            args.Add("--map");
        }

        args.Add($"--cwd={entry.WorkingDirectory}");
        args.Add($"--input={entry.InputPath}");
        args.Add($"--output={entry.OutputPath}");

        if (!string.IsNullOrWhiteSpace(settings.AdditionalArguments))
        {
            // Split rather than appended whole: every other token here is one argument, and passing a
            // multi-word string through as a single argument would hand Tailwind one nonsense flag.
            args.AddRange(SplitArguments(settings.AdditionalArguments));
        }

        return args;
    }

    private ProcessResult? RunProcess(string fileName, string processArguments, TaskLifetimeGate gate)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = processArguments,
            WorkingDirectory = ProjectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process();
        process.StartInfo = startInfo;
        var output = new OutputCollector(DiagnosticTailLineCount, captureAll: false);
        var error = new OutputCollector(DiagnosticTailLineCount, captureAll: false);

        // Declared out here, not beside the handlers that use them: a `using` inside the try disposes as
        // control leaves the try, which is before the finally closes the gate - leaving a window where a late
        // handler could pass the gate and signal a disposed event. At method scope they outlive the gate.
        using var outputClosed = new ManualResetEventSlim(false);
        using var errorClosed = new ManualResetEventSlim(false);

        // Neither handler may touch `process`. It is disposed as control leaves this method, while these can
        // still fire, so reaching for something like process.Id here would hit a disposed object on a
        // thread-pool thread - which terminates the build. Nothing tests this; it only holds by inspection.
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                // ReSharper disable once AccessToDisposedClosure
                gate.TryRun(outputClosed.Set);
                return;
            }

            output.Add(e.Data);
            gate.TryRun(() => Log.LogMessage(MessageImportance.Normal, e.Data));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                // ReSharper disable once AccessToDisposedClosure
                gate.TryRun(errorClosed.Set);
                return;
            }

            error.Add(e.Data);
            gate.TryRun(() => Log.LogMessage(MessageImportance.High, e.Data));
        };

        ProcessStartRetry.Start(process, new MsBuildTailwindLogger(Log));
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (TimeoutMilliseconds > 0)
        {
            if (!process.WaitForExit(TimeoutMilliseconds))
            {
                KillTimedOutProcess(process);

                // Drain on the way out, bounded exactly like the normal path below. Without this the lines
                // Tailwind had already written are discarded, and a timeout is the one failure where "what
                // was it doing?" is the only question worth asking.
                process.WaitForExit(OutputDrainGraceMilliseconds);
                outputClosed.Wait(OutputDrainGraceMilliseconds);
                errorClosed.Wait(OutputDrainGraceMilliseconds);

                Log.LogError($"Command timed out after {TimeoutMilliseconds}ms");

                // The streamed lines go out as messages, which quiet verbosity drops, so without this the
                // entire report at -v:q is a single line and no indication of what Tailwind had managed to do.
                LogOutputDetail(output.Tail, error.Tail);

                return null;
            }

            // The process is gone, but the handlers may not have drained. Waiting on the end-of-stream
            // signals rather than the parameterless WaitForExit() keeps that wait bounded - a detached
            // grandchild holding the write end can withhold EOF forever, outliving the timeout the caller
            // asked for - and costs no extra thread to abandon when it does.
            if (!(outputClosed.Wait(OutputDrainGraceMilliseconds) && errorClosed.Wait(OutputDrainGraceMilliseconds)))
            {
                Log.LogMessage(
                    MessageImportance.Normal,
                    $"Tailwind exited but its output was still open after {OutputDrainGraceMilliseconds}ms; some output may be missing.");
            }
        }
        else
        {
            process.WaitForExit();
        }

        return new ProcessResult(process.ExitCode, output.Tail, error.Tail);
    }

    /// <summary>
    /// Adds whatever Tailwind said to a failure that has already been reported.
    /// </summary>
    /// <remarks>
    /// Shared by the timeout and non-zero-exit paths so the two cannot drift into reporting differently.
    /// The tails are logged unlabelled on purpose: for a compiler, stderr already reads as a diagnostic, and
    /// an "Error output:" prefix would only push that text further from the start of the line without adding
    /// anything.
    /// </remarks>
    private void LogOutputDetail(string outputTail, string errorTail)
    {
        if (!string.IsNullOrWhiteSpace(errorTail))
        {
            Log.LogError(errorTail);
        }

        if (!string.IsNullOrWhiteSpace(outputTail))
        {
            Log.LogError(outputTail);
        }
    }

    [ExcludeFromCodeCoverage]
    private static void KillTimedOutProcess(Process process)
    {
        try
        {
            process.Kill();
        }
        catch
        {
            // Best-effort only: the process can exit between WaitForExit(timeout) and Kill().
        }
    }

    private string ResolveManifestDirectory()
    {
        if (!string.IsNullOrWhiteSpace(ManifestDirectory))
        {
            return ResolvePath(ProjectDirectory, ManifestDirectory);
        }

        return Path.Combine(ProjectDirectory, "obj", "Scarlet.Tailwind");
    }

    private static IReadOnlyList<string> ReadManifest(IFileSystem fileSystem, string manifestPath)
    {
        return fileSystem.File.Exists(manifestPath)
            ? fileSystem.File.ReadAllLines(manifestPath)
            : Array.Empty<string>();
    }

    private bool ParseAutoBoolean(string value, bool automatic, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return automatic;
        }

        return ParseBoolean(value, name);
    }

    private bool ParseBoolean(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (bool.TryParse(value, out var result))
        {
            return result;
        }

        Log.LogError("{0} must be true or false; received '{1}'.", name, value);
        return false;
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        var normalizedPath = NormalizePathSeparators(path);

        return Path.IsPathRooted(normalizedPath)
            ? Path.GetFullPath(normalizedPath)
            : Path.GetFullPath(Path.Combine(baseDirectory, normalizedPath));
    }

    private static string NormalizePathSeparators(string path)
    {
        return Path.DirectorySeparatorChar == '\\'
            ? path.Replace('/', Path.DirectorySeparatorChar)
            : path.Replace('\\', Path.DirectorySeparatorChar);
    }

    private ITaskItem CreateGeneratedFileItem(string path)
    {
        var item = new TaskItem(path);
        item.SetMetadata("RelativePath", GetRelativePath(ProjectDirectory, path));
        return item;
    }

    private static string GetRelativePath(string baseDirectory, string path)
    {
        var baseUri = new Uri(EnsureTrailingDirectorySeparator(Path.GetFullPath(baseDirectory)));
        var pathUri = new Uri(Path.GetFullPath(path));

        if (!string.Equals(baseUri.Scheme, pathUri.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        var relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
        return relative.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
               || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Splits a raw argument string on whitespace, honouring double quotes.
    /// </summary>
    /// <remarks>
    /// Only needed for <see cref="AdditionalArguments"/>, where the value arrives as one MSBuild string but
    /// means several arguments. Quotes let a path with spaces survive, which is the only case that would
    /// otherwise be unrepresentable.
    /// </remarks>
    internal static IReadOnlyList<string> SplitArguments(string value)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var started = false;

        foreach (var character in value)
        {
            if (character == '"')
            {
                quoted = !quoted;
                started = true;
                continue;
            }

            if (!quoted && char.IsWhiteSpace(character))
            {
                if (started)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    started = false;
                }

                continue;
            }

            current.Append(character);
            started = true;
        }

        if (started)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    private void ValidateAdditionalArguments(string value, string source)
    {
        foreach (var argument in SplitArguments(value))
        {
            if (IsOption(argument, "-w") || IsOption(argument, "--watch") || IsOption(argument, "--poll"))
            {
                Log.LogError(
                    "{0} must not contain '{1}'. Tailwind watch mode does not exit during a build; use a Watch item or run 'dotnet tailwind --watch' separately.",
                    source,
                    argument);
            }
            else if (IsOption(argument, "-i") || IsOption(argument, "--input")
                     || IsOption(argument, "-o") || IsOption(argument, "--output")
                     || IsOption(argument, "--cwd") || IsOption(argument, "--map"))
            {
                Log.LogError(
                    "{0} must not contain task-owned argument '{1}'. Configure input, OutputPath, Cwd or Map through TailwindBeforeStaticWebAssets metadata so generated files and Clean remain accurate.",
                    source,
                    argument);
            }
        }
    }

    private static bool IsOption(string argument, string option) =>
        string.Equals(argument, option, StringComparison.Ordinal)
        || argument.StartsWith(option + "=", StringComparison.Ordinal);

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    private static string Override(string metadata, string fallback) => string.IsNullOrWhiteSpace(metadata) ? fallback : metadata;

    /// <summary>
    /// What <c>TailwindMap</c> resolved to: off, inline, or an external file at a given path.
    /// </summary>
    private sealed class TailwindMapMode
    {
        private TailwindMapMode(bool inline, string? path)
        {
            Inline = inline;
            Path = path;
        }

        public bool Inline { get; }

        /// <summary>Where to write an external map, or <see langword="null"/> for inline and off.</summary>
        public string? Path { get; }

        public static TailwindMapMode None() => new(false, null);

        public static TailwindMapMode Inlined() => new(true, null);

        public static TailwindMapMode External(string path) => new(false, path);

        public override string ToString() => Path ?? (Inline ? "inline" : "none");
    }

    private sealed class TailwindSettings
    {
        public TailwindSettings(
            bool minify,
            bool optimize,
            TailwindMapMode map,
            bool silent,
            string? cwd,
            string additionalArguments)
        {
            Minify = minify;
            Optimize = optimize;
            Map = map;
            Silent = silent;
            Cwd = cwd;
            AdditionalArguments = additionalArguments;
        }

        public bool Minify { get; }
        public bool Optimize { get; }
        public TailwindMapMode Map { get; }
        public bool Silent { get; }
        public string? Cwd { get; }
        public string AdditionalArguments { get; }

        public override string ToString()
        {
            return string.Join(
                "|",
                Minify.ToString(),
                Optimize.ToString(),
                Map.ToString(),
                Silent.ToString(),
                Cwd ?? string.Empty,
                AdditionalArguments);
        }
    }

    private sealed class TailwindEntry
    {
        public TailwindEntry(
            string inputPath,
            string outputPath,
            string? mapPath,
            string workingDirectory,
            IReadOnlyList<string> expectedOutputs,
            TailwindSettings settings)
        {
            InputPath = inputPath;
            OutputPath = outputPath;
            MapPath = mapPath;
            WorkingDirectory = workingDirectory;
            ExpectedOutputs = expectedOutputs;
            Settings = settings;
        }

        public string InputPath { get; }
        public string OutputPath { get; }

        /// <summary>Absolute path of the external source map, or <see langword="null"/> when there is none.</summary>
        public string? MapPath { get; }

        public string WorkingDirectory { get; }
        public IReadOnlyList<string> ExpectedOutputs { get; }
        public TailwindSettings Settings { get; }
    }

    private sealed class ProcessResult
    {
        public ProcessResult(int exitCode, string outputTail, string errorTail)
        {
            ExitCode = exitCode;
            OutputTail = outputTail;
            ErrorTail = errorTail;
        }

        public int ExitCode { get; }
        public string OutputTail { get; }
        public string ErrorTail { get; }
    }
}
