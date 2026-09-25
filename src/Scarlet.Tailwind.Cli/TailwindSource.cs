namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Where the Tailwind executable that is about to run came from.
/// </summary>
internal enum TailwindSource
{
    /// <summary>No Tailwind executable could be resolved.</summary>
    NotFound,

    /// <summary>Supplied by the user through <c>SCARLET_TAILWIND_PATH</c>.</summary>
    Explicit,

    /// <summary>Shipped inside this package. No network was involved, ever.</summary>
    Embedded,

    /// <summary>Found in the per-user download cache from an earlier run.</summary>
    Cache,

    /// <summary>Downloaded from GitHub during this run.</summary>
    Downloaded
}