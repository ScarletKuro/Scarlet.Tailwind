using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Scarlet.Tailwind.Core.Providers;

/// <summary>
/// Resolves the concrete version behind GitHub's "latest" release redirect by inspecting the first
/// redirect's Location header without following it.
/// </summary>
/// <remarks>
/// GitHub's release-asset redirect chain has two hops: the first
/// (".../releases/download/v4.3.3/tailwindcss-linux-x64") carries the version; the second - a signed,
/// time-limited "release-assets.githubusercontent.com" URL - does not. A client that follows redirects
/// automatically (as the main download client does) only ever observes the second hop, so this resolver
/// uses its own handler with automatic redirects disabled to see the first one.
/// </remarks>
public sealed class GitHubLatestVersionResolver : ILatestVersionResolver
{
    private static readonly Regex VersionFromUriRegex = new(@"/download/v(?<ver>[0-9][^/]*)/", RegexOptions.Compiled);
    private readonly HttpMessageHandler? _handler;

    public GitHubLatestVersionResolver()
    {
    }

    /// <param name="handler">
    /// Overrides the handler used for the redirect probe. Internal - production code always uses the
    /// default (a fresh, non-redirecting <see cref="HttpClientHandler"/>); tests use this to substitute a
    /// fake handler without touching the network.
    /// </param>
    internal GitHubLatestVersionResolver(HttpMessageHandler handler)
    {
        _handler = handler;
    }

    public async Task<string?> TryResolveVersionAsync(string latestDownloadUrl, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient(_handler ?? new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: _handler is null);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("User-Agent", "Scarlet.Tailwind");

        using var response = await client.GetAsync(latestDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if ((int)response.StatusCode is < 300 or > 399)
        {
            // Not a redirect - GitHub's response shape may have changed, or the URL isn't what we expect.
            return null;
        }

        return TryParseVersionFromUri(response.Headers.Location);
    }

    /// <summary>
    /// Extracts the concrete version (e.g. "1.4.2") from a redirect Location such as
    /// ".../releases/download/v4.3.3/tailwindcss-windows-x64.exe". Returns <see langword="null"/> if the URI
    /// does not contain a recognizable "/download/v&lt;version&gt;/" segment (including a null URI).
    /// </summary>
    internal static string? TryParseVersionFromUri(Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        var match = VersionFromUriRegex.Match(uri.ToString());
        return match.Success ? match.Groups["ver"].Value : null;
    }
}
