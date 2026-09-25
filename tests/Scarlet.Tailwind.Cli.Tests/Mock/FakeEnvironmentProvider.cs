namespace Scarlet.Tailwind.Cli.Tests.Mock;

/// <summary>
/// A scripted environment, so the per-OS cache layouts can all be covered from one CI leg.
/// </summary>
internal sealed class FakeEnvironmentProvider : IEnvironmentProvider
{
    private readonly Dictionary<string, string> _variables;
    private readonly Dictionary<Environment.SpecialFolder, string> _folders;

    public FakeEnvironmentProvider(
        IDictionary<string, string>? variables = null,
        string? home = "/home/tester",
        bool isWindows = false,
        bool isMacOs = false,
        IDictionary<Environment.SpecialFolder, string>? folders = null,
        string tempDirectory = "/tmp")
    {
        _variables = variables is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);

        _folders = folders is null
            ? new Dictionary<Environment.SpecialFolder, string>()
            : new Dictionary<Environment.SpecialFolder, string>(folders);

        HomeDirectory = home;
        IsWindows = isWindows;
        IsMacOs = isMacOs;
        TempDirectory = tempDirectory;
    }

    public string? HomeDirectory { get; }

    public bool IsWindows { get; }

    public bool IsMacOs { get; }

    public string TempDirectory { get; }

    public string? GetVariable(string name) =>
        _variables.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public string? GetFolderPath(Environment.SpecialFolder folder) =>
        _folders.TryGetValue(folder, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
}
