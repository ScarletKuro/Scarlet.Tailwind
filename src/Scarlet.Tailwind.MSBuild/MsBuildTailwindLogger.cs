using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Scarlet.Tailwind.Core;

namespace Scarlet.Tailwind.MSBuild;

internal sealed class MsBuildTailwindLogger : ITailwindLogger
{
    private readonly TaskLoggingHelper _log;

    public MsBuildTailwindLogger(TaskLoggingHelper log) => _log = log;

    public void LogMessage(string message) =>
        _log.LogMessage(MessageImportance.High, message);
}
