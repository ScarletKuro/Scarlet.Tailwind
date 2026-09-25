using System.IO.Compression;
using System.Security;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Scarlet.Tailwind.MSBuild.IntegrationTests;

/// <summary>
/// Covers the <c>RunTailwindBeforeStaticWebAssets</c> target end to end, by running real <c>dotnet build</c>,
/// <c>pack</c> and <c>publish</c> invocations and asserting on what they produce.
/// </summary>
/// <remarks>
/// These tests focus on outcomes that only a real SDK build can prove: static web assets packing, publish
/// fingerprinting, the real <c>TailwindTimeoutMilliseconds</c> MSBuild property (as opposed to setting
/// <c>TailwindCompileTask.TimeoutMilliseconds</c> directly in-process, which
/// <see cref="TailwindCompileTaskIntegrationTests"/> already covers), and <c>TailwindClean</c>.
/// </remarks>
public class TailwindBeforeStaticWebAssetsTests
{
    private const string GeneratedAsset = "css/app.css";
    private const string TargetFramework = "net10.0";

    private readonly ITestOutputHelper _output;

    public TailwindBeforeStaticWebAssetsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SingleTargetFrameworkPack_IncludesGeneratedWwwrootFilesAsStaticWebAssets()
    {
        using var workspace = CreateRazorClassLibrary();

        await Pack(workspace);

        // staticwebassets/ is what makes the file reachable as _content/<PackageId>/css/site.css in a
        // consuming app. An absolute Content glob still packs the file, but under content/ and
        // contentFiles/, where no consumer will ever serve it.
        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    [Fact]
    public async Task RepeatedPack_DoesNotDuplicateGeneratedStaticWebAssets()
    {
        using var workspace = CreateRazorClassLibrary();

        await Pack(workspace);

        // The second pack evaluates the project with wwwroot already populated, so the SDK's own glob has
        // already claimed the generated file and the target re-adds it on top. Nothing about that path is
        // exercised by a clean build, which is the only case the other tests cover.
        await Pack(workspace);

        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    [Fact]
    public async Task WebProjectPublish_FingerprintsGeneratedStaticWebAssets()
    {
        using var workspace = CreateWebApplication();

        var result = await RunDotnet(workspace, $"publish --configuration {DotnetCli.Configuration} --output publish");
        Assert.Equal(0, result.ExitCode);

        var endpoints = Assert.Single(
            Directory.GetFiles(workspace.PathTo("publish"), "*.staticwebassets.endpoints.json"));
        var manifest = await File.ReadAllTextAsync(endpoints);

        // A fingerprinted route proves the file went through the static web assets pipeline rather than
        // being copied to the publish directory as plain content, which is what the README promises.
        Assert.Matches(new Regex("""
            "Route"\s*:\s*"css/app\.[a-z0-9]+\.css"
            """.Trim()), manifest);
    }

    [Fact]
    public async Task TailwindTimeoutMillisecondsProperty_ShouldKillABuildThatOverruns()
    {
        // The real MSBuild-property counterpart to TailwindCompileTaskIntegrationTests' direct-task timeout
        // test: that one sets TailwindCompileTask.TimeoutMilliseconds in-process and never touches the
        // .targets file at all, so it cannot catch $(TailwindTimeoutMilliseconds) being dropped on the way
        // to the task.
        //
        // A 1ms budget rather than a deliberately slow Tailwind. Process startup alone is tens of
        // milliseconds, so this fires every time, and it needs no workload that might get faster.
        //
        // --watch would be the obvious way to make a build genuinely overrun, and it is not used here:
        // without a TTY it exits immediately instead of watching, so the build finishes and then fails
        // somewhere else entirely, proving nothing about the timeout.
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <TailwindTimeoutMilliseconds>1</TailwindTimeoutMilliseconds>
            """);

        var result = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("timed out after 1ms", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailingTailwind_FailsTheBuildByDefault()
    {
        using var workspace = CreateRazorClassLibrary();
        workspace.WriteFile("Styles/app.css", "@import \"./nothing-here.css\";");

        var result = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Tailwind command failed", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TailwindEnabledProperty_WhenFalse_ShouldSkipCompilation()
    {
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <TailwindEnabled>false</TailwindEnabled>
            """);

        var result = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");

        Assert.Equal(0, result.ExitCode);
        Assert.False(
            File.Exists(workspace.PathTo("wwwroot", "css", "app.css")),
            "TailwindEnabled=false should prevent the target from compiling the entry point.");
    }

    [Fact]
    public async Task TailwindStampDirectoryProperty_ShouldPutTheStampAndManifestWhereItAsks()
    {
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <TailwindStampDirectory>custom/stamps</TailwindStampDirectory>
            """);

        var build = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, build.ExitCode);

        var stamp = workspace.PathTo("custom", "stamps", "Tailwind.settings.stamp");
        var manifest = workspace.PathTo("custom", "stamps", "Tailwind.generated.txt");
        Assert.True(File.Exists(stamp), $"Expected a settings stamp at {stamp}.");
        Assert.True(File.Exists(manifest), $"Expected a generated-file manifest at {manifest}.");

        var defaultStampDirectory = workspace.PathTo("obj", DotnetCli.Configuration, TargetFramework, "Scarlet.Tailwind");
        Assert.False(File.Exists(Path.Combine(defaultStampDirectory, "Tailwind.settings.stamp")));
        Assert.False(File.Exists(Path.Combine(defaultStampDirectory, "Tailwind.generated.txt")));

        var clean = await RunDotnet(workspace, $"clean --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, clean.ExitCode);

        Assert.False(File.Exists(stamp), "dotnet clean should remove the requested settings stamp.");
        Assert.False(File.Exists(manifest), "dotnet clean should remove the requested generated-file manifest.");
    }

    [Fact]
    public async Task ConfigurationProperty_ShouldDriveTheDebugAndReleaseDefaults()
    {
        // $(Configuration) is the one task input whose C# default ("Debug") agrees with what most tests
        // build, so dropping Configuration="$(Configuration)" from the .targets changes nothing observable
        // unless a test looks for it. Unwired, the task always believes it is a Debug build, and a Release
        // build silently ships unminified CSS with an inline source map.
        //
        // The spawned build has to use the same configuration as this assembly - the development targets
        // resolve the task from bin\$(Configuration)\netstandard2.0 - so this asserts whichever half applies
        // to the current run. CI builds Release, which is the half that matters: there, a dropped wiring
        // makes the task fall back to Debug and this fails.
        using var workspace = CreateRazorClassLibrary();

        var build = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, build.ExitCode);

        var css = await File.ReadAllTextAsync(workspace.PathTo("wwwroot", "css", "app.css"));

        if (string.Equals(DotnetCli.Configuration, "Debug", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Contains("sourceMappingURL=data:", css, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("sourceMappingURL", css, StringComparison.Ordinal);
            Assert.True(css.Split('\n').Length < 10, "A Release build should minify.");
        }
    }

    [Fact]
    public async Task TailwindMinifyAndMapProperties_ShouldOverrideTheConfigurationDefaults()
    {
        // Proves both properties survive the trip through the .targets, and that an explicit setting beats
        // whatever the configuration would otherwise imply - which is the whole point of them being settable.
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <TailwindMinify>true</TailwindMinify>
            <TailwindMap>false</TailwindMap>
            """);

        var build = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, build.ExitCode);

        var css = await File.ReadAllTextAsync(workspace.PathTo("wwwroot", "css", "app.css"));
        Assert.True(css.Split('\n').Length < 10, "TailwindMinify=true should minify even in a Debug build.");
        Assert.DoesNotContain("sourceMappingURL", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TailwindOptimizeProperty_ShouldReachTheTask()
    {
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <TailwindMinify>false</TailwindMinify>
            <TailwindOptimize>true</TailwindOptimize>
            """);

        var build = await RunDotnet(
            workspace,
            $"build --configuration {DotnetCli.Configuration} --verbosity normal");

        Assert.Equal(0, build.ExitCode);
        Assert.Contains(" --optimize ", build.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(" --minify ", build.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TailwindAdditionalArgumentsProperty_ShouldReachTheTask()
    {
        // Tailwind v4 deliberately ignores unrecognised flags, which gives this wiring test a harmless,
        // unique token to find in the rendered command without changing the generated files.
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <TailwindAdditionalArguments>--future-tailwind-option</TailwindAdditionalArguments>
            """);

        var build = await RunDotnet(
            workspace,
            $"build --configuration {DotnetCli.Configuration} --verbosity normal");

        Assert.Equal(0, build.ExitCode);
        Assert.Contains("--future-tailwind-option", build.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TailwindMapProperty_WithAPath_ShouldWriteAnExternalMapAndPackItToo()
    {
        // The two-file case. It has to reach static web assets as well, or the browser asks for a .map the
        // published app does not have.
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <TailwindMap>wwwroot/css/app.css.map</TailwindMap>
            """);

        await Pack(workspace);

        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}.map"));
    }

    [Fact]
    public async Task TailwindSilentProperty_ShouldSuppressTailwindsOwnChatter()
    {
        // A load-bearing assertion about wiring rather than about quiet builds: --silent is only observable
        // through Tailwind's output, so this fails if $(TailwindSilent) never reaches the task.
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <TailwindSilent>true</TailwindSilent>
            """);

        var build = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration} --verbosity normal");
        Assert.Equal(0, build.ExitCode);

        // Tailwind prints a "Done in NNms" line on every run unless silenced.
        Assert.DoesNotContain("Done in", build.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TailwindCwdProperty_ShouldMoveTheScanRoot()
    {
        // --cwd is only observable through which files get scanned, so this is also the assertion that the
        // property reaches Tailwind rather than being silently ignored.
        //
        // The scan root moves *down* into a subdirectory, never up with "..". Pointing it at the parent
        // would make Tailwind walk everything beside the workspace - on a CI runner that is the whole of
        // /tmp, including the NuGet caches - and the build times out instead of failing. Moving down proves
        // the same thing and stays bounded.
        using var workspace = CreateRazorClassLibrary(
            additionalProperties: """
            <TailwindCwd>ScanRoot</TailwindCwd>
            """);

        workspace.WriteFile("ScanRoot/Inside.cshtml", "<div class=\"tracking-widest\"></div>");

        var build = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, build.ExitCode);

        var css = await File.ReadAllTextAsync(workspace.PathTo("wwwroot", "css", "app.css"));

        // Found, because it is under the new scan root.
        Assert.Contains(".tracking-widest", css, StringComparison.Ordinal);

        // Not found, because Pages/Index.cshtml is outside it. This half is what makes the test meaningful:
        // without it, a build that ignored --cwd and scanned the whole project would also pass.
        Assert.DoesNotContain(".text-3xl", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_RemovesTheGeneratedStampManifestAndCss()
    {
        using var workspace = CreateRazorClassLibrary();

        var build = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, build.ExitCode);

        var stampDirectory = workspace.PathTo("obj", DotnetCli.Configuration, TargetFramework, "Scarlet.Tailwind");
        var stamp = Path.Combine(stampDirectory, "Tailwind.settings.stamp");
        var manifest = Path.Combine(stampDirectory, "Tailwind.generated.txt");
        Assert.True(File.Exists(stamp), $"Expected a settings stamp at {stamp}.");
        Assert.True(File.Exists(manifest), $"Expected a generated-file manifest at {manifest}.");

        var clean = await RunDotnet(workspace, $"clean --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, clean.ExitCode);

        Assert.False(File.Exists(stamp), "dotnet clean should remove the settings stamp.");
        Assert.False(File.Exists(manifest), "dotnet clean should remove the generated-file manifest.");
        Assert.False(
            File.Exists(workspace.PathTo("wwwroot", "css", "app.css")),
            "dotnet clean should remove the generated CSS, which it learns about from the manifest.");
    }

    /// <summary>
    /// The entry stylesheet and the markup that gives the scanner something to find.
    /// </summary>
    private static void WriteSources(TempWorkspace workspace)
    {
        workspace.WriteFile(
            "Styles/app.css",
            """
            @import "tailwindcss";

            @source not "./bin";
            @source not "./obj";
            """);

        workspace.WriteFile(
            "Pages/Index.cshtml",
            "<h1 class=\"text-3xl font-bold\">Hello</h1>");
    }

    /// <summary>
    /// A Razor Class Library is the strictest case: its wwwroot files have to be packed under
    /// staticwebassets/ for a consuming app to serve them.
    /// </summary>
    private static TempWorkspace CreateRazorClassLibrary(string additionalProperties = "")
    {
        var workspace = TempWorkspace.Create("static-web-assets");

        try
        {
            WriteSources(workspace);
            workspace.WriteFile(
                "GeneratedAssetsRcl.csproj",
                Project("Microsoft.NET.Sdk.Razor", additionalProperties));

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static TempWorkspace CreateWebApplication()
    {
        var workspace = TempWorkspace.Create("static-web-assets-web");

        try
        {
            WriteSources(workspace);
            workspace.WriteFile(
                "Program.cs",
                "WebApplication.CreateBuilder(args).Build().Run();");
            workspace.WriteFile(
                "GeneratedAssetsWeb.csproj",
                Project("Microsoft.NET.Sdk.Web", additionalProperties: "<ImplicitUsings>enable</ImplicitUsings>"));

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static string Project(string sdk, string additionalProperties) =>
        $"""
        <Project Sdk="{sdk}">
          <PropertyGroup>
            <TargetFramework>{TargetFramework}</TargetFramework>
            <IsPackable>true</IsPackable>
            {additionalProperties}
          </PropertyGroup>

          <ItemGroup>
            <FrameworkReference Include="Microsoft.AspNetCore.App" />
          </ItemGroup>

          <Import Project="{SecurityElement.Escape(DevelopmentTargetsPath)}" />

          <ItemGroup>
            <TailwindBeforeStaticWebAssets Include="Styles/app.css">
              <OutputPath>wwwroot/css/app.css</OutputPath>
            </TailwindBeforeStaticWebAssets>
          </ItemGroup>
        </Project>
        """;

    private static string DevelopmentTargetsPath => Path.Combine(
        RepositoryRoot.Path, "src", "Scarlet.Tailwind.MSBuild", "Scarlet.Tailwind.MSBuild.targets");

    private async Task Pack(TempWorkspace workspace)
    {
        var result = await RunDotnet(workspace, $"pack --configuration {DotnetCli.Configuration} --output nupkg");

        Assert.Equal(0, result.ExitCode);
    }

    private async Task<DotnetResult> RunDotnet(TempWorkspace workspace, string arguments)
    {
        var result = await DotnetCli.Run(workspace.RootDirectory, arguments);
        _output.WriteLine(result.Output);

        return result;
    }


    private static int CountPackageEntries(TempWorkspace workspace, string entryName)
    {
        var packagePath = Assert.Single(Directory.GetFiles(workspace.PathTo("nupkg"), "*.nupkg"));

        using var package = ZipFile.OpenRead(packagePath);

        return package.Entries.Count(entry => entry.FullName == entryName);
    }
}
