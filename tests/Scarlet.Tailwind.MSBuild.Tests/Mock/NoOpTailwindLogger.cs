namespace Scarlet.Tailwind.MSBuild.Tests.Mock;

internal sealed class NoOpTailwindLogger : ITailwindLogger
{
    public void LogMessage(string message) { }

    public static NoOpTailwindLogger Instance { get; } = new NoOpTailwindLogger();
}
