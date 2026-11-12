namespace Scarlet.Tailwind.MSBuild.Tests;

/// <summary>
/// Windows ARM64 runs Tailwind's x64 binary under emulation, where it intermittently dies with an access violation.
/// The task runs it again in exactly that case and in no other, so a real failure still fails the build at once.
/// </summary>
public class TailwindEmulationCrashTests
{
    [Fact]
    public void AccessViolationExitCode_IsTheOneWindowsReports()
    {
        // What CI logged: "Tailwind command failed with exit code -1073741819".
        Assert.Equal(-1073741819, TailwindCompileTask.AccessViolationExitCode);
    }

    [Fact]
    public void AccessViolation_OnWindowsArm64_IsRetried()
    {
        Assert.True(TailwindCompileTask.IsEmulationCrash(TailwindCompileTask.AccessViolationExitCode, Platform.WindowsArm64));
    }

    [Theory]
    [InlineData(Platform.WindowsX64)]
    [InlineData(Platform.LinuxX64)]
    [InlineData(Platform.LinuxArm64)]
    [InlineData(Platform.MacOsX64)]
    [InlineData(Platform.MacOsArm64)]
    [InlineData(Platform.LinuxMuslX64)]
    [InlineData(Platform.LinuxMuslArm64)]
    public void AccessViolation_OnANativePlatform_IsNotRetried(Platform platform)
    {
        // Natively the binary never crashed like this; if it ever does, that is a real failure to report.
        Assert.False(TailwindCompileTask.IsEmulationCrash(TailwindCompileTask.AccessViolationExitCode, platform));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(3)]
    public void AnyOtherFailure_OnWindowsArm64_IsNotRetried(int exitCode)
    {
        // A stylesheet error exits 1; retrying it would only print the same error twice.
        Assert.False(TailwindCompileTask.IsEmulationCrash(exitCode, Platform.WindowsArm64));
    }
}
