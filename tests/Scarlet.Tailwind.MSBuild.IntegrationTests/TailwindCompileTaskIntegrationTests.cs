using Microsoft.Build.Utilities;
using Xunit.Abstractions;

namespace Scarlet.Tailwind.MSBuild.IntegrationTests;

/// <summary>
/// Runs the real Tailwind binary through <see cref="TailwindCompileTask"/> in process.
/// </summary>
/// <remarks>
/// The runtime comes from the MSBuild project's staged <c>bin/runtimes</c>, the same place the development
/// targets point at, so these exercise the embedded-pack path rather than a download.
/// </remarks>
public class TailwindCompileTaskIntegrationTests
{
    private static readonly string RuntimeDirectory =
        Path.Combine(RepositoryRoot.Path, "src", "Scarlet.Tailwind.MSBuild", "bin", "runtimes");

    /// <summary>
    /// The entry stylesheet the README recommends, so the documented advice is also the tested path.
    /// </summary>
    private const string EntryStylesheet =
        """
        @import "tailwindcss";

        @source not "./bin";
        @source not "./obj";
        """;

    private readonly ITestOutputHelper _output;

    public TailwindCompileTaskIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CompileTask_EmitsRulesForClassesItFindsAndNothingElse()
    {
        using var workspace = CreateWorkspace("compile-basic");

        var task = CreateTask(workspace);

        Assert.True(task.Execute());

        var css = File.ReadAllText(workspace.PathTo("wwwroot", "css", "app.css"));

        // Present in Index.cshtml, so the scanner should have found it.
        Assert.Contains(".text-3xl", css);
        Assert.Contains(".bg-indigo-600", css);

        // Never written anywhere in the workspace. Tailwind v4 generates on demand, so its absence is what
        // proves the output is driven by the scan rather than being a fixed stylesheet.
        Assert.DoesNotContain(".bg-lime-300", css);
    }

    [Fact]
    public void CompileTask_ScansRelativeToTheProjectDirectory_NotTheProcessWorkingDirectory()
    {
        // The whole reason --cwd is always passed. Without it the scan root would be wherever the build
        // happened to be invoked from, and identical sources would produce different CSS.
        using var workspace = CreateWorkspace("compile-cwd");

        var task = CreateTask(workspace);

        Assert.True(task.Execute());

        Assert.Contains(".text-3xl", File.ReadAllText(workspace.PathTo("wwwroot", "css", "app.css")));
    }

    [Fact]
    public void CompileTask_InDebug_InlinesTheSourceMapIntoASingleFile()
    {
        using var workspace = CreateWorkspace("compile-debug-map");

        var task = CreateTask(workspace);
        task.Configuration = "Debug";

        Assert.True(task.Execute());

        var cssPath = workspace.PathTo("wwwroot", "css", "app.css");
        Assert.Contains("sourceMappingURL=data:", File.ReadAllText(cssPath));

        // Inline means one file, which is the point: one manifest entry, one static web asset, one file to
        // clean.
        Assert.False(File.Exists(cssPath + ".map"));
        Assert.Equal([cssPath], task.GeneratedFiles.Select(static file => file.ItemSpec));
    }

    [Fact]
    public void CompileTask_InRelease_MinifiesAndEmitsNoMap()
    {
        using var workspace = CreateWorkspace("compile-release");

        var task = CreateTask(workspace);
        task.Configuration = "Release";

        Assert.True(task.Execute());

        var cssPath = workspace.PathTo("wwwroot", "css", "app.css");
        var css = File.ReadAllText(cssPath);

        Assert.DoesNotContain("sourceMappingURL", css);
        Assert.False(File.Exists(cssPath + ".map"));

        // Minified output keeps the whole stylesheet on very few lines; the expanded form runs to hundreds.
        Assert.True(
            css.Split('\n').Length < 10,
            $"Expected minified output, got {css.Split('\n').Length} lines.");
    }

    [Fact]
    public void CompileTask_WithAnExternalMapPath_ProducesTwoFilesAndRecordsBoth()
    {
        using var workspace = CreateWorkspace("compile-external-map");

        var item = CreateItem();
        item.SetMetadata("Map", @"wwwroot\css\app.css.map");

        var task = CreateTask(workspace, item);

        Assert.True(task.Execute());

        var cssPath = workspace.PathTo("wwwroot", "css", "app.css");
        var mapPath = cssPath + ".map";

        Assert.True(File.Exists(mapPath), "An external map path should produce a second file.");
        Assert.Contains("sourceMappingURL=app.css.map", File.ReadAllText(cssPath));

        // Both have to be recorded, or Clean leaves the map behind and static web assets never sees it.
        Assert.Equal(
            [cssPath, mapPath],
            task.GeneratedFiles.Select(static file => file.ItemSpec));
    }

    [Fact]
    public void CompileTask_ItemMetadata_OverridesTheGlobalProperty()
    {
        using var workspace = CreateWorkspace("compile-metadata-override");

        var item = CreateItem();
        item.SetMetadata("Minify", "true");

        // Debug would otherwise mean unminified with an inline map.
        var task = CreateTask(workspace, item);
        task.Configuration = "Debug";

        Assert.True(task.Execute());

        var css = File.ReadAllText(workspace.PathTo("wwwroot", "css", "app.css"));
        Assert.True(css.Split('\n').Length < 10, "Item metadata should have won over the Debug default.");
    }

    [Fact]
    public void CompileTask_WithAllowedAdditionalArguments_ForwardsThem()
    {
        using var workspace = CreateWorkspace("compile-additional-arguments");

        var task = CreateTask(workspace);
        task.Configuration = "Debug";
        task.AdditionalArguments = "--minify";

        Assert.True(task.Execute());

        var css = File.ReadAllText(workspace.PathTo("wwwroot", "css", "app.css"));
        Assert.True(css.Split('\n').Length < 10, "The allowed --minify argument should reach Tailwind.");
    }

    [Fact]
    public void CompileTask_WithOptimizeEnabled_PassesTheOptimizeFlag()
    {
        using var workspace = CreateWorkspace("compile-optimize");
        var engine = new MockBuildEngine(_output);
        var task = CreateTask(workspace);
        task.BuildEngine = engine;
        task.Optimize = "true";

        Assert.True(task.Execute());
        Assert.Contains(
            engine.Messages,
            message => message.Message?.Contains(" --optimize ", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CompileTask_WithSilentEnabled_PassesTheSilentFlag()
    {
        using var workspace = CreateWorkspace("compile-silent");
        var engine = new MockBuildEngine(_output);
        var task = CreateTask(workspace);
        task.BuildEngine = engine;
        task.Silent = "true";

        Assert.True(task.Execute());
        Assert.Contains(
            engine.Messages,
            message => message.Message?.Contains(" --silent ", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CompileTask_WithBlankBooleanSettings_TreatsThemAsFalse()
    {
        using var workspace = CreateWorkspace("compile-blank-booleans");
        var engine = new MockBuildEngine(_output);
        var task = CreateTask(workspace);
        task.BuildEngine = engine;
        task.Optimize = "   ";
        task.Silent = "\t";

        Assert.True(task.Execute());

        var command = Assert.Single(
            engine.Messages,
            message => message.Message?.StartsWith("Executing:", StringComparison.Ordinal) == true).Message!;
        Assert.DoesNotContain("--optimize", command, StringComparison.Ordinal);
        Assert.DoesNotContain("--silent", command, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileTask_WithBlankAutomaticSettings_UsesTheConfigurationDefaults()
    {
        using var workspace = CreateWorkspace("compile-blank-auto");
        var engine = new MockBuildEngine(_output);
        var task = CreateTask(workspace);
        task.BuildEngine = engine;
        task.Configuration = "Release";
        task.Minify = "   ";
        task.Map = "\t";

        Assert.True(task.Execute());

        var command = Assert.Single(
            engine.Messages,
            message => message.Message?.StartsWith("Executing:", StringComparison.Ordinal) == true).Message!;
        Assert.Contains(" --minify ", command, StringComparison.Ordinal);
        Assert.DoesNotContain(" --map ", command, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void CompileTask_WithBooleanMapSetting_EmitsTheExpectedMapFlag(string map, bool expected)
    {
        using var workspace = CreateWorkspace($"compile-boolean-map-{map}");
        var engine = new MockBuildEngine(_output);
        var task = CreateTask(workspace);
        task.BuildEngine = engine;
        task.Map = map;

        Assert.True(task.Execute());

        var command = Assert.Single(
            engine.Messages,
            message => message.Message?.StartsWith("Executing:", StringComparison.Ordinal) == true).Message!;
        Assert.Equal(expected, command.Contains(" --map ", StringComparison.Ordinal));
    }

    [Fact]
    public void CompileTask_WithHelpArgument_LogsTailwindStandardOutput()
    {
        // Tailwind writes --help to stdout. This exercises the asynchronous stdout handler independently of
        // its ordinary progress output, which Tailwind writes to stderr.
        using var workspace = CreateWorkspace("compile-stdout");
        var engine = new MockBuildEngine(_output);
        var task = CreateTask(workspace);
        task.BuildEngine = engine;
        task.AdditionalArguments = "--help";

        Assert.True(task.Execute());
        Assert.Contains(
            engine.Messages,
            message => message.Message?.Contains("Usage:", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CompileTask_WithCwd_ResolvesItAgainstTheProjectDirectory()
    {
        using var workspace = CreateWorkspace("compile-custom-cwd");
        Directory.CreateDirectory(workspace.PathTo("scan-root"));
        var engine = new MockBuildEngine(_output);
        var task = CreateTask(workspace);
        task.BuildEngine = engine;
        task.Cwd = "scan-root";

        Assert.True(task.Execute());

        var command = Assert.Single(
            engine.Messages,
            message => message.Message?.StartsWith("Executing:", StringComparison.Ordinal) == true).Message!;
        Assert.Contains($"--cwd={workspace.PathTo("scan-root")}", command, StringComparison.Ordinal);
    }

    [Fact]
    public void CompileTask_WhenProcessFails_ReportsBothStandardOutputAndStandardError()
    {
        // Tailwind itself has no deterministic invocation that writes stdout and then fails: --help writes
        // stdout but succeeds, while compilation diagnostics use stderr. The probe exercises the complete
        // process and failure-reporting path without exposing LogOutputDetail solely for a direct test.
        using var workspace = CreateWorkspace("compile-process-output-failure");
        var runtimeDirectory = StageProcessProbe(workspace);
        var engine = new MockBuildEngine(_output);
        var task = CreateTask(workspace);
        task.BuildEngine = engine;
        task.RuntimeDirectory = runtimeDirectory;

        Assert.False(task.Execute());
        Assert.Contains(
            engine.Errors,
            error => error.Message?.Contains("exit code 3", StringComparison.Ordinal) == true);
        Assert.Contains(
            engine.Errors,
            error => error.Message?.Contains("STDOUT_CONTEXT", StringComparison.Ordinal) == true);
        Assert.Contains(
            engine.Errors,
            error => error.Message?.Contains("STDERR_CONTEXT", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CompileTask_GeneratedFiles_CarryRelativePathMetadata()
    {
        using var workspace = CreateWorkspace("compile-relative-path");

        var task = CreateTask(workspace);

        Assert.True(task.Execute());

        var generated = Assert.Single(task.GeneratedFiles);

        // The targets turn this into a Content item; an absolute path there packs the file under a
        // meaningless location.
        Assert.Equal(
            Path.Combine("wwwroot", "css", "app.css"),
            generated.GetMetadata("RelativePath"));
    }

    [Fact]
    public void CompileTask_WritesAManifestAndASettingsStamp()
    {
        using var workspace = CreateWorkspace("compile-manifest");

        var task = CreateTask(workspace);

        Assert.True(task.Execute());

        var manifest = workspace.PathTo("obj", "Scarlet.Tailwind", "Tailwind.generated.txt");
        var stamp = workspace.PathTo("obj", "Scarlet.Tailwind", "Tailwind.settings.stamp");

        Assert.True(File.Exists(manifest), "Clean reads the manifest to know what to delete.");
        Assert.True(File.Exists(stamp));
        Assert.Equal(
            [workspace.PathTo("wwwroot", "css", "app.css")],
            File.ReadAllLines(manifest));
    }

    [Fact]
    public void CompileTask_WithStampDirectorySet_WritesTheStampAndManifestThere()
    {
        using var workspace = CreateWorkspace("compile-stamp-directory");
        var task = CreateTask(workspace);
        task.StampDirectory = "custom-stamps";

        Assert.True(task.Execute());

        var stampDirectory = workspace.PathTo("custom-stamps");
        Assert.True(File.Exists(Path.Combine(stampDirectory, "Tailwind.settings.stamp")));
        Assert.True(File.Exists(Path.Combine(stampDirectory, "Tailwind.generated.txt")));
        Assert.False(Directory.Exists(workspace.PathTo("obj", "Scarlet.Tailwind")));
    }

    [Fact]
    public void CompileTask_WithAbsoluteStampDirectory_UsesItUnchanged()
    {
        using var workspace = CreateWorkspace("compile-absolute-stamp-directory");
        var stampDirectory = workspace.PathTo("absolute-stamps");
        var task = CreateTask(workspace);
        task.StampDirectory = stampDirectory;

        Assert.True(task.Execute());

        Assert.True(File.Exists(Path.Combine(stampDirectory, "Tailwind.settings.stamp")));
        Assert.True(File.Exists(Path.Combine(stampDirectory, "Tailwind.generated.txt")));
    }

    [Fact]
    public void CompileTask_WithMalformedRuntimePack_WarnsAndUsesTheExplicitRuntimeDirectory()
    {
        using var workspace = CreateWorkspace("compile-malformed-runtime-pack");
        var engine = new MockBuildEngine(_output);
        var task = CreateTask(workspace);
        task.BuildEngine = engine;
        task.RuntimePacks = [new TaskItem("Zephyr.Tailwind.Typo")];

        Assert.True(task.Execute());
        Assert.Contains(
            engine.Warnings,
            warning => warning.Message?.Contains("Zephyr.Tailwind.Typo", StringComparison.Ordinal) == true);
        Assert.Contains(
            engine.Warnings,
            warning => warning.Message?.Contains(TailwindRuntimePack.RidMetadataName, StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData(RuntimeSelection.VersionDownload)]
    [InlineData(RuntimeSelection.RuntimePackItem)]
    public void CompileTask_WithChangedRuntimeSelection_ChangesTheSettingsStamp(RuntimeSelection changed)
    {
        using var workspace = CreateWorkspace("compile-runtime-stamp");

        Assert.True(CreateTask(workspace).Execute());

        var stampPath = workspace.PathTo("obj", "Scarlet.Tailwind", "Tailwind.settings.stamp");
        var originalStamp = File.ReadAllText(stampPath);
        var second = CreateTask(workspace);

        if (changed == RuntimeSelection.VersionDownload)
        {
            second.TailwindVersionDownload = "1.2.3";
        }
        else
        {
            var pack = new TaskItem("Contoso.Tailwind.Runtime.custom");
            pack.SetMetadata(
                TailwindRuntimePack.RidMetadataName,
                TailwindRuntimeResolver.GetRuntimeIdentifier(TailwindRuntimeResolver.GetCurrentPlatform()));
            pack.SetMetadata(TailwindRuntimePack.RuntimesPathMetadataName, workspace.PathTo("custom-runtimes"));
            pack.SetMetadata(TailwindRuntimePack.PriorityMetadataName, "50");
            second.RuntimePacks = [pack];
        }

        Assert.True(second.Execute());
        Assert.NotEqual(originalStamp, File.ReadAllText(stampPath));
    }

    [Fact]
    public void CompileTask_WhenTheMapSettingChanges_ReportsTheNowUnusedFileAsRemoved()
    {
        using var workspace = CreateWorkspace("compile-removed");

        // First build: an external map, so two files exist and both are in the manifest.
        var withMap = CreateItem();
        withMap.SetMetadata("Map", @"wwwroot\css\app.css.map");
        Assert.True(CreateTask(workspace, withMap).Execute());

        var mapPath = workspace.PathTo("wwwroot", "css", "app.css.map");
        Assert.True(File.Exists(mapPath));

        // Second build: no map at all. The map file is now orphaned.
        var withoutMap = CreateItem();
        withoutMap.SetMetadata("Map", "false");
        var second = CreateTask(workspace, withoutMap);

        Assert.True(second.Execute());

        // Reported so the targets can drop it from @(Content), and deleted so it does not get served or
        // packed as a stale asset.
        Assert.Equal([mapPath], second.RemovedFiles.Select(static file => file.ItemSpec));
        Assert.False(File.Exists(mapPath), "A file no longer generated should not be left on disk.");
    }

    [Fact]
    public void CompileTask_WithNoEntryPoints_SucceedsWithoutRunningTailwind()
    {
        using var workspace = CreateWorkspace("compile-empty");

        var task = CreateTask(workspace);
        task.Compilations = [];

        Assert.True(task.Execute());
        Assert.False(Directory.Exists(workspace.PathTo("wwwroot")));
    }

    [Fact]
    public void CompileTask_WhenTailwindFails_ReportsTheError()
    {
        using var workspace = CreateWorkspace("compile-failure");

        // A directive Tailwind cannot resolve: the import target does not exist.
        workspace.WriteFile("Styles/app.css", "@import \"./does-not-exist.css\";");

        var engine = new MockBuildEngine(_output);
        var task = CreateTask(workspace);
        task.BuildEngine = engine;

        Assert.False(task.Execute());
        Assert.Contains(
            engine.Errors,
            error => error.Message?.Contains("does-not-exist.css", StringComparison.Ordinal) == true);
        Assert.False(File.Exists(workspace.PathTo("obj", "Scarlet.Tailwind", "Tailwind.settings.stamp")));
        Assert.False(File.Exists(workspace.PathTo("obj", "Scarlet.Tailwind", "Tailwind.generated.txt")));
    }

    [Fact]
    public void CompileTask_WhenTimeoutElapses_Fails()
    {
        // Tailwind has no supported long-running build mode: --watch is deliberately rejected because it
        // would hang an ordinary build when no timeout is configured. A 1ms budget still exercises the real
        // process-timeout path deterministically because starting Tailwind alone takes longer than that.
        using var workspace = CreateWorkspace("compile-timeout");

        var engine = new MockBuildEngine(_output);
        var task = CreateTask(workspace);
        task.BuildEngine = engine;
        task.TimeoutMilliseconds = 1;

        Assert.False(task.Execute());
        Assert.Contains(
            engine.Errors,
            error => error.Message?.Contains("Command timed out after 1ms", StringComparison.Ordinal) == true);
        Assert.False(File.Exists(workspace.PathTo("obj", "Scarlet.Tailwind", "Tailwind.settings.stamp")));
        Assert.False(File.Exists(workspace.PathTo("obj", "Scarlet.Tailwind", "Tailwind.generated.txt")));
    }

    [Fact]
    public void CompileTask_WithATimeoutOfZero_DoesNotTimeOut()
    {
        // 0 means "wait indefinitely", which is the default. A regression that treated 0 as "expire
        // immediately" would fail every build.
        using var workspace = CreateWorkspace("compile-no-timeout");

        var task = CreateTask(workspace);
        task.TimeoutMilliseconds = 0;

        Assert.True(task.Execute());
    }

    [Fact]
    public void CompileTask_WithPositiveTimeout_CompletesBeforeItExpires()
    {
        using var workspace = CreateWorkspace("compile-positive-timeout");

        var task = CreateTask(workspace);
        task.TimeoutMilliseconds = 30_000;

        Assert.True(task.Execute());
    }

    private TempWorkspace CreateWorkspace(string name)
    {
        var workspace = TempWorkspace.Create(name);

        workspace.WriteFile("Styles/app.css", EntryStylesheet);
        workspace.WriteFile(
            "Pages/Index.cshtml",
            """
            <h1 class="text-3xl font-bold">Hello</h1>
            <button class="bg-indigo-600 px-4 py-2 text-white">Go</button>
            """);

        return workspace;
    }

    private static TaskItem CreateItem()
    {
        var item = new TaskItem(Path.Combine("Styles", "app.css"));
        item.SetMetadata("OutputPath", Path.Combine("wwwroot", "css", "app.css"));

        return item;
    }

    private static string StageProcessProbe(TempWorkspace workspace)
    {
        var platform = TailwindRuntimeResolver.GetCurrentPlatform();
        var runtimeDirectory = workspace.PathTo("probe-runtimes");
        var nativeDirectory = Path.Combine(
            runtimeDirectory,
            TailwindRuntimeResolver.GetRuntimeIdentifier(platform),
            "native");
        Directory.CreateDirectory(nativeDirectory);

        var probeDirectory = Path.Combine(AppContext.BaseDirectory, "ProcessProbe");
        Assert.True(Directory.Exists(probeDirectory), $"Process probe was not copied to '{probeDirectory}'.");

        foreach (var source in Directory.GetFiles(probeDirectory))
        {
            File.Copy(source, Path.Combine(nativeDirectory, Path.GetFileName(source)));
        }

        var executable = Path.Combine(
            nativeDirectory,
            TailwindRuntimeResolver.GetExecutableName(platform));
        Assert.True(File.Exists(executable), $"Process probe executable was not staged at '{executable}'.");

        return runtimeDirectory;
    }

    private TailwindCompileTask CreateTask(TempWorkspace workspace, TaskItem? item = null) =>
        new()
        {
            BuildEngine = new MockBuildEngine(_output),
            Compilations = [item ?? CreateItem()],
            ProjectDirectory = workspace.RootDirectory,
            Configuration = "Debug",
            RuntimeDirectory = RuntimeDirectory
        };

    public enum RuntimeSelection
    {
        VersionDownload,
        RuntimePackItem
    }
}
