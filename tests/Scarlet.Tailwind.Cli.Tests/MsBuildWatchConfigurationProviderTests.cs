using System.Text;
using System.Text.Json;

namespace Scarlet.Tailwind.Cli.Tests;

public class MsBuildWatchConfigurationProviderTests
{
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
                            ["InputPath"] = "C:\\repo with spaces\\Styles\\app.css",
                            ["OutputPath"] = "C:\\repo with spaces\\wwwroot\\css\\app.css",
                            ["ArgumentsBase64"] = encoded
                        }
                    }
                }
            }
        });

        var result = MsBuildWatchConfigurationProvider.ParseTargetResult(json, "C:\\repo\\App.csproj");

        var invocation = Assert.Single(result);
        Assert.Equal("C:\\tools\\tailwindcss.exe", invocation.Request.ExecutablePath);
        Assert.Equal([.. arguments, "--watch=always"], invocation.Request.Arguments);
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
                      "InputPath": "app.css",
                      "OutputPath": "out.css",
                      "ArgumentsBase64": "YXBwLmNzcw=="
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
}
