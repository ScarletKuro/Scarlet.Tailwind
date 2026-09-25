namespace Scarlet.Tailwind.Cli;

/// <summary>
/// The ambient environment the tool reads, behind an interface.
/// </summary>
/// <remarks>
/// Nothing outside <see cref="SystemEnvironmentProvider"/> touches <see cref="Environment"/> directly. That
/// is what lets the per-OS cache locations be tested deterministically from a single CI leg instead of
/// needing a Windows, macOS and Linux runner to cover three code paths.
/// </remarks>
internal interface IEnvironmentProvider
{
    /// <summary>Reads an environment variable, or <see langword="null"/> when unset or empty.</summary>
    string? GetVariable(string name);

    /// <summary>Reads a well-known folder, or <see langword="null"/> when the platform has none.</summary>
    string? GetFolderPath(Environment.SpecialFolder folder);

    /// <summary>The current user's home directory, or <see langword="null"/> when it cannot be determined.</summary>
    string? HomeDirectory { get; }

    /// <summary>Whether the host is Windows.</summary>
    bool IsWindows { get; }

    /// <summary>Whether the host is macOS.</summary>
    bool IsMacOs { get; }

    /// <summary>The directory to fall back to when no writable user location can be determined.</summary>
    string TempDirectory { get; }
}
