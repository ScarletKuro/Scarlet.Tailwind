namespace Scarlet.Tailwind.MSBuild.Tests;

public class PlatformTests
{
    [Fact]
    public void GetCurrentPlatform_ShouldReturnValidPlatform()
    {
        // Act
        var platform = TailwindRuntimeResolver.GetCurrentPlatform();

        // Assert
        Assert.True(Enum.IsDefined(platform));
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "win-x64")]
    [InlineData(Platform.WindowsArm64, "win-arm64")]
    [InlineData(Platform.LinuxX64, "linux-x64")]
    [InlineData(Platform.LinuxArm64, "linux-arm64")]
    [InlineData(Platform.LinuxMuslX64, "linux-musl-x64")]
    [InlineData(Platform.LinuxMuslArm64, "linux-musl-arm64")]
    [InlineData(Platform.MacOsX64, "osx-x64")]
    [InlineData(Platform.MacOsArm64, "osx-arm64")]
    public void GetRuntimeIdentifier_ShouldReturnCorrectIdentifier(Platform platform, string expected)
    {
        // Act
        var result = TailwindRuntimeResolver.GetRuntimeIdentifier(platform);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetRuntimeIdentifier_WithInvalidPlatform_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidPlatform = (Platform)999;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            TailwindRuntimeResolver.GetRuntimeIdentifier(invalidPlatform));
        Assert.Contains("Unknown platform", exception.Message);
        Assert.Equal("platform", exception.ParamName);
    }

    /// <summary>
    /// The release asset names, which are Tailwind's vocabulary rather than .NET's.
    /// </summary>
    /// <remarks>
    /// Worth pinning literally: these are concatenated straight into a download URL, so a value that is
    /// merely plausible - <c>osx</c> for <c>macos</c>, say, or <c>win</c> for <c>windows</c> - produces a
    /// 404 at download time rather than a compile error. WindowsArm64 deliberately names the x64 asset,
    /// because Tailwind publishes no ARM64 build for Windows.
    /// </remarks>
    [Theory]
    [InlineData(Platform.WindowsX64, "tailwindcss-windows-x64.exe")]
    [InlineData(Platform.WindowsArm64, "tailwindcss-windows-x64.exe")]
    [InlineData(Platform.LinuxX64, "tailwindcss-linux-x64")]
    [InlineData(Platform.LinuxArm64, "tailwindcss-linux-arm64")]
    [InlineData(Platform.LinuxMuslX64, "tailwindcss-linux-x64-musl")]
    [InlineData(Platform.LinuxMuslArm64, "tailwindcss-linux-arm64-musl")]
    [InlineData(Platform.MacOsX64, "tailwindcss-macos-x64")]
    [InlineData(Platform.MacOsArm64, "tailwindcss-macos-arm64")]
    public void GetDownloadName_ShouldReturnTheUpstreamAssetName(Platform platform, string expected)
    {
        // Act
        var result = TailwindRuntimeResolver.GetDownloadName(platform);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetDownloadName_WithInvalidPlatform_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidPlatform = (Platform)999;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            TailwindRuntimeResolver.GetDownloadName(invalidPlatform));
        Assert.Contains("Unknown platform", exception.Message);
        Assert.Equal("platform", exception.ParamName);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "Scarlet.Tailwind.Runtime.windows-x64")]
    [InlineData(Platform.WindowsArm64, "Scarlet.Tailwind.Runtime.windows-x64")]
    [InlineData(Platform.LinuxX64, "Scarlet.Tailwind.Runtime.linux-x64")]
    [InlineData(Platform.LinuxArm64, "Scarlet.Tailwind.Runtime.linux-arm64")]
    [InlineData(Platform.LinuxMuslX64, "Scarlet.Tailwind.Runtime.linux-x64-musl")]
    [InlineData(Platform.LinuxMuslArm64, "Scarlet.Tailwind.Runtime.linux-arm64-musl")]
    [InlineData(Platform.MacOsX64, "Scarlet.Tailwind.Runtime.darwin-x64")]
    [InlineData(Platform.MacOsArm64, "Scarlet.Tailwind.Runtime.darwin-arm64")]
    public void GetRuntimePackageName_ShouldReturnCorrectName(Platform platform, string expected)
    {
        // Act
        var result = TailwindRuntimeResolver.GetRuntimePackageName(platform);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetRuntimePackageName_WithInvalidPlatform_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidPlatform = (Platform)999;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            TailwindRuntimeResolver.GetRuntimePackageName(invalidPlatform));
        Assert.Contains("Unknown platform", exception.Message);
        Assert.Equal("platform", exception.ParamName);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "tailwindcss.exe")]
    [InlineData(Platform.WindowsArm64, "tailwindcss.exe")]
    [InlineData(Platform.LinuxX64, "tailwindcss")]
    [InlineData(Platform.LinuxArm64, "tailwindcss")]
    [InlineData(Platform.LinuxMuslX64, "tailwindcss")]
    [InlineData(Platform.LinuxMuslArm64, "tailwindcss")]
    [InlineData(Platform.MacOsX64, "tailwindcss")]
    [InlineData(Platform.MacOsArm64, "tailwindcss")]
    public void GetExecutableName_ShouldReturnCorrectName(Platform platform, string expected)
    {
        // Act
        var result = TailwindRuntimeResolver.GetExecutableName(platform);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetExecutableName_WithInvalidPlatform_ShouldThrowArgumentException()
    {
        // Arrange
        var invalidPlatform = (Platform)999;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            TailwindRuntimeResolver.GetExecutableName(invalidPlatform));
        Assert.Contains("Unknown platform", exception.Message);
        Assert.Equal("platform", exception.ParamName);
    }

    /// <summary>
    /// Every value of <see cref="Platform"/> must be mapped.
    /// </summary>
    /// <remarks>
    /// The per-platform theories above enumerate what exists today; this one fails when a value is added to
    /// the enum and not to the map, which is the mistake those theories cannot catch because nobody thinks
    /// to add the new row.
    /// </remarks>
    [Fact]
    public void EveryPlatform_ShouldBeMapped()
    {
        foreach (var platform in Enum.GetValues<Platform>())
        {
            Assert.False(string.IsNullOrWhiteSpace(TailwindRuntimeResolver.GetRuntimeIdentifier(platform)));
            Assert.False(string.IsNullOrWhiteSpace(TailwindRuntimeResolver.GetDownloadName(platform)));
            Assert.False(string.IsNullOrWhiteSpace(TailwindRuntimeResolver.GetRuntimePackageName(platform)));
            Assert.False(string.IsNullOrWhiteSpace(TailwindRuntimeResolver.GetExecutableName(platform)));
        }
    }
}
