namespace Scarlet.Tailwind.Core.Providers;

/// <summary>
/// No-op implementation of <see cref="IChmodProvider"/>.
/// </summary>
/// <remarks>
/// This provider is used on platforms where executable permissions are
/// either not applicable or are managed by the operating system (e.g. Windows).
/// </remarks>
internal sealed class NoOpChmodProvider : IChmodProvider
{
    /// <inheritdoc />
    public void EnsureExecutablePermissions(string filePath)
    {
        // Intentionally does nothing
    }

    /// <summary>
    /// Gets the singleton instance of <see cref="NoOpChmodProvider"/>.
    /// </summary>
    public static NoOpChmodProvider Instance { get; } = new();
}
