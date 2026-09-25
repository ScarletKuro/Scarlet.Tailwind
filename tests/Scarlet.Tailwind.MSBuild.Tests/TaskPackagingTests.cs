using System.Xml.Linq;

namespace Scarlet.Tailwind.MSBuild.Tests;

/// <summary>
/// Guards the one packaging rule that no other test in this repository can see.
/// </summary>
/// <remarks>
/// MSBuild loads the task from <c>tools/netstandard2.0/Scarlet.Tailwind.MSBuild.dll</c> and resolves that
/// assembly's dependencies from the same folder. Every assembly the task needs therefore has to be packed
/// there explicitly. The integration tests cannot catch a violation - they construct <c>TailwindCompileTask</c>
/// in-process, where the ordinary project references apply - so they stay green while every consumer build
/// fails with "Could not load file or assembly". Only the e2e scripts catch it, and they take minutes.
/// This test takes milliseconds and encodes the rule rather than today's file list, so it also covers the
/// next shared assembly somebody adds.
/// </remarks>
public class TaskPackagingTests
{
    private const string ToolsPackagePath = "tools/netstandard2.0";

    [Fact]
    public void EveryReferencedProject_ShouldBePackedIntoTheToolsFolder()
    {
        // Arrange
        var project = LoadTaskProject();
        var packed = PackedToolsAssemblies(project);

        // Act - a ProjectReference whose output is actually referenced becomes a runtime dependency
        var mustBePacked = project.Descendants("ProjectReference")
            .Where(reference => !string.Equals(
                reference.Attribute("ReferenceOutputAssembly")?.Value,
                "false",
                StringComparison.OrdinalIgnoreCase))
            .Select(reference => Path.GetFileNameWithoutExtension(
                reference.Attribute("Include")!.Value.Replace('\\', '/')) + ".dll")
            .ToList();

        // Assert
        Assert.NotEmpty(mustBePacked);

        foreach (var assembly in mustBePacked)
        {
            Assert.True(
                packed.Contains(assembly),
                $"'{assembly}' is referenced by the task but is not packed into {ToolsPackagePath}/. "
                + "MSBuild resolves task dependencies from the folder the task assembly lives in, so every "
                + $"consumer of this package would fail to load the task. Add a <None Include=\"$(OutputPath)"
                + $"\\netstandard2.0\\{assembly}\" Pack=\"true\" PackagePath=\"{ToolsPackagePath}/\" /> item.");
        }
    }

    [Fact]
    public void SharedCoreAssembly_ShouldBePackedIntoTheToolsFolder()
    {
        // Arrange - named explicitly as well, because this is the assembly the task was split into and the
        // failure it causes is remote from its cause
        var packed = PackedToolsAssemblies(LoadTaskProject());

        // Act & Assert
        Assert.Contains("Scarlet.Tailwind.Core.dll", packed);
    }

    [Fact]
    public void ThirdPartyNotice_ShouldBePackedForPackableProjects()
    {
        var noticePath = Path.Combine(RepositoryRoot.Path, "LICENSE-3RD-PARTY.txt");
        var targetsPath = Path.Combine(RepositoryRoot.Path, "Directory.Build.targets");

        Assert.True(File.Exists(noticePath), $"Third-party notice not found: {noticePath}");
        Assert.Contains("Tailwind CSS", File.ReadAllText(noticePath));

        var targets = XDocument.Load(targetsPath).Root;
        Assert.NotNull(targets);

        var noticeItem = targets.Descendants("None")
            .SingleOrDefault(none => (none.Attribute("Include")?.Value ?? string.Empty)
                .Contains("LICENSE-3RD-PARTY.txt", StringComparison.Ordinal));

        Assert.NotNull(noticeItem);
        Assert.Equal("true", noticeItem.Attribute("Pack")?.Value);
        Assert.Equal("\\", noticeItem.Attribute("PackagePath")?.Value);
    }

    private static XElement LoadTaskProject()
    {
        var projectPath = Path.Combine(
            RepositoryRoot.Path, "src", "Scarlet.Tailwind.MSBuild", "Scarlet.Tailwind.MSBuild.csproj");

        Assert.True(File.Exists(projectPath), $"Task project not found: {projectPath}");

        var project = XDocument.Load(projectPath).Root;
        Assert.NotNull(project);

        return project;
    }

    private static HashSet<string> PackedToolsAssemblies(XElement project)
    {
        return project.Descendants("None")
            .Where(none => string.Equals(
                (none.Attribute("PackagePath")?.Value ?? string.Empty).Replace('\\', '/').TrimEnd('/'),
                ToolsPackagePath,
                StringComparison.OrdinalIgnoreCase))
            .Select(none => Path.GetFileName(none.Attribute("Include")!.Value.Replace('\\', '/')))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
