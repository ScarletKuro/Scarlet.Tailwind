namespace Scarlet.Tailwind.Cli;

/// <summary>
/// A request to run Tailwind.
/// </summary>
/// <param name="ExecutablePath">The Tailwind executable to start.</param>
/// <param name="Arguments">The arguments, forwarded verbatim and in order.</param>
internal readonly record struct TailwindLaunchRequest(string ExecutablePath, IReadOnlyList<string> Arguments);