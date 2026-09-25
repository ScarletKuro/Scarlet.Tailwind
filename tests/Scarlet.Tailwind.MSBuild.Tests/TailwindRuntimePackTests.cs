namespace Scarlet.Tailwind.MSBuild.Tests;

public class TailwindRuntimePackTests
{
    [Fact]
    public void Deduplicate_WithUnresolvablePath_ShouldNotThrow()
    {
        // Arrange - a path the OS cannot canonicalize must still be usable as a comparison key
        var packs = new[]
        {
            new TailwindRuntimePack("a", "linux-x64", "::invalid|path\0"),
            new TailwindRuntimePack("b", "linux-x64", "::invalid|path\0"),
            new TailwindRuntimePack("c", "linux-x64", "/other/runtimes")
        };

        // Act
        var result = TailwindRuntimePack.Deduplicate(packs);

        // Assert - the two identical unresolvable paths still collapse
        Assert.Equal(2, result.Count);
        Assert.Equal(["a", "c"], result.Select(pack => pack.Id));
    }

    [Fact]
    public void Deduplicate_WithDifferentRids_ShouldKeepBoth()
    {
        // Arrange
        var packs = new[]
        {
            new TailwindRuntimePack("a", "osx-arm64", "/packs/runtimes"),
            new TailwindRuntimePack("b", "linux-x64", "/packs/runtimes")
        };

        // Act
        var result = TailwindRuntimePack.Deduplicate(packs);

        // Assert
        Assert.Equal(2, result.Count);
    }

    [Theory]
    [InlineData("", "osx-arm64", "/runtimes")]
    [InlineData("id", "", "/runtimes")]
    [InlineData("id", "osx-arm64", "")]
    public void Constructor_WithEmptyRequiredValue_ShouldThrow(string id, string rid, string runtimesPath)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new TailwindRuntimePack(id, rid, runtimesPath));
    }

    [Fact]
    public void ToString_ShouldIncludeRidAndVariant()
    {
        // Assert
        Assert.Equal(
            "Scarlet.Tailwind.Runtime.linux-x64 (linux-x64, default)",
            new TailwindRuntimePack("Scarlet.Tailwind.Runtime.linux-x64", "linux-x64", "/runtimes", "default").ToString());
        Assert.Equal(
            "Scarlet.Tailwind.Runtime.linux-arm64 (linux-arm64)",
            new TailwindRuntimePack("Scarlet.Tailwind.Runtime.linux-arm64", "linux-arm64", "/runtimes").ToString());
    }
}
