using System.IO.Abstractions;

namespace Scarlet.Tailwind.Core.Extensions;

public static class FileExtensions
{
    /// <summary>
    /// Deletes a scratch file, ignoring any failure.
    /// </summary>
    /// <remarks>
    /// The caller runs in a finally, where a throw would replace whatever actually went wrong - a corrupt
    /// archive would surface as a delete failure - and on the success path would fail a download that had
    /// already produced a working runtime. A scanner briefly holding the file open is enough to cause it on
    /// Windows. There is no Exists check because <see cref="System.IO.File.Delete(string)"/> does not throw
    /// when the file is missing: a guard would defend against the one outcome that is harmless while doing
    /// nothing about the locked file that actually fails.
    /// </remarks>
    public static void TryDeleteFile(this IFile file, string path)
    {
        try
        {
            file.Delete(path);
        }
        catch
        {
            // Ignore cleanup errors.
        }
    }
}