namespace Scarlet.Tailwind.Core;

/// <summary>
/// Stable capability tokens advertised by <c>dotnet tailwind --scarlet-info</c>.
/// </summary>
public static class TailwindCliCapabilities
{
    /// <summary>
    /// The CLI can resolve an MSBuild project and run its Tailwind entry points in watch mode.
    /// </summary>
    public const string ProjectWatchV1 = "project-watch-v1";
}
