using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Scarlet.Tailwind.MSBuild.IntegrationTests;

/// <summary>
/// Verifies <see cref="GitHubLatestVersionResolver"/> against the real GitHub redirect chain. The
/// resolver's unit tests (in Scarlet.Tailwind.MSBuild.Tests) fake this response shape; this test exists
/// specifically to confirm the fake still matches reality.
/// </summary>
public class GitHubLatestVersionResolverIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public GitHubLatestVersionResolverIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task TryResolveVersionAsync_AgainstRealGitHub_ShouldResolveAConcreteVersion()
    {
        var resolver = new GitHubLatestVersionResolver();

        var resolvedVersion = await resolver.TryResolveVersionAsync("https://github.com/tailwindlabs/tailwindcss/releases/latest/download/sha256sums.txt");

        _output.WriteLine($"Resolved to version: {resolvedVersion}");

        Assert.NotNull(resolvedVersion);
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+"), resolvedVersion);
    }
}
