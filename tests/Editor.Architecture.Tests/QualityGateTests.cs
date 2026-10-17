using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace Editor.Architecture.Tests;

/// <summary>Exercises compiler, analyzer, and formatter gates with isolated source files.</summary>
public sealed class QualityGateTests
{
    /// <summary>Valid code passes, and representative violations fail in both configurations.</summary>
    /// <param name="configuration">The build configuration to check.</param>
    /// <returns>A task that completes after the gate checks.</returns>
    [Theory]
    [InlineData("Debug")]
    [InlineData("Release")]
    public async Task DiagnosticsFailVerification(string configuration)
    {
        string root = FindRepositoryRoot();
        string directory = Directory.CreateTempSubdirectory("tangopad-quality-").FullName;
        try
        {
            foreach (string file in (string[])["global.json", "Directory.Build.props", ".editorconfig"])
            {
                File.Copy(Path.Combine(root, file), Path.Combine(directory, file));
            }

            await File.WriteAllTextAsync(Path.Combine(directory, "Fixture.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />", TestContext.Current.CancellationToken).ConfigureAwait(true);
            string source = Path.Combine(directory, "Sample.cs");
            const string valid = "namespace Fixture;\n\ninternal static class Sample\n{\n    internal static string Read()\n    {\n        return string.Empty;\n    }\n}\n";
            await File.WriteAllTextAsync(source, valid, TestContext.Current.CancellationToken).ConfigureAwait(true);
            (int exitCode, string output) = await RunAsync(directory, "build", "--configuration", configuration, "--disable-build-servers").ConfigureAwait(true);
            Assert.True(exitCode == 0, output);

            (string Source, string Diagnostic)[] violations =
            [
                (valid.Replace("string.Empty", "null", StringComparison.Ordinal), "CS8603"),
                (valid.Replace("Read()", "read()", StringComparison.Ordinal), "IDE1006"),
                (valid.Replace("string.Empty", "throw new System.Exception()", StringComparison.Ordinal), "CA2201"),
                (valid.Replace("return string.Empty;", "var value = string.Empty; return value;", StringComparison.Ordinal), "IDE0008"),
            ];
            foreach ((string text, string diagnostic) in violations)
            {
                await File.WriteAllTextAsync(source, text, TestContext.Current.CancellationToken).ConfigureAwait(true);
                (exitCode, output) = await RunAsync(directory, "build", "--configuration", configuration, "--no-restore", "--disable-build-servers").ConfigureAwait(true);
                Assert.True(exitCode != 0, $"The build accepted {diagnostic}: {output}");
                Assert.Contains($"error {diagnostic}", output, StringComparison.Ordinal);
            }

            await File.WriteAllTextAsync(source, valid, TestContext.Current.CancellationToken).ConfigureAwait(true);
            (exitCode, output) = await RunAsync(directory, "format", "Fixture.csproj", "--verify-no-changes", "--no-restore", "--severity", "info").ConfigureAwait(true);
            Assert.True(exitCode == 0, output);
            string unformatted = valid.Replace("    internal", " internal", StringComparison.Ordinal);
            await File.WriteAllTextAsync(source, unformatted, TestContext.Current.CancellationToken).ConfigureAwait(true);
            (exitCode, output) = await RunAsync(directory, "format", "Fixture.csproj", "--verify-no-changes", "--no-restore", "--severity", "info").ConfigureAwait(true);
            Assert.True(exitCode != 0, output);
            Assert.Contains("WHITESPACE", output, StringComparison.Ordinal);
            Assert.Equal(unformatted, await File.ReadAllTextAsync(source, TestContext.Current.CancellationToken).ConfigureAwait(true));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Verification retains the required read-only checks and both build configurations.</summary>
    [Fact]
    public void VerificationIncludesRequiredGates()
    {
        XDocument document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "eng", "verify.proj"));
        Dictionary<string, XElement> targets = document.Root!.Elements("Target")
            .ToDictionary(target => target.Attribute("Name")!.Value, StringComparer.Ordinal);
        HashSet<string> reachable = new(StringComparer.Ordinal);
        Visit("Verify");
        foreach (string target in (string[])["Restore", "Format", "BuildDebug", "Build", "Test", "VerifyTestReports"])
        {
            Assert.Contains(target, reachable);
            Assert.Null(targets[target].Attribute("Condition"));
        }

        CheckCommand("Format", "dotnet format Tangopad.slnx --verify-no-changes --no-restore --severity info");
        CheckCommand("BuildDebug", "dotnet build Tangopad.slnx --configuration Debug --no-restore --disable-build-servers -warnaserror");
        CheckCommand("Build", "dotnet build Tangopad.slnx --configuration Release --no-restore --disable-build-servers -warnaserror");

        void Visit(string name)
        {
            if (!reachable.Add(name))
            {
                return;
            }

            Assert.Null(targets[name].Attribute("Condition"));
            foreach (string dependency in (targets[name].Attribute("DependsOnTargets")?.Value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                Visit(dependency);
            }
        }

        void CheckCommand(string target, string expected)
        {
            XElement command = Assert.Single(targets[target].Elements("Exec"));
            Assert.Equal(expected, command.Attribute("Command")?.Value);
            Assert.Null(command.Attribute("Condition"));
            Assert.Null(command.Attribute("IgnoreExitCode"));
            Assert.Null(command.Attribute("ContinueOnError"));
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string directory, params string[] arguments)
    {
        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await output.ConfigureAwait(false) + await error.ConfigureAwait(false));
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
