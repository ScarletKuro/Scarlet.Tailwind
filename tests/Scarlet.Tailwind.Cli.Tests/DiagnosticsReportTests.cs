using System.Text.Json;
using Scarlet.Tailwind.Cli.Tests.Mock;

namespace Scarlet.Tailwind.Cli.Tests;

/// <summary>
/// Covers what <c>--scarlet-info</c> prints for each way Tailwind can be resolved.
/// </summary>
/// <remarks>
/// This is the output people paste into issues, and <c>tests/e2e/cli-tool/verify.sh</c> greps it to prove
/// nothing was downloaded, so the wording is a contract rather than decoration.
/// </remarks>
public class DiagnosticsReportTests
{
    // TailwindSource is internal, so the source is named rather than passed: an internal type cannot appear in
    // the signature of a public xunit test method.
    [Theory]
    [InlineData(nameof(TailwindSource.Embedded), "embedded")]
    [InlineData(nameof(TailwindSource.Cache), "cache")]
    [InlineData(nameof(TailwindSource.Downloaded), "downloaded")]
    [InlineData(nameof(TailwindSource.Explicit), "explicit")]
    [InlineData(nameof(TailwindSource.NotFound), "not found")]
    public void ToText_ShouldDescribeEverySource(string sourceName, string expected)
    {
        // Arrange
        var resolution = CreateResolution(Enum.Parse<TailwindSource>(sourceName));

        // Act
        var report = DiagnosticsReport.ToText(resolution, CreateOptions());

        // Assert
        Assert.Contains($"Source", report);
        Assert.Contains(expected, report);
    }

    [Fact]
    public void ToText_WhenResolved_ShouldNotOfferADownloadUrl()
    {
        // Act
        var report = DiagnosticsReport.ToText(CreateResolution(TailwindSource.Embedded), CreateOptions());

        // Assert
        Assert.Contains("(not needed)", report);
        Assert.DoesNotContain("https://github.com/tailwindlabs/tailwindcss/releases", report);
    }

    [Fact]
    public void ToText_WhenNotResolved_ShouldNameTheExactArchiveItWouldFetch()
    {
        // Arrange
        var resolution = CreateResolution(TailwindSource.NotFound, executablePath: null, version: "1.4.2");

        // Act
        var report = DiagnosticsReport.ToText(resolution, CreateOptions());

        // Assert
        Assert.Contains("https://github.com/tailwindlabs/tailwindcss/releases/download/v1.4.2/tailwindcss-linux-x64", report);
    }

    [Fact]
    public void ToText_WhenLatestIsRequested_ShouldPointAtTheLatestRelease()
    {
        // Arrange
        var resolution = CreateResolution(TailwindSource.NotFound, executablePath: null, version: TailwindCliOptions.LatestVersion);

        // Act
        var report = DiagnosticsReport.ToText(resolution, CreateOptions());

        // Assert
        Assert.Contains("releases/latest/download/tailwindcss-linux-x64", report);
    }

    [Fact]
    public void ToText_ShouldReportWhichEnvironmentVariablesAreSet()
    {
        // Arrange - the pinned version is 1.4.2, so setting it here proves the line echoes the override
        // rather than falling back to the pin
        var options = CreateOptions(new Dictionary<string, string>
        {
            [TailwindCliOptions.VersionVariable] = "1.2.3",
            [TailwindCliOptions.CacheVariable] = "/cache",
            [TailwindCliOptions.NoEmbeddedVariable] = "1",
            [TailwindCliOptions.PassthroughVariable] = "1",
            [TailwindCliOptions.DiagnosticsVariable] = "1",
            [TailwindCliOptions.DownloadTimeoutVariable] = "42"
        });

        // Act
        var report = DiagnosticsReport.ToText(CreateResolution(TailwindSource.Embedded), options);

        // Assert
        AssertReportsVariable(report, TailwindCliOptions.VersionVariable, "1.2.3");
        AssertReportsVariable(report, TailwindCliOptions.NoEmbeddedVariable, "enabled");
        AssertReportsVariable(report, TailwindCliOptions.PassthroughVariable, "enabled");
        AssertReportsVariable(report, TailwindCliOptions.DiagnosticsVariable, "enabled");
        AssertReportsVariable(report, TailwindCliOptions.CacheVariable, "/cache");
        AssertReportsVariable(report, TailwindCliOptions.DownloadTimeoutVariable, "42");
    }

    [Fact]
    public void ToText_WithNothingSet_ShouldSayUnset()
    {
        // Arrange - a genuinely empty environment, rather than CreateOptions()'s default, which pins
        // SCARLET_TAILWIND_CACHE so unrelated tests don't depend on the host's filesystem layout
        var options = TailwindCliOptions.FromEnvironment(new FakeEnvironmentProvider(), "1.4.2");

        // Act
        var report = DiagnosticsReport.ToText(CreateResolution(TailwindSource.Embedded), options);

        // Assert
        AssertReportsVariable(report, TailwindCliOptions.NoEmbeddedVariable, "(unset)");
        AssertReportsVariable(report, TailwindCliOptions.PathVariable, "(unset)");
        AssertReportsVariable(report, TailwindCliOptions.VersionVariable, "(unset)");
        AssertReportsVariable(report, TailwindCliOptions.CacheVariable, "(unset)");
        AssertReportsVariable(report, TailwindCliOptions.DownloadTimeoutVariable, "(unset)");
    }

    [Theory]
    [InlineData("", "Scarlet.Tailwind.Cli (portable)")]
    [InlineData("win-x64", "Scarlet.Tailwind.Cli.win-x64")]
    [InlineData("linux-arm64", "Scarlet.Tailwind.Cli.linux-arm64")]
    public void DescribePackage_ShouldNameThePackageThisBuildCameFrom(string rid, string expected)
    {
        // Act & Assert - the portable package reports itself differently, and only one of the two can ever
        // be reached from a test build, which is why the identifier is a parameter
        Assert.Equal(expected, DiagnosticsReport.DescribePackage(rid));
    }

    [Fact]
    public void ToJson_ShouldEmitTheSameFactsAsParseableJson()
    {
        // Arrange
        var options = CreateOptions(new Dictionary<string, string>
        {
            [TailwindCliOptions.VersionVariable] = "1.2.3",
            [TailwindCliOptions.CacheVariable] = "/cache",
            [TailwindCliOptions.NoEmbeddedVariable] = "1",
            [TailwindCliOptions.PassthroughVariable] = "1",
            [TailwindCliOptions.DiagnosticsVariable] = "1",
            [TailwindCliOptions.DownloadTimeoutVariable] = "42"
        });

        // Act
        var json = DiagnosticsReport.ToJson(CreateResolution(TailwindSource.Embedded), options);

        // Assert
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("embedded", root.GetProperty("source").GetString());
        Assert.Equal("linux-x64", root.GetProperty("runtimeIdentifier").GetString());
        Assert.Equal("/tool/tailwindcss", root.GetProperty("tailwindExecutable").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("downloadUrl").ValueKind);

        var environment = root.GetProperty("environment");
        Assert.Equal("1.2.3", environment.GetProperty(TailwindCliOptions.VersionVariable).GetString());
        Assert.Equal("/cache", environment.GetProperty(TailwindCliOptions.CacheVariable).GetString());
        Assert.True(environment.GetProperty(TailwindCliOptions.NoEmbeddedVariable).GetBoolean());
        Assert.True(environment.GetProperty(TailwindCliOptions.PassthroughVariable).GetBoolean());
        Assert.True(environment.GetProperty(TailwindCliOptions.DiagnosticsVariable).GetBoolean());
        Assert.Equal("42", environment.GetProperty(TailwindCliOptions.DownloadTimeoutVariable).GetString());
    }

    [Fact]
    public void ToJson_WhenNotResolved_ShouldCarryTheDownloadUrlAndFailureReason()
    {
        // Arrange
        var resolution = CreateResolution(TailwindSource.NotFound, executablePath: null, failureReason: "nothing yet");

        // Act
        var json = DiagnosticsReport.ToJson(resolution, CreateOptions());

        // Assert
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("tailwindExecutable").ValueKind);
        Assert.Contains("/download/v1.4.2/", root.GetProperty("downloadUrl").GetString()!);
        Assert.Equal("nothing yet", root.GetProperty("failureReason").GetString());
    }

    [Fact]
    public void ToJson_WithNothingSet_ShouldNullTheStringOverridesRatherThanTheResolvedDefault()
    {
        // Arrange - a genuinely empty environment, rather than CreateOptions()'s default, which pins
        // SCARLET_TAILWIND_CACHE so unrelated tests don't depend on the host's filesystem layout
        var options = TailwindCliOptions.FromEnvironment(new FakeEnvironmentProvider(), "1.4.2");

        // Act
        var json = DiagnosticsReport.ToJson(CreateResolution(TailwindSource.Embedded), options);

        // Assert
        using var document = JsonDocument.Parse(json);
        var environment = document.RootElement.GetProperty("environment");

        Assert.Equal(JsonValueKind.Null, environment.GetProperty(TailwindCliOptions.PathVariable).ValueKind);
        Assert.Equal(JsonValueKind.Null, environment.GetProperty(TailwindCliOptions.VersionVariable).ValueKind);
        Assert.Equal(JsonValueKind.Null, environment.GetProperty(TailwindCliOptions.CacheVariable).ValueKind);
        Assert.Equal(JsonValueKind.Null, environment.GetProperty(TailwindCliOptions.DownloadTimeoutVariable).ValueKind);
        Assert.False(environment.GetProperty(TailwindCliOptions.NoEmbeddedVariable).GetBoolean());
        Assert.False(environment.GetProperty(TailwindCliOptions.PassthroughVariable).GetBoolean());
        Assert.False(environment.GetProperty(TailwindCliOptions.DiagnosticsVariable).GetBoolean());
    }

    private static TailwindResolution CreateResolution(
        TailwindSource source,
        string? executablePath = "/tool/tailwindcss",
        string version = "1.4.2",
        string? failureReason = null)
    {
        return new TailwindResolution(
            executablePath,
            source,
            Platform.LinuxX64,
            "linux-x64",
            version,
            "/cache",
            $"/cache/runtimes/{version}",
            "/tool/tailwindcss",
            failureReason);
    }

    private static TailwindCliOptions CreateOptions(IDictionary<string, string>? variables = null)
    {
        variables ??= new Dictionary<string, string> { [TailwindCliOptions.CacheVariable] = "/cache" };

        return TailwindCliOptions.FromEnvironment(new FakeEnvironmentProvider(variables), "1.4.2");
    }

    /// <summary>
    /// Asserts that the report has a line pairing <paramref name="name"/> with <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately not an assertion about the exact column width. Baking the padding into every expected
    /// string means widening a column - which the longest environment variable name already forced once -
    /// fails a dozen tests for a cosmetic reason, and tempts whoever is fixing them to paste the new number
    /// in without looking. What actually matters is that the two appear together and are separated at all,
    /// which is exactly what a too-narrow column breaks.
    /// </remarks>
    private static void AssertReportsVariable(string report, string name, string value)
    {
        var line = Assert.Single(
            report.Split('\n').Select(candidate => candidate.TrimEnd('\r')),
            candidate => candidate.TrimStart().StartsWith(name, StringComparison.Ordinal));

        Assert.EndsWith(value, line, StringComparison.Ordinal);
        Assert.Contains(
            ' ',
            line.TrimStart()[name.Length..^value.Length]);
    }
}
