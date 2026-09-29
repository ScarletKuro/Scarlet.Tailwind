using System.Xml.Linq;

namespace Scarlet.Tailwind.MSBuild.Tests;

/// <summary>
/// The package ships <c>build/Scarlet.Tailwind.MSBuild.targets</c> for single-target-framework projects and
/// <c>buildMultiTargeting/Scarlet.Tailwind.MSBuild.targets</c> for multi-targeted ones, while the samples
/// and integration tests import <c>Scarlet.Tailwind.MSBuild.targets</c> from the source tree. All three are
/// hand-maintained copies, so anything proven against the development copy only holds for real consumers
/// while the three agree.
/// </summary>
public class TailwindTargetsTests
{
    private const string PackagedTargets = "build/Scarlet.Tailwind.MSBuild.targets";
    private const string MultiTargetingTargets = "buildMultiTargeting/Scarlet.Tailwind.MSBuild.targets";
    private const string DevelopmentTargets = "Scarlet.Tailwind.MSBuild.targets";

    private static readonly IReadOnlyDictionary<string, string> PublicPropertyDefaults =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TailwindEnabled"] = "true",
            ["TailwindMinify"] = "Auto",
            ["TailwindOptimize"] = "false",
            ["TailwindMap"] = "Auto",
            ["TailwindSilent"] = "false",
            ["TailwindCwd"] = string.Empty,
            ["TailwindAdditionalArguments"] = string.Empty,
            ["TailwindRuntimeDirectory"] = string.Empty,
            ["TailwindRuntimeDownload"] = "false",
            ["TailwindVersionDownload"] = string.Empty,
            ["TailwindDownloadMutexTimeoutSeconds"] = "300",
            ["TailwindManifestDirectory"] = string.Empty,
            ["TailwindTimeoutMilliseconds"] = "0"
        };

    private static readonly string[] TargetNames =
    [
        "_TailwindResolveManifestDirectory",
        "_TailwindResolveWatchConfiguration",
        "ResolveTailwindWatchConfiguration",
        "RunTailwindBeforeStaticWebAssets",
        "TailwindClean"
    ];

    /// <summary>
    /// The two NuGet import paths must expose the complete public property surface with the documented
    /// defaults. An exact comparison is deliberate: checking only properties that happen to be present
    /// cannot notice one being deleted from both copies.
    /// </summary>
    [Theory]
    [InlineData("build/Scarlet.Tailwind.MSBuild.props")]
    [InlineData("buildMultiTargeting/Scarlet.Tailwind.MSBuild.props")]
    public void Props_ShouldDefineEveryPublicPropertyAndDefault(string propsRelativePath)
    {
        var properties = LoadProject(propsRelativePath)
            .Elements("PropertyGroup")
            .Elements()
            .ToDictionary(property => property.Name.LocalName, property => property, StringComparer.Ordinal);

        Assert.Equal(
            PublicPropertyDefaults.Keys.OrderBy(name => name, StringComparer.Ordinal),
            properties.Keys.OrderBy(name => name, StringComparer.Ordinal));

        foreach (var expected in PublicPropertyDefaults)
        {
            var property = properties[expected.Key];
            Assert.Equal(expected.Value, property.Value);
            Assert.Equal($"'$({expected.Key})' == ''", property.Attribute("Condition")?.Value);
        }
    }

    [Theory]
    [InlineData(MultiTargetingTargets)]
    [InlineData(DevelopmentTargets)]
    public void EveryTargetsFile_ShouldDefineTheSameTargets(string otherTargets)
    {
        // Comparing the target bodies below only covers targets that exist in both files; without this, a
        // target added to one copy alone would go unnoticed.
        var packaged = LoadTargetNames(PackagedTargets);
        var other = LoadTargetNames(otherTargets);

        Assert.Equal(packaged, other);
    }

    /// <summary>
    /// The list this fixture iterates must be the list the targets file actually declares.
    /// </summary>
    /// <remarks>
    /// Without this, renaming a target and forgetting to update <see cref="TargetNames"/> leaves the
    /// comparison below silently covering one target fewer, and a wiring mistake in the dropped target
    /// ships. The failure mode a hand-maintained list always has.
    /// </remarks>
    [Fact]
    public void TargetNames_ShouldListEveryTargetTheFileDeclares()
    {
        Assert.Equal(
            TargetNames.OrderBy(name => name, StringComparer.Ordinal),
            LoadTargetNames(PackagedTargets));
    }

    /// <summary>
    /// The multi-targeting copy is a verbatim duplicate: nothing about it legitimately differs.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryTargetName))]
    public void MultiTargetingTargets_ShouldBeIdenticalToPackagedTargets(string targetName)
    {
        var packagedTarget = LoadTarget(PackagedTargets, targetName);
        var multiTargetingTarget = LoadTarget(MultiTargetingTargets, targetName);

        AssertSameTarget(targetName, PackagedTargets, packagedTarget, MultiTargetingTargets, multiTargetingTarget);
    }

    [Theory]
    // The development copy has to build the task assembly before it can call into it, so it prefixes one
    // extra dependency; the packaged copy ships that assembly and must not carry it.
    [InlineData("_TailwindResolveManifestDirectory", null)]
    [InlineData("_TailwindResolveWatchConfiguration", "ResolveProjectReferences")]
    [InlineData("ResolveTailwindWatchConfiguration", null)]
    [InlineData("TailwindClean", null)]
    [InlineData("RunTailwindBeforeStaticWebAssets", "ResolveProjectReferences")]
    public void DevelopmentTargets_ShouldStayInSyncWithPackagedTargets(string targetName, string? developmentOnlyPrefix)
    {
        // Arrange
        var packagedTarget = LoadTarget(PackagedTargets, targetName);
        var developmentTarget = LoadTarget(DevelopmentTargets, targetName);

        var packagedDependsOn = packagedTarget.Attribute("DependsOnTargets")?.Value;
        var expectedDevelopmentDependsOn = developmentOnlyPrefix is null
            ? packagedDependsOn
            : string.IsNullOrEmpty(packagedDependsOn)
                ? developmentOnlyPrefix
                : $"{developmentOnlyPrefix};{packagedDependsOn}";

        Assert.Equal(expectedDevelopmentDependsOn, developmentTarget.Attribute("DependsOnTargets")?.Value);

        // Normalised away so the bodies can be compared as-is; every other difference is a failure.
        developmentTarget.SetAttributeValue("DependsOnTargets", packagedDependsOn);

        // Act & Assert
        AssertSameTarget(targetName, PackagedTargets, packagedTarget, DevelopmentTargets, developmentTarget);
    }

    /// <summary>
    /// Every parameter the task declares must be passed by every copy of the targets.
    /// </summary>
    /// <remarks>
    /// Derived by reflection rather than written out, because a hand-written list is exactly as likely to be
    /// forgotten as the targets line it is meant to guard. An unwired parameter is otherwise invisible: the
    /// task falls back to its C# default, which is usually the same value the tests happen to exercise, so
    /// every behavioural test still passes and the property silently does nothing in a consumer's build.
    /// </remarks>
    // DeclaredOnly: BuildEngine and HostObject come from ITask and are set by MSBuild itself, never by a
    // targets attribute.
    [Theory]
    [InlineData(PackagedTargets)]
    [InlineData(MultiTargetingTargets)]
    [InlineData(DevelopmentTargets)]
    public void Targets_ShouldWireEveryCompileTaskInput(string targetsRelativePath)
    {
        // Arrange
        var expected = typeof(TailwindCompileTask)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(property => property.CanWrite)
            .Where(property => property.GetCustomAttributes(typeof(Microsoft.Build.Framework.OutputAttribute), inherit: true).Length == 0)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        // Act
        var invocations = LoadProject(targetsRelativePath).Descendants("TailwindCompileTask").ToArray();
        Assert.NotEmpty(invocations);

        foreach (var invocation in invocations)
        {
            var wired = invocation
                .Attributes()
                .Select(attribute => attribute.Name.LocalName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            // Assert
            var missing = expected.Except(wired, StringComparer.Ordinal).ToArray();
            Assert.True(
                missing.Length == 0,
                $"{targetsRelativePath} does not pass: {string.Join(", ", missing)}. "
                + "A task parameter with no targets attribute silently keeps its C# default in every consumer build.");

            var unknown = wired.Except(expected, StringComparer.Ordinal).ToArray();
            Assert.True(
                unknown.Length == 0,
                $"{targetsRelativePath} passes parameters the task does not declare: {string.Join(", ", unknown)}. "
                + "MSBuild fails the build on an unknown task parameter.");

            Assert.All(invocation.Attributes(), attribute => Assert.False(
                string.IsNullOrWhiteSpace(attribute.Value),
                $"{attribute.Name.LocalName} is wired to an empty value in {targetsRelativePath}."));
        }
    }

    /// <summary>
    /// Every task output must be captured: build outputs feed static web assets and Clean, while resolved
    /// watch invocations are returned to the project-aware CLI command.
    /// </summary>
    [Theory]
    [InlineData(PackagedTargets)]
    [InlineData(MultiTargetingTargets)]
    [InlineData(DevelopmentTargets)]
    public void Targets_ShouldCaptureEveryCompileTaskOutput(string targetsRelativePath)
    {
        var expected = typeof(TailwindCompileTask)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(property => property.GetCustomAttributes(typeof(Microsoft.Build.Framework.OutputAttribute), inherit: true).Length != 0)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var invocations = LoadProject(targetsRelativePath).Descendants("TailwindCompileTask").ToArray();
        Assert.NotEmpty(invocations);

        foreach (var invocation in invocations)
        {
            var captured = invocation
                .Elements("Output")
                .Select(output => output.Attribute("TaskParameter")?.Value ?? string.Empty)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected, captured);
        }
    }

    [Theory]
    [InlineData(PackagedTargets)]
    [InlineData(MultiTargetingTargets)]
    [InlineData(DevelopmentTargets)]
    public void ResolveWatchConfiguration_ShouldReturnAnEmptySuccessfulResultWhenResolutionIsDisabled(
        string targetsRelativePath)
    {
        var project = LoadProject(targetsRelativePath);
        var publicTarget = Assert.Single(
            project.Elements("Target"),
            target => target.Attribute("Name")?.Value == "ResolveTailwindWatchConfiguration");
        var helperTarget = Assert.Single(
            project.Elements("Target"),
            target => target.Attribute("Name")?.Value == "_TailwindResolveWatchConfiguration");
        var publicDependsOn = publicTarget.Attribute("DependsOnTargets")?.Value;
        var helperCondition = helperTarget.Attribute("Condition")?.Value;

        Assert.Null(publicTarget.Attribute("Condition"));
        Assert.NotNull(publicDependsOn);
        Assert.NotNull(helperCondition);
        Assert.Contains(
            "_TailwindResolveWatchConfiguration",
            publicDependsOn,
            StringComparison.Ordinal);
        Assert.Contains(
            "'$(TailwindEnabled)' == 'true'",
            helperCondition,
            StringComparison.Ordinal);
        Assert.Contains(
            "'@(TailwindBeforeStaticWebAssets)' != ''",
            helperCondition,
            StringComparison.Ordinal);
    }

    public static TheoryData<string> EveryTargetName()
    {
        var data = new TheoryData<string>();

        foreach (var name in TargetNames)
        {
            data.Add(name);
        }

        return data;
    }

    private static void AssertSameTarget(
        string targetName,
        string leftPath,
        XElement left,
        string rightPath,
        XElement right)
    {
        Assert.True(
            XNode.DeepEquals(left, right),
            $"""
             The '{targetName}' target differs between the two targets files.

             {leftPath}:
             {left}

             {rightPath}:
             {right}
             """);
    }

    private static IReadOnlyList<string> LoadTargetNames(string targetsRelativePath) =>
        LoadProject(targetsRelativePath)
            .Elements("Target")
            .Select(target => target.Attribute("Name")?.Value ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    private static XElement LoadTarget(string targetsRelativePath, string targetName) =>
        // Detached from its document so the caller can strip the expected differences before comparing.
        new(Assert.Single(
            LoadProject(targetsRelativePath).Elements("Target"),
            target => string.Equals(target.Attribute("Name")?.Value, targetName, StringComparison.Ordinal)));

    private static XElement LoadProject(string targetsRelativePath)
    {
        var targetsPath = Path.Combine(
            RepositoryRoot.Path, "src", "Scarlet.Tailwind.MSBuild", targetsRelativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(targetsPath), $"Targets file not found: {targetsPath}");

        var project = XDocument.Load(targetsPath).Root;
        Assert.NotNull(project);

        return project;
    }
}
