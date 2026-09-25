namespace Scarlet.Tailwind.Testing;

/// <summary>
/// Locates the repository root so tests can assert against files that are not build outputs -
/// props files, project files and Directory.Build.props.
/// </summary>
internal static class RepositoryRoot
{
    private static readonly Lazy<string> LazyPath = new(Locate);

    /// <summary>
    /// The absolute path of the directory containing the solution file.
    /// </summary>
    public static string Path => LazyPath.Value;

    /// <summary>
    /// Walks up from the test assembly until the directory holding a solution file is found.
    /// </summary>
    /// <remarks>
    /// Matches any <c>*.slnx</c> rather than a fixed name, so renaming the solution - tempting now that it
    /// holds more than the MSBuild package - does not silently break every file-system-based test.
    /// </remarks>
    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.slnx").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root above '{AppContext.BaseDirectory}'. Expected a *.slnx file in an ancestor directory.");
    }
}
