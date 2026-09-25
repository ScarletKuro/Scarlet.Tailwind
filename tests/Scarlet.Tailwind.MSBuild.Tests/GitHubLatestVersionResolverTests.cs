using System.Net;

namespace Scarlet.Tailwind.MSBuild.Tests;

public class GitHubLatestVersionResolverTests
{
    private const string LatestUrl = "https://github.com/tailwindlabs/tailwindcss/releases/latest/download/tailwindcss-linux-x64";

    [Fact]
    public async Task TryResolveVersionAsync_WithGitHubRedirectShape_ShouldParseVersion()
    {
        // The exact shape GitHub returns for the first hop of a release-asset redirect: a 302 whose
        // Location carries the tag. The second hop (a signed release-assets.githubusercontent.com URL)
        // never contains the version, which is why this resolver must not follow redirects itself.
        var handler = new StaticRedirectHandler(
            HttpStatusCode.Found,
            "https://github.com/tailwindlabs/tailwindcss/releases/download/v4.3.3/tailwindcss-linux-x64");
        var resolver = new GitHubLatestVersionResolver(handler);

        var result = await resolver.TryResolveVersionAsync(LatestUrl);

        Assert.Equal("4.3.3", result);
    }

    [Fact]
    public async Task TryResolveVersionAsync_WhenResponseIsNotARedirect_ShouldReturnNull()
    {
        var handler = new StaticStatusHandler(HttpStatusCode.OK);
        var resolver = new GitHubLatestVersionResolver(handler);

        var result = await resolver.TryResolveVersionAsync(LatestUrl);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryResolveVersionAsync_WhenRedirectHasNoLocation_ShouldReturnNull()
    {
        var handler = new StaticStatusHandler(HttpStatusCode.Found);
        var resolver = new GitHubLatestVersionResolver(handler);

        var result = await resolver.TryResolveVersionAsync(LatestUrl);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("https://github.com/tailwindlabs/tailwindcss/releases/download/v4.3.3/tailwindcss-windows-x64.exe", "4.3.3")]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/357728969/46532abe?sp=r&sig=abc", null)]
    [InlineData(null, null)]
    public void TryParseVersionFromUri_WithVariousUris_ShouldParseOrReturnNull(string? uri, string? expected)
    {
        var parsed = uri is null ? null : new Uri(uri);
        Assert.Equal(expected, GitHubLatestVersionResolver.TryParseVersionFromUri(parsed));
    }

    /// <summary>Returns a fixed status code with no Location header.</summary>
    private sealed class StaticStatusHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public StaticStatusHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    /// <summary>Returns a fixed redirect status with a Location header.</summary>
    private sealed class StaticRedirectHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _location;

        public StaticRedirectHandler(HttpStatusCode statusCode, string location)
        {
            _statusCode = statusCode;
            _location = location;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode);
            response.Headers.Location = new Uri(_location);
            return Task.FromResult(response);
        }
    }
}
