using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Scarlet.Tailwind.Core;

/// <summary>
/// Renders Tailwind argument lists into the single command-line string that
/// <see cref="System.Diagnostics.ProcessStartInfo.Arguments"/> takes.
/// </summary>
/// <remarks>
/// <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/> would make all of this unnecessary, but it
/// does not exist in netstandard2.0, which this assembly targets so the MSBuild task can load it. The
/// encoding below is the one <c>CommandLineToArgvW</c> documents and the one .NET's own Unix argument
/// parser implements, so a single rendering is correct on every host.
/// </remarks>
public static class TailwindCommandLine
{
    /// <summary>
    /// Joins already-ordered argument tokens into a command line, quoting each as needed.
    /// </summary>
    public static string BuildProcessArguments(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Select(QuoteArgument));

    /// <summary>
    /// Renders an executable plus its arguments the way a person would type it, for the build log.
    /// </summary>
    public static string FormatProcessCommand(string fileName, string processArguments)
    {
        var commandText = QuoteArgument(fileName);

        return string.IsNullOrWhiteSpace(processArguments)
            ? commandText
            : $"{commandText} {processArguments}";
    }

    /// <summary>
    /// Encodes one argument so the receiving process parses back exactly the string passed in.
    /// </summary>
    /// <remarks>
    /// Backslashes are only special immediately before a quote, which is the rule a naive
    /// <c>value.Replace("\\", "\\\\")</c> gets wrong: it would turn <c>C:\dir\app.css</c> into
    /// <c>C:\\dir\\app.css</c>, since those backslashes precede letters and stay literal. Windows tolerates
    /// the doubled separators, so that mistake survives testing and then fails somewhere else. Here a run of
    /// backslashes is doubled only when a quote (or the closing quote) follows it.
    ///
    /// An argument with nothing special in it is returned unquoted, which keeps the logged command line
    /// readable.
    /// </remarks>
    public static string QuoteArgument(string value)
    {
        if (value.Length > 0 && value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');

        for (var i = 0; i < value.Length; i++)
        {
            var backslashes = 0;
            while (i < value.Length && value[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i == value.Length)
            {
                // Trailing backslashes sit right before the closing quote, so they must be doubled or that
                // quote would be escaped and the argument would swallow whatever follows it.
                builder.Append('\\', backslashes * 2);
                break;
            }

            if (value[i] == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1);
                builder.Append('"');
            }
            else
            {
                builder.Append('\\', backslashes);
                builder.Append(value[i]);
            }
        }

        builder.Append('"');

        return builder.ToString();
    }
}
