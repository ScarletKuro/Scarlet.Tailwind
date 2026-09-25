using System.Collections;
using Microsoft.Build.Framework;
using Xunit.Abstractions;

namespace Scarlet.Tailwind.MSBuild.IntegrationTests;

/// <summary>
/// Mock implementation of IBuildEngine for testing MSBuild tasks.
/// </summary>
public class MockBuildEngine : IBuildEngine
{
    private readonly ITestOutputHelper? _output;
    private readonly List<BuildErrorEventArgs> _errors = new();
    private readonly List<BuildWarningEventArgs> _warnings = new();
    private readonly List<CustomBuildEventArgs> _customEvents = new();
    private readonly List<BuildMessageEventArgs> _messages = new();

    public MockBuildEngine(ITestOutputHelper? output = null)
    {
        _output = output;
    }

    public IReadOnlyList<BuildErrorEventArgs> Errors => _errors;
    public IReadOnlyList<BuildWarningEventArgs> Warnings => _warnings;
    public IReadOnlyList<CustomBuildEventArgs> CustomEvents => _customEvents;
    public IReadOnlyList<BuildMessageEventArgs> Messages => _messages;

    public bool ContinueOnError => false;

    public int LineNumberOfTaskNode => 0;

    public int ColumnNumberOfTaskNode => 0;

    public string ProjectFileOfTaskNode => string.Empty;

    public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs)
    {
        return true;
    }

    public void LogCustomEvent(CustomBuildEventArgs e)
    {
        _customEvents.Add(e);
        _output?.WriteLine($"[Custom] {e.Message}");
    }

    public void LogErrorEvent(BuildErrorEventArgs e)
    {
        _errors.Add(e);
        _output?.WriteLine($"[Error] {e.Message}");
    }

    public void LogMessageEvent(BuildMessageEventArgs e)
    {
        _messages.Add(e);
        _output?.WriteLine($"[Message] {e.Message}");
    }

    public void LogWarningEvent(BuildWarningEventArgs e)
    {
        _warnings.Add(e);
        _output?.WriteLine($"[Warning] {e.Message}");
    }
}
