namespace Scarlet.Tailwind.Testing;

/// <summary>
/// A throwaway directory outside the repository, so a spawned build cannot pick up the repository's
/// <c>Directory.Build.props</c> or leave artefacts behind in the source tree.
/// </summary>
internal sealed class TempWorkspace : IDisposable
{
    public string RootDirectory { get; }

    private TempWorkspace(string rootDirectory)
    {
        RootDirectory = rootDirectory;
    }

    public static TempWorkspace Create(string name)
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), $"scarlet-tailwind-{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootDirectory);

        return new TempWorkspace(rootDirectory);
    }

    public string PathTo(params string[] segments) =>
        Path.Combine([RootDirectory, .. segments]);

    public void WriteFile(string relativePath, string contents)
    {
        var fullPath = PathTo(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp test workspaces.
        }
    }
}
