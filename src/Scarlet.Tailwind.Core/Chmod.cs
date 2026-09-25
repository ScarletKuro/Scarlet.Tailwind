using Scarlet.Tailwind.Core.Providers;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Scarlet.Tailwind.Core;

/// <summary>
/// Factory for creating the platform-correct <see cref="IChmodProvider"/>.
/// </summary>
[ExcludeFromCodeCoverage]
public static class Chmod
{
    /// <summary>
    /// Creates the platform-correct <see cref="IChmodProvider"/> for the current OS.
    /// </summary>
    /// <returns>A <see cref="NoOpChmodProvider"/> on Windows; otherwise a <see cref="UnixChmodProvider"/>.</returns>
    public static IChmodProvider CreateProvider()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return NoOpChmodProvider.Instance;
        }

        // Assume POSIX-compatible platform (Linux, macOS, Android, etc.)
        return UnixChmodProvider.Instance;
    }
}