using System.Xml.Linq;

namespace Scarlet.Tailwind.MSBuild.Tests;

/// <summary>
/// Checks that every runtime package's props file declares the pack the resolver expects.
/// </summary>
/// <remarks>
/// The props files are near-identical copies, and most of them describe a platform this test run can never
/// execute on. A swapped RID would otherwise only surface on somebody else's CI leg.
/// </remarks>
public class RuntimePackagePropsTests
{
    [Theory]
    [InlineData(Platform.WindowsX64)]
    [InlineData(Platform.LinuxX64)]
    [InlineData(Platform.LinuxArm64)]
    [InlineData(Platform.LinuxMuslX64)]
    [InlineData(Platform.LinuxMuslArm64)]
    [InlineData(Platform.MacOsX64)]
    [InlineData(Platform.MacOsArm64)]
    public void RuntimePackageProps_ShouldDeclareThePackTheResolverLooksFor(Platform platform)
    {
        // Arrange
        var packageId = TailwindRuntimeResolver.GetRuntimePackageName(platform);
        var expectedRid = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);
        var propsPath = Path.Combine(RepositoryRoot.Path, "src", packageId, "build", $"{packageId}.props");

        Assert.True(File.Exists(propsPath), $"Props file not found: {propsPath}");

        // Act
        var project = XDocument.Load(propsPath).Root;
        Assert.NotNull(project);

        // The pack serving this platform's own RID, which for every package here is its primary pack.
        var pack = Assert.Single(
            project.Descendants("TailwindRuntimePack"),
            candidate => candidate.Element(TailwindRuntimePack.RidMetadataName)?.Value == expectedRid);

        // Assert
        Assert.Equal(packageId, pack.Attribute("Include")?.Value);

        var runtimesPath = pack.Element(TailwindRuntimePack.RuntimesPathMetadataName)?.Value;
        Assert.NotNull(runtimesPath);
        Assert.Contains("runtimes", runtimesPath);

        Assert.False(string.IsNullOrWhiteSpace(pack.Element(TailwindRuntimePack.VariantMetadataName)?.Value));
        Assert.True(int.TryParse(pack.Element(TailwindRuntimePack.PriorityMetadataName)?.Value, out _));
    }

    /// <summary>
    /// Windows ARM64 is served by the windows-x64 package's second, lower-priority pack.
    /// </summary>
    /// <remarks>
    /// The one place a pack serves a RID it does not store under, so it is the one place
    /// <see cref="TailwindRuntimePack.NativeRidMetadataName"/> has to be right. Without it the resolver
    /// looks under <c>win-arm64/native/</c>, which this package does not ship, and a win-arm64 host gets
    /// "pack found but incomplete" instead of a working Tailwind.
    /// </remarks>
    [Fact]
    public void WindowsX64Props_ShouldAlsoServeWinArm64ThroughAnEmulatedPack()
    {
        // Arrange
        var packageId = TailwindRuntimeResolver.GetRuntimePackageName(Platform.WindowsX64);
        var propsPath = Path.Combine(RepositoryRoot.Path, "src", packageId, "build", $"{packageId}.props");

        // Act
        var project = XDocument.Load(propsPath).Root;
        Assert.NotNull(project);

        var emulated = Assert.Single(
            project.Descendants("TailwindRuntimePack"),
            candidate => candidate.Element(TailwindRuntimePack.RidMetadataName)?.Value == "win-arm64");

        // Assert
        Assert.Equal("win-x64", emulated.Element(TailwindRuntimePack.NativeRidMetadataName)?.Value);
        Assert.Equal("x64-emulated", emulated.Element(TailwindRuntimePack.VariantMetadataName)?.Value);

        // Negative priority, so a future native windows-arm64 package at the default 0 wins without any
        // code change. That is the property the whole arrangement exists to provide.
        var priority = int.Parse(emulated.Element(TailwindRuntimePack.PriorityMetadataName)!.Value);
        Assert.True(priority < 0, $"Expected the emulated pack to have a negative priority, found {priority}.");
    }

    /// <summary>
    /// Only the windows-x64 package may declare more than one pack.
    /// </summary>
    /// <remarks>
    /// Two packs for one RID from different packages fight silently, and the item contract's answer to that
    /// is <c>Priority</c>. This test is here so that a second multi-pack package is a deliberate decision
    /// with a test to update, rather than something that slips in.
    /// </remarks>
    [Fact]
    public void OnlyTheWindowsX64Package_ShouldDeclareMoreThanOnePack()
    {
        var windowsX64 = TailwindRuntimeResolver.GetRuntimePackageName(Platform.WindowsX64);

        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(RepositoryRoot.Path, "src"), "Scarlet.Tailwind.Runtime.*"))
        {
            var packageId = Path.GetFileName(directory);
            var propsPath = Path.Combine(directory, "build", $"{packageId}.props");
            var packs = XDocument.Load(propsPath).Root!.Descendants("TailwindRuntimePack").Count();

            var expected = packageId == windowsX64 ? 2 : 1;
            Assert.Equal(expected, packs);
        }
    }

    /// <summary>
    /// There is no legacy property contract, and there should never be one.
    /// </summary>
    /// <remarks>
    /// Runtime discovery is item-based from the first release. A per-RID property can represent only one
    /// path and cannot model both native and emulated candidates with deterministic priorities, so this
    /// test prevents an incompatible property-based discovery path from being introduced accidentally.
    /// </remarks>
    [Fact]
    public void RuntimePackageProps_ShouldNotDeclareAnyLegacyProperties()
    {
        foreach (var directory in Directory.EnumerateDirectories(Path.Combine(RepositoryRoot.Path, "src"), "Scarlet.Tailwind.Runtime.*"))
        {
            var packageId = Path.GetFileName(directory);
            var propsPath = Path.Combine(directory, "build", $"{packageId}.props");

            var propertyGroups = XDocument.Load(propsPath).Root!.Elements("PropertyGroup").ToArray();

            Assert.True(
                propertyGroups.Length == 0,
                $"{packageId} declares a PropertyGroup. Runtime discovery is the TailwindRuntimePack item and "
                + "nothing else; a per-RID property can only hold one path and would fight with the emulated "
                + "win-arm64 pack, last import winning, with no diagnostic.");
        }
    }

    [Fact]
    public void RuntimePackageProps_ShouldHaveNoStragglersInSrc()
    {
        // Arrange - an extra runtime package the resolver knows nothing about would go unnoticed otherwise.
        // Distinct() because WindowsArm64 and WindowsX64 deliberately share one package.
        var expected = Enum.GetValues<Platform>()
            .Select(TailwindRuntimeResolver.GetRuntimePackageName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);

        // Act
        var onDisk = Directory
            .EnumerateDirectories(Path.Combine(RepositoryRoot.Path, "src"), "Scarlet.Tailwind.Runtime.*")
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal);

        // Assert
        Assert.Equal(expected, onDisk);
    }
}
