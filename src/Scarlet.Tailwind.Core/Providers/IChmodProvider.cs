namespace Scarlet.Tailwind.Core.Providers;

/// <summary>
/// Defines an abstraction for applying executable file permissions.
/// </summary>
/// <remarks>
/// Implementations may provide platform‑specific behavior.  
/// On POSIX‑compatible systems, this typically involves setting the
/// executable permission bits on the target file.  
/// On platforms that do not support POSIX permissions (such as Windows),
/// implementations may choose to perform no action.
/// </remarks>
public interface IChmodProvider
{
    /// <summary>
    /// Ensures that the specified file has executable permissions.
    /// </summary>
    /// <param name="filePath">
    /// The path to the file whose permissions should be updated.
    /// </param>
    /// <remarks>
    /// The exact behavior depends on the underlying implementation.
    /// Implementations should preserve existing permissions whenever possible
    /// and only add the necessary executable bits.
    /// </remarks>
    void EnsureExecutablePermissions(string filePath);
}