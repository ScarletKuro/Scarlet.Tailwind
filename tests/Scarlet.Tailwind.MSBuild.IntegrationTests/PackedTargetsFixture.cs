namespace Scarlet.Tailwind.MSBuild.IntegrationTests;

/// <summary>
/// The packaged targets, packed once into a feed nothing else writes to.
/// </summary>
/// <remarks>
/// Tests cannot pack the task project's bin/ output with --no-build: the static web assets tests import the
/// development targets, whose ProjectReference rebuilds that project, and <c>dotnet clean</c> on one of their
/// workspaces cascades through it and deletes the task DLL outright. Packing whatever happens to be in bin/ at
/// that moment fails with NU5026. This fixture builds while packing, so it produces the DLL it packs, and every
/// test afterwards restores from the finished .nupkg rather than from shared build output.
/// </remarks>
public sealed class PackedTargetsFixture : IDisposable
{
    public const string Version = "0.0.0-integration-test";

    public PackedTargetsFixture()
    {
        FeedDirectory = Path.Combine(Path.GetTempPath(), $"scarlet-tailwind-feed-{Guid.NewGuid():N}");
        var taskProject = Path.Combine(RepositoryRoot.Path, "src", "Scarlet.Tailwind.MSBuild", "Scarlet.Tailwind.MSBuild.csproj");

        var result = DotnetCli.Run(
                RepositoryRoot.Path,
                $"pack \"{taskProject}\" -c {DotnetCli.Configuration} -p:PackageVersion={Version} -o \"{FeedDirectory}\"")
            .GetAwaiter()
            .GetResult();

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Packing the task project failed:{Environment.NewLine}{result.Output}");
        }
    }

    public string FeedDirectory { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(FeedDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover feed in the temp directory harms nothing.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
