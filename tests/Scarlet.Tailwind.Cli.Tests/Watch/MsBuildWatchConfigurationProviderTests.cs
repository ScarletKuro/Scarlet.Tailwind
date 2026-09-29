using System.Text;
using System.Text.Json;
using Scarlet.Tailwind.Cli.Watch;

namespace Scarlet.Tailwind.Cli.Tests.Watch;

public class MsBuildWatchConfigurationProviderTests
{
    [Fact]
    public void ParseTargetResult_WithAnEmptySuccessfulTarget_ShouldReturnNoInvocations()
    {
        const string json =
            """
            {
              "TargetResults": {
                "ResolveTailwindWatchConfiguration": {
                  "Result": "Success"
                }
              }
            }
            """;

        var result = MsBuildWatchConfigurationProvider.ParseTargetResult(json, "App.csproj");

        Assert.Empty(result);
    }

    [Fact]
    public void ParseTargetResult_ShouldDecodeArgumentsAndAppendWatchMode()
    {
        var arguments = new[]
        {
            "--map",
            "--cwd=C:\\repo with spaces",
            "--input=C:\\repo with spaces\\Styles\\app.css",
            "--output=C:\\repo with spaces\\wwwroot\\css\\app.css",
            string.Empty
        };
        var encoded = string.Join(
            ";",
            arguments.Select(argument => Convert.ToBase64String(Encoding.UTF8.GetBytes(argument))));
        var json = JsonSerializer.Serialize(new
        {
            TargetResults = new Dictionary<string, object>
            {
                ["ResolveTailwindWatchConfiguration"] = new
                {
                    Result = "Success",
                    Items = new[]
                    {
                        new Dictionary<string, string>
                        {
                            ["Identity"] = "Styles/app.css",
                            ["ProtocolVersion"] = "1",
                            ["ExecutablePath"] = "C:\\tools\\tailwindcss.exe",
                            ["WorkingDirectory"] = "C:\\repo with spaces",
                            ["InputPath"] = "C:\\repo with spaces\\Styles\\app.css",
                            ["OutputPath"] = "C:\\repo with spaces\\wwwroot\\css\\app.css",
                            ["ArgumentsBase64"] = encoded,
                            ["GeneratedPathsBase64"] = Convert.ToBase64String(
                                Encoding.UTF8.GetBytes("C:\\repo with spaces\\wwwroot\\css\\app.css"))
                        }
                    }
                }
            }
        });

        var result = MsBuildWatchConfigurationProvider.ParseTargetResult(json, "C:\\repo\\App.csproj");

        var invocation = Assert.Single(result);
        Assert.Equal("C:\\tools\\tailwindcss.exe", invocation.Request.ExecutablePath);
        Assert.Equal("C:\\repo with spaces", invocation.WorkingDirectory);
        Assert.Equal([.. arguments, "--watch=always"], invocation.Request.Arguments);
        Assert.Equal(["C:\\repo with spaces\\wwwroot\\css\\app.css"], invocation.GeneratedPaths);
    }

    [Fact]
    public void ParseTargetResult_WithAnUnknownProtocol_ShouldFailClearly()
    {
        const string json =
            """
            {
              "TargetResults": {
                "ResolveTailwindWatchConfiguration": {
                  "Result": "Success",
                  "Items": [
                    {
                      "ProtocolVersion": "99",
                      "ExecutablePath": "tailwindcss",
                      "WorkingDirectory": "/repo",
                      "InputPath": "app.css",
                      "OutputPath": "out.css",
                      "ArgumentsBase64": "YXBwLmNzcw==",
                      "GeneratedPathsBase64": "b3V0LmNzcw=="
                    }
                  ]
                }
              }
            }
            """;

        var exception = Assert.Throws<TailwindWatchException>(
            () => MsBuildWatchConfigurationProvider.ParseTargetResult(json, "App.csproj"));

        Assert.Contains("protocol version '99'", exception.Message);
    }

    [Fact]
    public void ParseTargetResult_WithOlderVersionOneMetadata_ShouldUseSafeFallbacks()
    {
        const string json =
            """
            {
              "TargetResults": {
                "ResolveTailwindWatchConfiguration": {
                  "Result": "Success",
                  "Items": [
                    {
                      "ProtocolVersion": "1",
                      "ExecutablePath": "tailwindcss",
                      "InputPath": "Styles/app.css",
                      "OutputPath": "wwwroot/css/app.css",
                      "ArgumentsBase64": "LS1pbnB1dA=="
                    }
                  ]
                }
              }
            }
            """;

        var projectPath = Path.Combine(Path.GetTempPath(), "repo", "App.csproj");
        var invocation = Assert.Single(
            MsBuildWatchConfigurationProvider.ParseTargetResult(json, projectPath));

        Assert.Equal(Path.GetDirectoryName(projectPath), invocation.WorkingDirectory);
        Assert.Equal(["wwwroot/css/app.css"], invocation.GeneratedPaths);
    }
}
