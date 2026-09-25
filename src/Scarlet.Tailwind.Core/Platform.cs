namespace Scarlet.Tailwind.Core;

/// <summary>
/// Represents the supported platforms for Tailwind runtime.
/// </summary>
public enum Platform
{
    /// <summary>Windows x64</summary>
    WindowsX64,
    /// <summary>Linux x64</summary>
    LinuxX64,
    /// <summary>Linux ARM64</summary>
    LinuxArm64,
    /// <summary>macOS x64</summary>
    MacOsX64,
    /// <summary>macOS ARM64</summary>
    MacOsArm64,
    /// <summary>Windows ARM64</summary>
    WindowsArm64,
    /// <summary>Linux x64, musl libc (e.g. Alpine)</summary>
    LinuxMuslX64,
    /// <summary>Linux ARM64, musl libc (e.g. Alpine)</summary>
    LinuxMuslArm64
}
