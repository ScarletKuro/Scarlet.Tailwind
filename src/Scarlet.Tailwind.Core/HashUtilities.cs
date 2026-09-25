using System;
using System.Security.Cryptography;
using System.Text;

namespace Scarlet.Tailwind.Core;

/// <summary>
/// Shared hashing helper, so callers that need a filesystem- or mutex-name-safe digest of some text
/// don't each hand-roll the same SHA-256-to-hex-string conversion.
/// </summary>
public static class HashUtilities
{
    /// <summary>
    /// Computes the SHA-256 hash of <paramref name="value"/> (UTF-8 encoded) as a lowercase hex string
    /// with no separators.
    /// </summary>
    public static string ComputeSha256Hex(string value)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }
}
