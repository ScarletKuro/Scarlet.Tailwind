using System.Diagnostics.CodeAnalysis;
using Scarlet.Tailwind.Core;

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// The outcome of resolving a Tailwind executable, including the context needed to explain it.
/// </summary>
/// <param name="ExecutablePath">The resolved executable, or <see langword="null"/> when none was found.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="Platform">The detected host platform.</param>
/// <param name="RuntimeIdentifier">The runtime identifier for <paramref name="Platform"/>.</param>
/// <param name="RequestedVersion">The version that was asked for.</param>
/// <param name="CacheRoot">The per-user cache root in effect.</param>
/// <param name="RuntimeDirectory">The version-scoped directory downloads go to.</param>
/// <param name="EmbeddedProbePath">Where an embedded binary would have been, for diagnostics.</param>
/// <param name="FailureReason">Why resolution failed, when it did.</param>
internal sealed record TailwindResolution(
    string? ExecutablePath,
    TailwindSource Source,
    Platform Platform,
    string RuntimeIdentifier,
    string RequestedVersion,
    string CacheRoot,
    string RuntimeDirectory,
    string EmbeddedProbePath,
    string? FailureReason)
{
    /// <summary>Whether a usable Tailwind executable was resolved.</summary>
    [MemberNotNullWhen(true, nameof(ExecutablePath))]
    public bool IsResolved => ExecutablePath is not null;
}
