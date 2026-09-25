using System.Threading;
using System.Threading.Tasks;

namespace Scarlet.Tailwind.Core.Providers;

/// <summary>
/// Resolves the concrete version behind a "latest" release download URL without downloading the release
/// asset itself. Abstracted so it can be replaced in tests.
/// </summary>
public interface ILatestVersionResolver
{
    /// <summary>
    /// Attempts to resolve the concrete version (e.g. "1.4.2") that <paramref name="latestDownloadUrl"/>
    /// currently points to.
    /// </summary>
    /// <returns>The resolved version, or <see langword="null"/> if it could not be determined.</returns>
    Task<string?> TryResolveVersionAsync(string latestDownloadUrl, CancellationToken cancellationToken = default);
}
