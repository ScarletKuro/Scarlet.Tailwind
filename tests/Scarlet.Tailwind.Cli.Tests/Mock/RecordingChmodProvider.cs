namespace Scarlet.Tailwind.Cli.Tests.Mock;

/// <summary>
/// Records the paths made executable.
/// </summary>
/// <remarks>
/// Worth asserting rather than assuming: NuGet packages carry no Unix permission bits, so an embedded Tailwind
/// arrives 0644 and the chmod is the only thing standing between a Linux user and EACCES on first run.
/// </remarks>
internal sealed class RecordingChmodProvider : IChmodProvider
{
    private readonly List<string> _paths = [];

    public IReadOnlyList<string> Paths => _paths;

    public void EnsureExecutablePermissions(string filePath) => _paths.Add(filePath);
}
