using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// The real environment. The only type in the CLI that talks to <see cref="Environment"/>.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class SystemEnvironmentProvider : IEnvironmentProvider
{
    /// <inheritdoc />
    public string? GetVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <inheritdoc />
    public string? GetFolderPath(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);

        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    /// <inheritdoc />
    public string? HomeDirectory
    {
        get
        {
            var home = GetVariable(IsWindows ? "USERPROFILE" : "HOME")
                       ?? GetFolderPath(Environment.SpecialFolder.UserProfile);

            return string.IsNullOrWhiteSpace(home) ? null : home;
        }
    }

    /// <inheritdoc />
    public bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <inheritdoc />
    public bool IsMacOs => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <inheritdoc />
    public string TempDirectory => Path.GetTempPath();
}
