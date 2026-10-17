using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Editor.Architecture.Tests;

/// <summary>Checks evaluated project dependencies against the repository policy.</summary>
public sealed class DependencyRulesTests
{
    /// <summary>Checks that the policy and solution include every repository project.</summary>
    [Fact]
    public void EveryProjectIsInThePolicyAndSolution()
    {
        string root = FindRepositoryRoot();
        using JsonDocument policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "eng", "architecture.json")));
        string[] expected = [.. policy.RootElement.EnumerateObject().Select(project => project.Name).Order(StringComparer.Ordinal)];
        string[] actual = [.. ((string[])["src", "tests", "eng"])
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(root, directory), "*.csproj", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
        string[] solution = [.. XDocument.Load(Path.Combine(root, "Tangopad.slnx")).Descendants("Project")
            .Select(project => project.Attribute("Path")!.Value).Order(StringComparer.Ordinal)];

        Assert.Equal(expected, actual);
        Assert.Equal(expected, solution);
    }

    /// <summary>Checks project, package, assembly, and framework references for each configuration.</summary>
    /// <param name="configuration">The build configuration to evaluate.</param>
    /// <returns>A task that completes after all projects are checked.</returns>
    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public async Task EvaluatedDependenciesMatchThePolicy(string configuration)
    {
        string root = FindRepositoryRoot();
        using JsonDocument policy = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "eng", "architecture.json"), TestContext.Current.CancellationToken).ConfigureAwait(true));
        foreach (JsonProperty project in policy.RootElement.EnumerateObject())
        {
            using JsonDocument evaluation = await EvaluateProjectAsync(root, project.Name, configuration).ConfigureAwait(true);
            JsonElement items = evaluation.RootElement.GetProperty("Items");
            string[] references = [.. items.GetProperty("ProjectReference").EnumerateArray()
                .Select(item => Path.GetRelativePath(root, item.GetProperty("FullPath").GetString()!).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)];

            Assert.Equal(ReadStrings(project.Value.GetProperty("projects")), references);
            Assert.Equal(ReadStrings(project.Value.GetProperty("packages")), ReadIdentities(items.GetProperty("PackageReference")));
            Assert.Empty(items.GetProperty("Reference").EnumerateArray());
            Assert.Equal(["Microsoft.NETCore.App"], ReadIdentities(items.GetProperty("FrameworkReference")));
            Assert.Equal("net10.0", evaluation.RootElement.GetProperty("Properties").GetProperty("TargetFramework").GetString());
        }
    }

    private static string[] ReadStrings(JsonElement array)
    {
        return [.. array.EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal)];
    }

    private static string[] ReadIdentities(JsonElement array)
    {
        return [.. array.EnumerateArray().Select(item => item.GetProperty("Identity").GetString()!).Order(StringComparer.Ordinal)];
    }

    private static async Task<JsonDocument> EvaluateProjectAsync(string root, string project, string configuration)
    {
        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in new[]
        {
            "msbuild", project, "-nologo", "-getItem:ProjectReference,PackageReference,Reference,FrameworkReference",
            "-getProperty:TargetFramework", $"-property:Configuration={configuration}",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        Assert.True(process.ExitCode == 0, await error.ConfigureAwait(false));
        return JsonDocument.Parse(await output.ConfigureAwait(false));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tangopad.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Cannot find Tangopad.slnx above the test assembly.");
    }
}
