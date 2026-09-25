using Microsoft.Build.Utilities;
using Xunit.Abstractions;

namespace Scarlet.Tailwind.MSBuild.IntegrationTests;

/// <summary>
/// The download path, against the real GitHub release.
/// </summary>
/// <remarks>
/// Hits the network and fetches ~110 MB. Each test owns its runtime directory so cache assertions cannot
/// be satisfied accidentally by another test.
/// </remarks>
[Collection(nameof(TailwindCompileTaskDownloadIntegrationTests))]
[CollectionDefinition(nameof(TailwindCompileTaskDownloadIntegrationTests), DisableParallelization = true)]
public class TailwindCompileTaskDownloadIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public TailwindCompileTaskDownloadIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CompileTask_WithDownloadEnabled_FetchesTailwindAndCompiles()
    {
        using var workspace = CreateWorkspace("download-compile");
        var runtimeDirectory = workspace.PathTo("runtimes");

        var task = CreateTask(workspace, runtimeDirectory);

        Assert.True(task.Execute());

        var executable = Path.Combine(
            runtimeDirectory,
            TailwindRuntimeResolver.GetRuntimeIdentifier(TailwindRuntimeResolver.GetCurrentPlatform()),
            "native",
            TailwindRuntimeResolver.GetExecutableName(TailwindRuntimeResolver.GetCurrentPlatform()));

        Assert.True(File.Exists(executable), $"Expected a downloaded Tailwind at {executable}.");

        // The marker is what makes the cache trustworthy: it is written only after the binary is in place,
        // so its presence means a complete download rather than an interrupted one.
        Assert.True(File.Exists(executable + ".version"));
        Assert.Equal(PinnedTailwindVersion(), File.ReadAllText(executable + ".version").Trim());

        Assert.Contains(".text-3xl", File.ReadAllText(workspace.PathTo("wwwroot", "css", "app.css")));
    }

    [Fact]
    public void CompileTask_WithEmptyVersionDownload_ResolvesLatestAndCompiles()
    {
        using var workspace = CreateWorkspace("download-latest");
        var runtimeDirectory = workspace.PathTo("runtimes");
        var task = CreateTask(workspace, runtimeDirectory);
        task.TailwindVersionDownload = null;

        Assert.True(task.Execute());

        var executable = Path.Combine(
            runtimeDirectory,
            TailwindRuntimeResolver.GetRuntimeIdentifier(TailwindRuntimeResolver.GetCurrentPlatform()),
            "native",
            TailwindRuntimeResolver.GetExecutableName(TailwindRuntimeResolver.GetCurrentPlatform()));
        var marker = executable + ".version";

        Assert.True(File.Exists(executable));
        Assert.True(File.Exists(marker));
        Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(marker)));
        Assert.Contains(".text-3xl", File.ReadAllText(workspace.PathTo("wwwroot", "css", "app.css")));
    }

    [Fact]
    public void CompileTask_WithACompleteCache_DoesNotDownloadAgain()
    {
        using var workspace = CreateWorkspace("download-cached");
        var runtimeDirectory = workspace.PathTo("runtimes");

        Assert.True(CreateTask(workspace, runtimeDirectory).Execute());

        var executable = Path.Combine(
            runtimeDirectory,
            TailwindRuntimeResolver.GetRuntimeIdentifier(TailwindRuntimeResolver.GetCurrentPlatform()),
            "native",
            TailwindRuntimeResolver.GetExecutableName(TailwindRuntimeResolver.GetCurrentPlatform()));

        var firstWrite = File.GetLastWriteTimeUtc(executable);

        var engine = new MockBuildEngine(_output);
        var second = CreateTask(workspace, runtimeDirectory);
        second.BuildEngine = engine;

        Assert.True(second.Execute());

        // Re-downloading would replace the file and move the timestamp.
        Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(executable));
        Assert.Contains(
            engine.Messages,
            message => message.Message?.Contains("already cached", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void CompileTask_WhenTheMarkerIsMissing_DownloadsAgainRatherThanTrustingTheBinary()
    {
        using var workspace = CreateWorkspace("download-no-marker");
        var runtimeDirectory = workspace.PathTo("runtimes");

        Assert.True(CreateTask(workspace, runtimeDirectory).Execute());

        var executable = Path.Combine(
            runtimeDirectory,
            TailwindRuntimeResolver.GetRuntimeIdentifier(TailwindRuntimeResolver.GetCurrentPlatform()),
            "native",
            TailwindRuntimeResolver.GetExecutableName(TailwindRuntimeResolver.GetCurrentPlatform()));

        // A binary with no marker is exactly what an interrupted download leaves behind, so it must not be
        // treated as a cache hit.
        File.Delete(executable + ".version");

        Assert.True(CreateTask(workspace, runtimeDirectory).Execute());
        Assert.True(File.Exists(executable + ".version"), "The marker should have been rewritten.");
    }

    /// <summary>
    /// The version pinned in <c>Directory.Build.props</c>, which is what the samples request.
    /// </summary>
    private static string PinnedTailwindVersion()
    {
        var props = System.Xml.Linq.XDocument.Load(Path.Combine(RepositoryRoot.Path, "Directory.Build.props"));

        return props.Root!.Descendants("TailwindVersion").Single().Value.Trim();
    }

    private static TempWorkspace CreateWorkspace(string name)
    {
        var workspace = TempWorkspace.Create(name);

        workspace.WriteFile("Styles/app.css", "@import \"tailwindcss\";\n\n@source not \"./bin\";\n@source not \"./obj\";");
        workspace.WriteFile("Pages/Index.cshtml", "<h1 class=\"text-3xl font-bold\">Hello</h1>");

        return workspace;
    }

    private TailwindCompileTask CreateTask(TempWorkspace workspace, string runtimeDirectory)
    {
        var item = new TaskItem(Path.Combine("Styles", "app.css"));
        item.SetMetadata("OutputPath", Path.Combine("wwwroot", "css", "app.css"));

        return new TailwindCompileTask
        {
            BuildEngine = new MockBuildEngine(_output),
            Compilations = [item],
            ProjectDirectory = workspace.RootDirectory,
            Configuration = "Debug",
            TailwindRuntimeDownload = true,
            TailwindVersionDownload = PinnedTailwindVersion(),
            RuntimeDirectory = runtimeDirectory
        };
    }
}
