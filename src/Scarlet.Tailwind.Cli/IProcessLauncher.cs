namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Runs Tailwind and returns its exit code.
/// </summary>
/// <remarks>
/// The seam that keeps everything else unit-testable: tests substitute a recorder and assert the exact
/// argument list that would have been handed to Tailwind, without starting a process.
/// </remarks>
internal interface IProcessLauncher
{
    /// <summary>
    /// Runs the request to completion.
    /// </summary>
    /// <param name="request">What to run.</param>
    /// <returns>Tailwind's exit code.</returns>
    int Run(TailwindLaunchRequest request);
}
