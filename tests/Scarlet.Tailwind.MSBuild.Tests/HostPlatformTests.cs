using System.Runtime.InteropServices;

namespace Scarlet.Tailwind.MSBuild.Tests;

/// <summary>
/// Covers the host combinations that cannot be reproduced on a development machine or in CI.
/// </summary>
/// <remarks>
/// These used to fail silently: any non-arm64 Linux mapped to <see cref="Platform.LinuxX64"/>, so a 32-bit
/// ARM or riscv64 host would download an x64 Tailwind and fail at exec with a message that says nothing about
/// architecture.
/// </remarks>
public class HostPlatformTests
{
    [Theory]
    [InlineData(Architecture.X64, false, Platform.WindowsX64)]
    [InlineData(Architecture.Arm64, false, Platform.WindowsArm64)]
    public void GetPlatform_OnWindows_ShouldMapArchitecture(Architecture architecture, bool musl, Platform expected)
    {
        // Act
        var platform = TailwindRuntimeResolver.GetPlatform(OSPlatform.Windows, architecture, musl, "Windows");

        // Assert
        Assert.Equal(expected, platform);
    }

    [Theory]
    [InlineData(Architecture.X64, Platform.LinuxX64)]
    [InlineData(Architecture.Arm64, Platform.LinuxArm64)]
    public void GetPlatform_OnGlibcLinux_ShouldMapArchitecture(Architecture architecture, Platform expected)
    {
        // Act
        var platform = TailwindRuntimeResolver.GetPlatform(OSPlatform.Linux, architecture, isMuslLibc: false, "Linux");

        // Assert
        Assert.Equal(expected, platform);
    }

    [Theory]
    [InlineData(Architecture.X64, Platform.MacOsX64)]
    [InlineData(Architecture.Arm64, Platform.MacOsArm64)]
    public void GetPlatform_OnMacOs_ShouldMapArchitecture(Architecture architecture, Platform expected)
    {
        // Act
        var platform = TailwindRuntimeResolver.GetPlatform(OSPlatform.OSX, architecture, isMuslLibc: false, "Darwin");

        // Assert
        Assert.Equal(expected, platform);
    }

    [Fact]
    public void GetPlatform_WithA32BitHostProcess_ShouldStillUseTheX64Build()
    {
        // Arrange - Tailwind ships no 32-bit build, but a 32-bit host process on a 64-bit OS (an older MSBuild,
        // say) can start the x64 binary, and always could. Rejecting x86 would break that.

        // Act
        var platform = TailwindRuntimeResolver.GetPlatform(OSPlatform.Windows, Architecture.X86, isMuslLibc: false, "Windows");

        // Assert
        Assert.Equal(Platform.WindowsX64, platform);
    }

    [Theory]
    [InlineData(Architecture.Arm)]
    [InlineData(Architecture.RiscV64)]
    [InlineData(Architecture.Ppc64le)]
    public void GetPlatform_OnAnArchitectureTailwindDoesNotPublish_ShouldSaySo(Architecture architecture)
    {
        // Act
        var exception = Assert.Throws<PlatformNotSupportedException>(() =>
            TailwindRuntimeResolver.GetPlatform(OSPlatform.Linux, architecture, isMuslLibc: false, "Linux 6.1"));

        // Assert - naming the architecture matters; the old behaviour was an exec format error
        Assert.Contains(architecture.ToString(), exception.Message);
        Assert.Contains("x64 and arm64", exception.Message);
    }

    [Theory]
    [InlineData(Architecture.X64, Platform.LinuxMuslX64)]
    [InlineData(Architecture.Arm64, Platform.LinuxMuslArm64)]
    public void GetPlatform_OnMuslLinux_ShouldMapArchitecture(Architecture architecture, Platform expected)
    {
        // Act
        var platform = TailwindRuntimeResolver.GetPlatform(OSPlatform.Linux, architecture, isMuslLibc: true, "Alpine Linux");

        // Assert
        Assert.Equal(expected, platform);
    }

    [Fact]
    public void GetPlatform_OnAnUnknownOperatingSystem_ShouldSaySo()
    {
        // Act
        var exception = Assert.Throws<PlatformNotSupportedException>(() =>
            TailwindRuntimeResolver.GetPlatform(OSPlatform.FreeBSD, Architecture.X64, isMuslLibc: false, "FreeBSD 14"));

        // Assert
        Assert.Contains("FreeBSD 14", exception.Message);
    }

    [Fact]
    public void GetCurrentPlatform_ShouldAgreeWithGetPlatformForThisHost()
    {
        // Arrange - the wiring between the two is easy to get wrong and nothing else covers it

        // Act
        var current = TailwindRuntimeResolver.GetCurrentPlatform();

        // Assert
        Assert.True(Enum.IsDefined(current));
        Assert.Equal(RuntimeInformation.ProcessArchitecture == Architecture.Arm64, current is Platform.WindowsArm64 or Platform.LinuxArm64 or Platform.LinuxMuslArm64 or Platform.MacOsArm64);
    }

    [Fact]
    public void DetectOSPlatform_WhenWindows_ShouldReturnWindows()
    {
        // Act
        var osPlatform = TailwindRuntimeResolver.DetectOSPlatform(isWindows: true, isLinux: false, isOSX: false);

        // Assert
        Assert.Equal(OSPlatform.Windows, osPlatform);
    }

    [Fact]
    public void DetectOSPlatform_WhenLinux_ShouldReturnLinux()
    {
        // Act
        var osPlatform = TailwindRuntimeResolver.DetectOSPlatform(isWindows: false, isLinux: true, isOSX: false);

        // Assert
        Assert.Equal(OSPlatform.Linux, osPlatform);
    }

    [Fact]
    public void DetectOSPlatform_WhenOSX_ShouldReturnOSX()
    {
        // Act
        var osPlatform = TailwindRuntimeResolver.DetectOSPlatform(isWindows: false, isLinux: false, isOSX: true);

        // Assert
        Assert.Equal(OSPlatform.OSX, osPlatform);
    }

    [Fact]
    public void DetectOSPlatform_WhenNoneMatch_ShouldReturnDefault()
    {
        // Arrange - this is the case no real CI host can produce: every runner is Windows, Linux, or
        // macOS, so GetCurrentPlatform() can never observe all three checks come back false.

        // Act
        var osPlatform = TailwindRuntimeResolver.DetectOSPlatform(isWindows: false, isLinux: false, isOSX: false);

        // Assert
        Assert.Equal(default, osPlatform);
    }
}
