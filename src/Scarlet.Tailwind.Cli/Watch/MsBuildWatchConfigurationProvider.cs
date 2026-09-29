using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace Scarlet.Tailwind.Cli;

/// <summary>
/// Asks the consuming project to resolve the exact Tailwind executable and arguments used by its build.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class MsBuildWatchConfigurationProvider : ITailwindWatchConfigurationProvider
{
    private const string TargetName = "ResolveTailwindWatchConfiguration";
    private const string ProtocolVersion = "1";

    public IReadOnlyList<TailwindWatchInvocation> Resolve(
        string? project,
        string configuration,
        string currentDirectory)
    {
        var projectPath = ResolveProjectPath(project, currentDirectory);
        var dotnetPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var startInfo = new ProcessStartInfo(dotnetPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!
        };

        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-verbosity:quiet");
        startInfo.ArgumentList.Add($"-getTargetResult:{TargetName}");
        startInfo.ArgumentList.Add($"-property:Configuration={configuration}");

        using var process = Process.Start(startInfo)
            ?? throw new TailwindWatchException("could not start dotnet msbuild.", ExitCodes.TailwindNotExecutable);

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        var output = standardOutput.GetAwaiter().GetResult();
        var error = standardError.GetAwaiter().GetResult().Trim();

        if (process.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(error)
                ? $"MSBuild exited with code {process.ExitCode}."
                : error;
            throw new TailwindWatchException(
                $"could not read Tailwind configuration from '{projectPath}'. {detail}");
        }

        return ParseTargetResult(output, projectPath);
    }

    internal static IReadOnlyList<TailwindWatchInvocation> ParseTargetResult(string json, string projectPath)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var target = document.RootElement
                .GetProperty("TargetResults")
                .GetProperty(TargetName);

            if (!string.Equals(target.GetProperty("Result").GetString(), "Success", StringComparison.Ordinal))
            {
                throw new TailwindWatchException(
                    $"MSBuild did not successfully resolve Tailwind configuration for '{projectPath}'.");
            }

            var result = new List<TailwindWatchInvocation>();
            if (!target.TryGetProperty("Items", out var items))
            {
                return result;
            }

            foreach (var item in items.EnumerateArray())
            {
                var protocolVersion = GetRequiredString(item, "ProtocolVersion");
                if (!string.Equals(protocolVersion, ProtocolVersion, StringComparison.Ordinal))
                {
                    throw new TailwindWatchException(
                        $"the project returned unsupported Tailwind watch protocol version '{protocolVersion}'.");
                }

                var executablePath = GetRequiredString(item, "ExecutablePath");
                var inputPath = GetRequiredString(item, "InputPath");
                var outputPath = GetRequiredString(item, "OutputPath");
                var arguments = DecodeArguments(GetRequiredString(item, "ArgumentsBase64"));
                // Both fields were added compatibly to protocol v1. Older tool/package combinations still
                // get the original behavior instead of failing configuration discovery.
                var workingDirectory = GetOptionalString(item, "WorkingDirectory")
                    ?? Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
                var generatedPaths = GetOptionalString(item, "GeneratedPathsBase64") is { } encodedPaths
                    ? DecodeArguments(encodedPaths)
                    : [outputPath];
                arguments.Add("--watch=always");

                result.Add(new TailwindWatchInvocation(
                    new TailwindLaunchRequest(executablePath, arguments),
                    workingDirectory,
                    inputPath,
                    outputPath,
                    generatedPaths));
            }

            return result;
        }
        catch (TailwindWatchException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new TailwindWatchException(
                $"MSBuild returned an invalid Tailwind watch configuration for '{projectPath}': {exception.Message}");
        }
    }

    private static string ResolveProjectPath(string? project, string currentDirectory)
    {
        var candidate = string.IsNullOrWhiteSpace(project)
            ? currentDirectory
            : Path.GetFullPath(project, currentDirectory);

        if (File.Exists(candidate))
        {
            return candidate;
        }

        if (!Directory.Exists(candidate))
        {
            throw new TailwindWatchException($"project path '{candidate}' does not exist.");
        }

        var projects = new[] { "*.csproj", "*.fsproj", "*.vbproj" }
            .SelectMany(pattern => Directory.EnumerateFiles(candidate, pattern, SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return projects.Length switch
        {
            1 => projects[0],
            0 => throw new TailwindWatchException($"no project was found in '{candidate}'. Use --project to specify one."),
            _ => throw new TailwindWatchException(
                $"multiple projects were found in '{candidate}'. Use --project to specify one: "
                + string.Join(", ", projects.Select(Path.GetFileName)))
        };
    }

    private static List<string> DecodeArguments(string value)
    {
        return value
            .Split(';')
            .Select(encoded => Encoding.UTF8.GetString(Convert.FromBase64String(encoded)))
            .ToList();
    }

    private static string GetRequiredString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new TailwindWatchException($"MSBuild omitted required watch metadata '{propertyName}'.");
        }

        return property.GetString()!;
    }

    private static string? GetOptionalString(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new TailwindWatchException($"MSBuild returned invalid watch metadata '{propertyName}'.");
        }

        return property.GetString();
    }
}
