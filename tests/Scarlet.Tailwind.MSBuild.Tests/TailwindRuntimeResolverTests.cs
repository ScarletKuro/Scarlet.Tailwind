using System.IO.Abstractions.TestingHelpers;

namespace Scarlet.Tailwind.MSBuild.Tests;

public class TailwindRuntimeResolverTests
{
    private static MockFileSystem FileSystemWithTailwind(string runtimesPath, Platform platform)
    {
        var fileSystem = new MockFileSystem();
        fileSystem.AddFile(TailwindRuntimeResolver.GetExecutablePath(runtimesPath, platform), new MockFileData("fake executable"));

        return fileSystem;
    }

    [Fact]
    public void ResolveTailwindExecutable_WithNoRuntimeDirectory_ShouldThrowFileNotFoundException()
    {
        var platform = TailwindRuntimeResolver.GetCurrentPlatform();

        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() =>
            TailwindRuntimeResolver.ResolveTailwindExecutable(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                platform,
                runtimeDirectory: null));
        Assert.Contains("Tailwind runtime package not found", exception.Message);
        Assert.Contains("Scarlet.Tailwind.Runtime", exception.Message);
    }

    [Fact]
    public void ResolveTailwindExecutable_WithMissingFile_ShouldThrowFileNotFoundException()
    {
        // Arrange
        var runtimeDirectory = "/runtime";
        var platform = Platform.LinuxX64;
        var mockFileSystem = new MockFileSystem();

        // Act & Assert
        var exception = Assert.Throws<FileNotFoundException>(() =>
            TailwindRuntimeResolver.ResolveTailwindExecutable(
                mockFileSystem,
                NoOpChmodProvider.Instance,
                platform,
                runtimeDirectory));

        Assert.Contains("Tailwind executable not found at", exception.Message);
        Assert.Contains("Scarlet.Tailwind.Runtime.linux-x64", exception.Message);
    }

    [Theory]
    [InlineData(Platform.WindowsX64, "win-x64", "tailwindcss.exe")]
    [InlineData(Platform.LinuxX64, "linux-x64", "tailwindcss")]
    [InlineData(Platform.LinuxArm64, "linux-arm64", "tailwindcss")]
    [InlineData(Platform.MacOsX64, "osx-x64", "tailwindcss")]
    [InlineData(Platform.MacOsArm64, "osx-arm64", "tailwindcss")]
    public void ResolveTailwindExecutable_WithValidFile_ShouldReturnPath(
        Platform platform,
        string runtimeId,
        string executableName)
    {
        // Arrange
        var runtimeDirectory = "/runtime";
        var expectedPath = Path.GetFullPath(Path.Combine(runtimeDirectory, runtimeId, "native", executableName));

        var mockFileSystem = new MockFileSystem();
        mockFileSystem.AddFile(expectedPath, new MockFileData("fake executable"));

        // Act
        var result = TailwindRuntimeResolver.ResolveTailwindExecutable(
            mockFileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory);

        // Assert
        Assert.Equal(expectedPath, result);
    }

    [Fact]
    public void ResolveTailwindExecutable_WithInvalidPath_ShouldThrowException()
    {
        var platform = TailwindRuntimeResolver.GetCurrentPlatform();

        // Act & Assert
        Assert.ThrowsAny<Exception>(() =>
            TailwindRuntimeResolver.ResolveTailwindExecutable(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                platform));
    }

    [Fact]
    public void ResolveTailwindExecutable_WithMatchingPack_ShouldReturnPathFromPack()
    {
        // Arrange
        var platform = Platform.LinuxArm64;
        var packs = new[]
        {
            new TailwindRuntimePack("Scarlet.Tailwind.Runtime.linux-x64", "linux-x64", "/packs/linux-x64/runtimes"),
            new TailwindRuntimePack("Scarlet.Tailwind.Runtime.linux-arm64", "linux-arm64", "/packs/linux-arm64/runtimes")
        };
        var fileSystem = FileSystemWithTailwind("/packs/linux-arm64/runtimes", platform);

        // Act
        var result = TailwindRuntimeResolver.ResolveTailwindExecutable(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs);

        // Assert
        Assert.Equal(TailwindRuntimeResolver.GetExecutablePath("/packs/linux-arm64/runtimes", platform), result);
    }

    [Fact]
    public void ResolveTailwindExecutable_WithExplicitDirectory_ShouldIgnorePacks()
    {
        // Arrange - an explicit TailwindRuntimeDirectory is a deliberate override
        var platform = Platform.LinuxArm64;
        var packs = new[] { new TailwindRuntimePack("pack", "linux-arm64", "/packs/linux-arm64/runtimes") };
        var fileSystem = FileSystemWithTailwind("/explicit", platform);

        // Act
        var result = TailwindRuntimeResolver.ResolveTailwindExecutable(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: "/explicit",
            runtimePacks: packs);

        // Assert
        Assert.Equal(TailwindRuntimeResolver.GetExecutablePath("/explicit", platform), result);
    }

    [Fact]
    public void ResolveTailwindExecutable_WithHigherPriorityPack_ShouldPreferIt()
    {
        // Arrange
        var platform = Platform.LinuxX64;
        var packs = new[]
        {
            new TailwindRuntimePack("Scarlet.Tailwind.Runtime.linux-x64", "linux-x64", "/packs/linux-x64/runtimes", "default"),
            new TailwindRuntimePack("Contoso.Tailwind.linux-x64", "linux-x64", "/packs/custom/runtimes", priority: 100)
        };

        var fileSystem = FileSystemWithTailwind("/packs/linux-x64/runtimes", platform);
        fileSystem.AddFile(TailwindRuntimeResolver.GetExecutablePath("/packs/custom/runtimes", platform), new MockFileData("fake executable"));

        // Act
        var result = TailwindRuntimeResolver.ResolveTailwindExecutable(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs);

        // Assert
        Assert.Equal(TailwindRuntimeResolver.GetExecutablePath("/packs/custom/runtimes", platform), result);
    }

    [Fact]
    public void ResolveTailwindExecutable_WithBestPackMissingItsBinary_ShouldFallBackToNextCandidate()
    {
        // Arrange
        var platform = Platform.LinuxX64;
        var packs = new[]
        {
            new TailwindRuntimePack("Contoso.Tailwind.linux-x64", "linux-x64", "/packs/custom/runtimes", priority: 100),
            new TailwindRuntimePack("Scarlet.Tailwind.Runtime.linux-x64", "linux-x64", "/packs/linux-x64/runtimes", "default")
        };
        var fileSystem = FileSystemWithTailwind("/packs/linux-x64/runtimes", platform);

        // Act
        var result = TailwindRuntimeResolver.ResolveTailwindExecutable(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs);

        // Assert
        Assert.Equal(TailwindRuntimeResolver.GetExecutablePath("/packs/linux-x64/runtimes", platform), result);
    }

    [Fact]
    public void ResolveTailwindExecutable_WithPackForAnotherHost_ShouldNameWhatIsInstalled()
    {
        // Arrange - the classic "wrong runtime package referenced" mistake
        var packs = new[] { new TailwindRuntimePack("Scarlet.Tailwind.Runtime.linux-x64", "linux-x64", "/packs/linux-x64/runtimes") };

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() =>
            TailwindRuntimeResolver.ResolveTailwindExecutable(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                Platform.MacOsArm64,
                runtimeDirectory: null,
                runtimePacks: packs));

        // Assert
        Assert.Contains("Tailwind runtime package not found", exception.Message);
        Assert.Contains("osx-arm64", exception.Message);
        Assert.Contains("Scarlet.Tailwind.Runtime.darwin-arm64", exception.Message);
        Assert.Contains("Scarlet.Tailwind.Runtime.linux-x64 (linux-x64)", exception.Message);
    }

    [Fact]
    public void ResolveTailwindExecutable_WithMatchingPackButNoBinary_ShouldListSearchedLocations()
    {
        // Arrange
        var packs = new[] { new TailwindRuntimePack("Scarlet.Tailwind.Runtime.darwin-arm64", "osx-arm64", "/packs/osx-arm64/runtimes") };

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() =>
            TailwindRuntimeResolver.ResolveTailwindExecutable(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                Platform.MacOsArm64,
                runtimeDirectory: null,
                runtimePacks: packs));

        // Assert
        Assert.Contains("Tailwind executable not found at", exception.Message);
        Assert.Contains("Scarlet.Tailwind.Runtime.darwin-arm64 (osx-arm64)", exception.Message);
        Assert.Contains(TailwindRuntimeResolver.GetExecutablePath("/packs/osx-arm64/runtimes", Platform.MacOsArm64), exception.Message);
    }

    [Fact]
    public void ResolveTailwindExecutable_WithMatchingPack_ShouldLogTheSelection()
    {
        // Arrange
        var platform = Platform.WindowsX64;
        var packs = new[] { new TailwindRuntimePack("Scarlet.Tailwind.Runtime.windows-x64", "win-x64", "/packs/win-x64/runtimes", "default") };
        var fileSystem = FileSystemWithTailwind("/packs/win-x64/runtimes", platform);
        var messages = new List<string>();

        // Act
        TailwindRuntimeResolver.ResolveTailwindExecutable(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs,
            log: messages.Add);

        // Assert
        Assert.Contains(messages, message => message.Contains("Scarlet.Tailwind.Runtime.windows-x64 (win-x64, default)"));
    }

    [Fact]
    public void SelectPacks_ShouldOrderByPriorityThenIdIndependentlyOfInputOrder()
    {
        // Arrange
        var low = new TailwindRuntimePack("z-pack", "linux-x64", "/z/runtimes");
        var alsoLow = new TailwindRuntimePack("a-pack", "linux-x64", "/a/runtimes");
        var high = new TailwindRuntimePack("m-pack", "linux-x64", "/m/runtimes", priority: 5);
        var other = new TailwindRuntimePack("other", "osx-arm64", "/o/runtimes", priority: 99);

        // Act
        var forward = TailwindRuntimeResolver.SelectPacks([low, alsoLow, high, other], Platform.LinuxX64);
        var reversed = TailwindRuntimeResolver.SelectPacks([other, high, alsoLow, low], Platform.LinuxX64);

        // Assert
        Assert.Equal(["m-pack", "a-pack", "z-pack"], forward.Select(pack => pack.Id));
        Assert.Equal(forward.Select(pack => pack.Id), reversed.Select(pack => pack.Id));
    }

    [Fact]
    public void SelectPacks_WithSameIdAndPriority_ShouldBreakTheTieOnPath()
    {
        // Arrange - two packs indistinguishable except for where they live
        var second = new TailwindRuntimePack("same-id", "linux-x64", "/b/runtimes");
        var first = new TailwindRuntimePack("same-id", "linux-x64", "/a/runtimes");

        // Act
        var forward = TailwindRuntimeResolver.SelectPacks([second, first], Platform.LinuxX64);
        var reversed = TailwindRuntimeResolver.SelectPacks([first, second], Platform.LinuxX64);

        // Assert
        Assert.Equal(["/a/runtimes", "/b/runtimes"], forward.Select(pack => pack.RuntimesPath));
        Assert.Equal(forward.Select(pack => pack.RuntimesPath), reversed.Select(pack => pack.RuntimesPath));
    }

    [Fact]
    public void ResolveTailwindExecutable_WithSeveralCandidates_ShouldLogHowManyWereConsidered()
    {
        // Arrange
        var platform = Platform.LinuxX64;
        var packs = new[]
        {
            new TailwindRuntimePack("Contoso.Tailwind.linux-x64", "linux-x64", "/packs/custom/runtimes", priority: 100),
            new TailwindRuntimePack("Scarlet.Tailwind.Runtime.linux-x64", "linux-x64", "/packs/linux-x64/runtimes", "default")
        };
        var fileSystem = FileSystemWithTailwind("/packs/custom/runtimes", platform);
        var messages = new List<string>();

        // Act
        TailwindRuntimeResolver.ResolveTailwindExecutable(
            fileSystem,
            NoOpChmodProvider.Instance,
            platform,
            runtimeDirectory: null,
            runtimePacks: packs,
            log: messages.Add);

        // Assert
        Assert.Contains(messages, message =>
            message.Contains("Contoso.Tailwind.linux-x64") && message.Contains("out of 2 candidates"));
    }

    [Fact]
    public void ResolveTailwindExecutable_WithSeveralCandidatesAndNoBinary_ShouldListEveryLocation()
    {
        // Arrange
        var packs = new[]
        {
            new TailwindRuntimePack("Contoso.Tailwind.linux-x64", "linux-x64", "/packs/custom/runtimes", priority: 100),
            new TailwindRuntimePack("Scarlet.Tailwind.Runtime.linux-x64", "linux-x64", "/packs/linux-x64/runtimes", "default")
        };

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() =>
            TailwindRuntimeResolver.ResolveTailwindExecutable(
                new MockFileSystem(),
                NoOpChmodProvider.Instance,
                Platform.LinuxX64,
                runtimeDirectory: null,
                runtimePacks: packs));

        // Assert
        Assert.Contains("2 runtime packs target linux-x64", exception.Message);
        Assert.Contains(TailwindRuntimeResolver.GetExecutablePath("/packs/custom/runtimes", Platform.LinuxX64), exception.Message);
        Assert.Contains(TailwindRuntimeResolver.GetExecutablePath("/packs/linux-x64/runtimes", Platform.LinuxX64), exception.Message);
    }

    [Fact]
    public void SelectPacks_WithNull_ShouldReturnEmpty()
    {
        // Act & Assert
        Assert.Empty(TailwindRuntimeResolver.SelectPacks(null, Platform.LinuxX64));
    }

    [Theory]
    [InlineData(Platform.WindowsArm64, "win-arm64", "tailwindcss.exe")]
    [InlineData(Platform.MacOsX64, "osx-x64", "tailwindcss")]
    public void GetExecutablePath_ShouldFollowTheRuntimePackLayout(Platform platform, string rid, string executableName)
    {
        // Act
        var result = TailwindRuntimeResolver.GetExecutablePath("/packs/runtimes", platform);

        // Assert
        Assert.Equal(Path.GetFullPath(Path.Combine("/packs/runtimes", rid, "native", executableName)), result);
    }
}
