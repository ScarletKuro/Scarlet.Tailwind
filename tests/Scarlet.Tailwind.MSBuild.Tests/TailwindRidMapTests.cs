using System.Xml.Linq;

namespace Scarlet.Tailwind.MSBuild.Tests;

/// <summary>
/// Keeps the CLI's RID table in step with <see cref="TailwindRuntimeResolver"/>.
/// </summary>
/// <remarks>
/// <c>Scarlet.Tailwind.Cli.csproj</c> has to map each runtime identifier to a runtime project and an executable
/// name in MSBuild, where it cannot call into the resolver. That duplication is deliberate but silent: get
/// an entry wrong and the RID package embeds the wrong platform's Tailwind, which only surfaces for whoever
/// installs on that platform. This asserts the two agree.
/// </remarks>
public class TailwindRidMapTests
{
    private static readonly Platform[] SupportedPlatforms =
    [
        Platform.WindowsX64,
        Platform.WindowsArm64,
        Platform.LinuxX64,
        Platform.LinuxArm64,
        Platform.LinuxMuslX64,
        Platform.LinuxMuslArm64,
        Platform.MacOsX64,
        Platform.MacOsArm64
    ];

    [Fact]
    public void CliProject_ShouldMapEveryRuntimeIdentifierToTheRightRuntimeProject()
    {
        // Arrange
        var project = LoadCliProject();

        // Act & Assert
        foreach (var platform in SupportedPlatforms)
        {
            var rid = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);

            // Scarlet.Tailwind.Runtime.windows-x64 -> windows-x64
            var expectedProject = TailwindRuntimeResolver.GetRuntimePackageName(platform)
                .Replace("Scarlet.Tailwind.Runtime.", string.Empty);

            var actualProject = ConditionedValue(project, "TailwindRuntimeProject", rid);

            Assert.True(
                expectedProject == actualProject,
                $"Scarlet.Tailwind.Cli.csproj maps '{rid}' to runtime project '{actualProject}', but "
                + $"TailwindRuntimeResolver says it should be '{expectedProject}'. The RID package would embed "
                + "the wrong platform's Tailwind.");
        }
    }

    [Fact]
    public void CliProject_ShouldCoverEveryPlatformTheResolverKnows()
    {
        // Arrange - a new platform added to the resolver must also be added to the CLI's table, or its RID
        // package would silently ship without a Tailwind binary
        var project = LoadCliProject();

        var mappedRids = project.Descendants("TailwindRuntimeProject")
            .Select(element => element.Attribute("Condition")?.Value ?? string.Empty)
            .Select(ExtractRid)
            .Where(rid => rid.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Act
        var expectedRids = SupportedPlatforms.Select(TailwindRuntimeResolver.GetRuntimeIdentifier);

        // Assert
        foreach (var rid in expectedRids)
        {
            Assert.Contains(rid, mappedRids);
        }

        Assert.Equal(SupportedPlatforms.Length, mappedRids.Count);
    }

    [Fact]
    public void CliProject_ShouldDeclareEveryMappedRidAsAToolRuntimeIdentifier()
    {
        // Arrange - a RID missing from RuntimeIdentifiers produces no package for that platform at all
        var project = LoadCliProject();

        var declared = Assert.Single(project.Descendants("RuntimeIdentifiers")).Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Act & Assert
        foreach (var platform in SupportedPlatforms)
        {
            Assert.Contains(TailwindRuntimeResolver.GetRuntimeIdentifier(platform), declared);
        }

        // The portable fallback is what serves hosts the eight RIDs miss.
        Assert.Contains("any", declared);
    }

    private static XElement LoadCliProject()
    {
        var path = Path.Combine(RepositoryRoot.Path, "src", "Scarlet.Tailwind.Cli", "Scarlet.Tailwind.Cli.csproj");

        Assert.True(File.Exists(path), $"CLI project not found: {path}");

        var project = XDocument.Load(path).Root;
        Assert.NotNull(project);

        return project;
    }

    /// <summary>
    /// Reads the value of a property whose condition selects the given runtime identifier.
    /// </summary>
    private static string ConditionedValue(XElement project, string propertyName, string rid)
    {
        return project.Descendants(propertyName)
            .Where(element => ExtractRid(element.Attribute("Condition")?.Value ?? string.Empty)
                .Equals(rid, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Value.Trim())
            .FirstOrDefault() ?? string.Empty;
    }

    /// <summary>
    /// Pulls the runtime identifier out of a condition like <c>'$(RuntimeIdentifier)' == 'win-x64'</c>.
    /// </summary>
    private static string ExtractRid(string condition)
    {
        const string marker = "'$(RuntimeIdentifier)' == '";

        var start = condition.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        start += marker.Length;
        var end = condition.IndexOf('\'', start);

        return end < 0 ? string.Empty : condition[start..end];
    }
}
