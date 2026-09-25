namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Exit codes the tool produces itself.
/// </summary>
/// <remarks>
/// Tailwind's own exit code is always returned verbatim when Tailwind actually ran, so these values only appear when
/// the tool failed before reaching Tailwind. They follow the shell convention so that scripts already handling
/// "command not found" behave sensibly without knowing anything about this tool.
/// </remarks>
internal static class ExitCodes
{
    /// <summary>Tailwind was resolved and started successfully; the reported code came from Tailwind.</summary>
    public const int Success = 0;

    /// <summary>The command line was not understood by the tool itself.</summary>
    public const int UsageError = 64;

    /// <summary>A Tailwind executable was found but could not be started.</summary>
    public const int TailwindNotExecutable = 126;

    /// <summary>No Tailwind executable could be resolved.</summary>
    public const int TailwindNotFound = 127;
}
