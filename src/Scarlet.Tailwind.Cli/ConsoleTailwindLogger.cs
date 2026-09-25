using Scarlet.Tailwind.Core;

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Writes the downloader's progress messages to stderr.
/// </summary>
/// <remarks>
/// stderr rather than stdout on purpose: <c>dotnet tailwind ... | jq</c> has to keep working, so nothing this
/// tool says may ever appear in Tailwind's output stream.
/// </remarks>
internal sealed class ConsoleTailwindLogger : ITailwindLogger
{
    private readonly TextWriter _stderr;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConsoleTailwindLogger"/> class.
    /// </summary>
    /// <param name="stderr">The stream to write to.</param>
    public ConsoleTailwindLogger(TextWriter stderr) => _stderr = stderr;

    /// <inheritdoc />
    public void LogMessage(string message) => _stderr.WriteLine($"Scarlet.Tailwind: {message}");
}
