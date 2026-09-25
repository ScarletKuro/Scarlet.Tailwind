using Microsoft.Build.Utilities;
using Xunit.Abstractions;

namespace Scarlet.Tailwind.MSBuild.IntegrationTests;

/// <summary>
/// What the task rejects, and what it says when it does.
/// </summary>
/// <remarks>
/// Each of these fails before Tailwind is ever started, so they need no runtime and run in milliseconds.
/// The messages matter as much as the failure: a build that stops with "item must specify OutputPath
/// metadata" is fixable, one that stops with a null reference is not.
/// </remarks>
public class TailwindCompileTaskValidationTests
{
    private readonly ITestOutputHelper _output;

    public TailwindCompileTaskValidationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CompileTask_WithoutOutputPathMetadata_FailsAndNamesTheItem()
    {
        using var workspace = TempWorkspace.Create("validate-no-output");
        workspace.WriteFile("Styles/app.css", "@import \"tailwindcss\";");

        var engine = new MockBuildEngine(_output);
        var task = new TailwindCompileTask
        {
            BuildEngine = engine,
            Compilations = [new TaskItem(Path.Combine("Styles", "app.css"))],
            ProjectDirectory = workspace.RootDirectory
        };

        Assert.False(task.Execute());

        var error = Assert.Single(engine.Errors);
        Assert.Contains("OutputPath", error.Message);
        Assert.Contains("app.css", error.Message);
    }

    [Fact]
    public void CompileTask_WithAMissingInput_FailsAndNamesIt()
    {
        using var workspace = TempWorkspace.Create("validate-missing-input");

        var item = new TaskItem(Path.Combine("Styles", "nope.css"));
        item.SetMetadata("OutputPath", Path.Combine("wwwroot", "css", "app.css"));

        var engine = new MockBuildEngine(_output);
        var task = new TailwindCompileTask
        {
            BuildEngine = engine,
            Compilations = [item],
            ProjectDirectory = workspace.RootDirectory
        };

        Assert.False(task.Execute());

        var error = Assert.Single(engine.Errors);
        Assert.Contains("nope.css", error.Message);
    }

    [Theory]
    [InlineData("Minify", "TailwindMinify")]
    [InlineData("Optimize", "TailwindOptimize")]
    [InlineData("Silent", "TailwindSilent")]
    public void CompileTask_WithANonBooleanSetting_FailsNamingTheProperty(string property, string expectedName)
    {
        using var workspace = TempWorkspace.Create($"validate-{property}");
        workspace.WriteFile("Styles/app.css", "@import \"tailwindcss\";");

        var engine = new MockBuildEngine(_output);
        var task = new TailwindCompileTask
        {
            BuildEngine = engine,
            Compilations = [CreateItem()],
            ProjectDirectory = workspace.RootDirectory
        };

        typeof(TailwindCompileTask).GetProperty(property)!.SetValue(task, "yes-please");

        Assert.False(task.Execute());

        // The MSBuild property name, not the task parameter name: that is what the user wrote and what they
        // have to go and change.
        Assert.Contains(engine.Errors, error => error.Message?.Contains(expectedName, StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// <c>TailwindMap</c> is four-valued, so an unrecognised value is a path rather than an error.
    /// </summary>
    /// <remarks>
    /// Deliberately asymmetric with the booleans above and worth pinning: there is no value this property
    /// can reject, because anything that is not Auto/true/false is a legitimate output path.
    /// </remarks>
    [Fact]
    public void CompileTask_WithAnArbitraryMapValue_TreatsItAsAPathRatherThanFailing()
    {
        using var workspace = TempWorkspace.Create("validate-map-path");
        workspace.WriteFile("Styles/app.css", "@import \"tailwindcss\";");

        var engine = new MockBuildEngine(_output);
        var task = new TailwindCompileTask
        {
            BuildEngine = engine,
            Compilations = [CreateItem()],
            ProjectDirectory = workspace.RootDirectory,
            Map = "anything-at-all.map",
            // No runtime, so execution stops before Tailwind runs. Settings resolution has already happened
            // by then, which is all this test is about.
            RuntimeDirectory = Path.Combine(workspace.RootDirectory, "no-runtime-here")
        };

        Assert.False(task.Execute());

        // Failed for want of a runtime, not because the value was rejected.
        Assert.DoesNotContain(engine.Errors, error => error.Message?.Contains("TailwindMap", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void CompileTask_WithDownloadEnabledButNoRuntimeDirectory_SaysWhichPropertyIsMissing()
    {
        using var workspace = TempWorkspace.Create("validate-download-no-dir");
        workspace.WriteFile("Styles/app.css", "@import \"tailwindcss\";");

        var engine = new MockBuildEngine(_output);
        var task = new TailwindCompileTask
        {
            BuildEngine = engine,
            Compilations = [CreateItem()],
            ProjectDirectory = workspace.RootDirectory,
            TailwindRuntimeDownload = true,
            RuntimeDirectory = null
        };

        Assert.False(task.Execute());

        var error = Assert.Single(engine.Errors);
        Assert.Contains("TailwindRuntimeDirectory", error.Message);
    }

    [Theory]
    [InlineData("--watch")]
    [InlineData("--watch=always")]
    [InlineData("-w")]
    [InlineData("--poll=100")]
    [InlineData("--input other.css")]
    [InlineData("-i=other.css")]
    [InlineData("--output=other.css")]
    [InlineData("-o other.css")]
    [InlineData("--cwd ..")]
    [InlineData("--map=other.css.map")]
    public void CompileTask_WithUnsafeGlobalAdditionalArgument_FailsBeforeResolvingARuntime(string argument)
    {
        using var workspace = TempWorkspace.Create("validate-global-additional-argument");
        workspace.WriteFile("Styles/app.css", "@import \"tailwindcss\";");

        var engine = new MockBuildEngine(_output);
        var task = new TailwindCompileTask
        {
            BuildEngine = engine,
            Compilations = [CreateItem()],
            ProjectDirectory = workspace.RootDirectory,
            AdditionalArguments = argument
        };

        Assert.False(task.Execute());

        var error = Assert.Single(engine.Errors);
        Assert.Contains(argument.Split(' ')[0], error.Message);
        Assert.DoesNotContain("runtime package not found", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompileTask_WithUnsafeItemAdditionalArgument_NamesTheItem()
    {
        using var workspace = TempWorkspace.Create("validate-item-additional-argument");
        workspace.WriteFile("Styles/app.css", "@import \"tailwindcss\";");

        var item = CreateItem();
        item.SetMetadata("AdditionalArguments", "--output elsewhere.css");
        var engine = new MockBuildEngine(_output);
        var task = new TailwindCompileTask
        {
            BuildEngine = engine,
            Compilations = [item],
            ProjectDirectory = workspace.RootDirectory
        };

        Assert.False(task.Execute());

        var error = Assert.Single(engine.Errors);
        Assert.Contains("app.css", error.Message);
        Assert.Contains("--output", error.Message);
    }

    /// <summary>
    /// No packs and no explicit directory: nothing told the build where Tailwind is.
    /// </summary>
    /// <remarks>
    /// The first error a new consumer hits, so it names both ways out rather than just stating the
    /// problem.
    /// </remarks>
    [Fact]
    public void CompileTask_WithNoRuntimeAtAll_ExplainsHowToGetOne()
    {
        using var workspace = TempWorkspace.Create("validate-no-runtime");
        workspace.WriteFile("Styles/app.css", "@import \"tailwindcss\";");

        var engine = new MockBuildEngine(_output);
        var task = new TailwindCompileTask
        {
            BuildEngine = engine,
            Compilations = [CreateItem()],
            ProjectDirectory = workspace.RootDirectory
        };

        Assert.False(task.Execute());

        var error = Assert.Single(engine.Errors);
        Assert.Contains("Scarlet.Tailwind.Runtime.", error.Message);
        Assert.Contains("TailwindRuntimeDownload", error.Message);
        Assert.DoesNotContain("at Scarlet.Tailwind.", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An explicit directory that holds no Tailwind gets a different message from the no-pack case.
    /// </summary>
    /// <remarks>
    /// Kept distinct on purpose: here the user made a deliberate choice and got the path wrong, so telling
    /// them to install a runtime package would be advice for a problem they do not have. The message names
    /// the property they set and the exact layout it expects underneath.
    /// </remarks>
    [Fact]
    public void CompileTask_WithAnExplicitDirectoryThatHasNoTailwind_NamesThePropertyAndTheExpectedLayout()
    {
        using var workspace = TempWorkspace.Create("validate-empty-runtime-dir");
        workspace.WriteFile("Styles/app.css", "@import \"tailwindcss\";");

        var engine = new MockBuildEngine(_output);
        var task = new TailwindCompileTask
        {
            BuildEngine = engine,
            Compilations = [CreateItem()],
            ProjectDirectory = workspace.RootDirectory,
            RuntimeDirectory = Path.Combine(workspace.RootDirectory, "empty")
        };

        Assert.False(task.Execute());

        var error = Assert.Single(engine.Errors);
        Assert.Contains("TailwindRuntimeDirectory", error.Message);

        var platform = TailwindRuntimeResolver.GetCurrentPlatform();
        Assert.Contains(
            $"{TailwindRuntimeResolver.GetRuntimeIdentifier(platform)}/native/{TailwindRuntimeResolver.GetExecutableName(platform)}",
            error.Message);
    }

    private static TaskItem CreateItem()
    {
        var item = new TaskItem(Path.Combine("Styles", "app.css"));
        item.SetMetadata("OutputPath", Path.Combine("wwwroot", "css", "app.css"));

        return item;
    }
}
