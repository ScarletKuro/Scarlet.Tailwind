namespace Scarlet.Tailwind.Cli.Tests.Mock;

/// <summary>
/// Stands in for <see cref="GitHubLatestVersionResolver"/> so tests can dictate what "latest" resolves to
/// without touching the network.
/// </summary>
internal sealed class FakeLatestVersionResolver : ILatestVersionResolver
{
    private readonly string? _resolvedVersion;

    public FakeLatestVersionResolver(string? resolvedVersion)
    {
        _resolvedVersion = resolvedVersion;
    }

    public Task<string?> TryResolveVersionAsync(string latestDownloadUrl, CancellationToken cancellationToken = default) =>
        Task.FromResult(_resolvedVersion);
}
