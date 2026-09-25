using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

namespace Scarlet.Tailwind.Core.Providers;

/// <summary>
/// Unix-specific implementation of <see cref="IChmodProvider"/> that applies executable
/// permissions using <c>chmod(2)</c>.
/// </summary>
/// <remarks>
/// This implementation is intended for Unix-like operating systems.
/// </remarks>
[ExcludeFromCodeCoverage]
internal sealed class UnixChmodProvider : IChmodProvider
{
    /// <summary>
    /// Ensures that a file has executable permissions for user, group, and others.
    /// </summary>
    /// <param name="filePath">The path to the file.</param>
    /// <remarks>
    /// On Unix-like systems, this method is equivalent to running:
    /// <code>chmod +x filePath</code>.
    /// <para />
    /// Existing permissions are preserved; only execute bits are added.
    /// On Windows, this method is a no-op.
    /// </remarks>
    /// <exception cref="Win32Exception">
    /// Thrown if retrieving or updating file permissions fails.
    /// </exception>
    public void EnsureExecutablePermissions(string filePath)
    {
        // Set to 0755 (rwxr-xr-x) which is standard for executables
        // 0755 (octal) = 0x1ED (hex) = 493 (decimal)
        const int mode0755 = 0x1ED;

        if (LibsC.chmod(filePath, mode0755) != 0)
        {
            int errno = Marshal.GetLastWin32Error();
            throw new IOException(
                $"Failed to set executable permissions for {filePath} (errno: {errno})",
                new Win32Exception(errno));
        }
    }

    /// <summary>
    /// Gets the singleton instance of <see cref="UnixChmodProvider"/>.
    /// </summary>
    public static UnixChmodProvider Instance { get; } = new();

    /// <summary>
    /// Native libc interop methods.
    /// </summary>
    private static class LibsC
    {
        /// <summary>
        /// Changes the permissions of a file.
        /// </summary>
        /// <param name="path">The path to the file.</param>
        /// <param name="mode">
        /// The file mode to apply, including permission bits as defined by
        /// the POSIX <c>chmod(2)</c> specification.
        /// </param>
        /// <returns>
        /// <c>0</c> on success; <c>-1</c> on failure with <c>errno</c> set.
        /// </returns>
        [DllImport("libc", SetLastError = true)]
        public static extern int chmod(string path, int mode);
    }
}