using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Editor.Architecture.Tests;

/// <summary>Checks evaluated project dependencies against the repository policy.</summary>
public sealed class DependencyRulesTests
{
    /// <summary>Limits compiled domain and application APIs to their permitted dependencies.</summary>
    /// <param name="module">The assembly to inspect.</param>
    /// <returns>A task that completes after the compiled assembly is checked.</returns>
    [Theory]
    [InlineData("Editor.Core")]
    [InlineData("Editor.Application")]
    public async Task InnerModulesUseOnlyPermittedApis(string module)
    {
        string configuration = typeof(DependencyRulesTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        string root = FindRepositoryRoot();
        string project = $"src/{module}/{module}.csproj";
        await BuildProjectAsync(root, project, configuration).ConfigureAwait(true);
        string path = await EvaluateTargetPathAsync(root, project, configuration).ConfigureAwait(true);
        Assert.True(File.Exists(path), $"The {module} build did not produce the evaluated TargetPath: {path}");
        using FileStream stream = File.OpenRead(path);
        using PEReader assembly = new(stream);
        MetadataReader metadata = assembly.GetMetadataReader();

        string[] permittedAssemblies = module == "Editor.Core" ? ["System.Runtime"] : ["Editor.Core", "System.Runtime"];
        Assert.Equal(permittedAssemblies, metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name)).Order(StringComparer.Ordinal));

        string[] permittedNamespaces =
        [
            "System", "System.Diagnostics", "System.Reflection",
            "System.Runtime.CompilerServices", "System.Runtime.Versioning",
        ];
        if (module == "Editor.Application")
        {
            permittedNamespaces = [.. permittedNamespaces, "Editor.Core", "System.Threading", "System.Threading.Tasks"];
        }

        foreach (TypeReferenceHandle handle in metadata.TypeReferences)
        {
            TypeReference reference = metadata.GetTypeReference(handle);
            TypeReference declaringType = reference;
            while (declaringType.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                declaringType = metadata.GetTypeReference((TypeReferenceHandle)declaringType.ResolutionScope);
            }

            string typeNamespace = metadata.GetString(declaringType.Namespace);
            Assert.True(permittedNamespaces.Contains(typeNamespace, StringComparer.Ordinal),
                $"{module} cannot reference {typeNamespace}.{metadata.GetString(reference.Name)}. Review the module boundary before changing the API policy.");
        }

        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition definition = metadata.GetTypeDefinition(handle);
            string typeNamespace = metadata.GetString(definition.Namespace);
            Assert.True(typeNamespace.Length == 0 || typeNamespace == module || typeNamespace.StartsWith(module + ".", StringComparison.Ordinal),
                $"{module} cannot define types in {typeNamespace}. Keep filesystem implementations in Editor.Infrastructure.");
        }

        foreach (MethodDefinitionHandle handle in metadata.MethodDefinitions)
        {
            Assert.Equal((MethodAttributes)0, metadata.GetMethodDefinition(handle).Attributes & MethodAttributes.PinvokeImpl);
        }
    }

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
            CheckDependencies(root, project.Value, evaluation.RootElement.GetProperty("Items"));
            JsonElement properties = evaluation.RootElement.GetProperty("Properties");
            Assert.Equal("net10.0", properties.GetProperty("TargetFramework").GetString());
            foreach (string name in (string[])["TreatWarningsAsErrors", "EnforceCodeStyleInBuild", "EnableNETAnalyzers", "RunAnalyzers", "RunAnalyzersDuringBuild", "GenerateDocumentationFile"])
            {
                Assert.Equal("true", properties.GetProperty(name).GetString());
            }

            Assert.Equal("enable", properties.GetProperty("Nullable").GetString());
            Assert.Equal("14.0", properties.GetProperty("LangVersion").GetString());
            Assert.Equal("10.0-all", properties.GetProperty("AnalysisLevel").GetString());
            Assert.Equal(string.Empty, properties.GetProperty("WarningsNotAsErrors").GetString());
            Assert.Equal("1701;1702", properties.GetProperty("NoWarn").GetString());
            if (project.Name == "src/Editor.App/Editor.App.csproj")
            {
                Assert.Equal("true", properties.GetProperty("AvaloniaUseCompiledBindingsByDefault").GetString());
            }
        }
    }

    /// <summary>The dependency gate rejects evaluated references outside the policy.</summary>
    /// <param name="item">The forbidden MSBuild item.</param>
    /// <returns>A task that completes after the fixture is checked.</returns>
    [Theory]
    [InlineData("ProjectReference")]
    [InlineData("PackageReference")]
    [InlineData("Reference")]
    public async Task ForbiddenDependenciesAreRejected(string item)
    {
        string root = FindRepositoryRoot();
        string directory = Directory.CreateTempSubdirectory("tangopad-dependency-").FullName;
        try
        {
            string path = Path.Combine(directory, "Fixture.csproj");
            await File.WriteAllTextAsync(path,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><{item} Include=\"Forbidden\" /></ItemGroup></Project>",
                TestContext.Current.CancellationToken).ConfigureAwait(true);
            using JsonDocument policy = JsonDocument.Parse("{\"projects\":[],\"packages\":[]}");
            using JsonDocument evaluation = await EvaluateProjectAsync(root, path, "Release").ConfigureAwait(true);
            _ = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => CheckDependencies(root, policy.RootElement, evaluation.RootElement.GetProperty("Items")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void CheckDependencies(string root, JsonElement policy, JsonElement items)
    {
        string[] references = [.. items.GetProperty("ProjectReference").EnumerateArray()
            .Select(item => Path.GetRelativePath(root, item.GetProperty("FullPath").GetString()!).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
        Assert.Equal(ReadStrings(policy.GetProperty("projects")), references);
        Assert.Equal(ReadStrings(policy.GetProperty("packages")), ReadIdentities(items.GetProperty("PackageReference")));
        Assert.Empty(items.GetProperty("Reference").EnumerateArray());
        Assert.Equal(["Microsoft.NETCore.App"], ReadIdentities(items.GetProperty("FrameworkReference")));
    }

    private static string[] ReadStrings(JsonElement array)
    {
        return [.. array.EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal)];
    }

    private static string[] ReadIdentities(JsonElement array)
    {
        return [.. array.EnumerateArray().Select(item => item.GetProperty("Identity").GetString()!).Order(StringComparer.Ordinal)];
    }

    private static async Task BuildProjectAsync(string root, string project, string configuration)
    {
        _ = await RunDotnetAsync(
            root,
            [
                "build", project, "--nologo", "--disable-build-servers",
                $"--configuration={configuration}",
            ],
            timeoutSeconds: 120).ConfigureAwait(false);
    }

    private static async Task<string> EvaluateTargetPathAsync(string root, string project, string configuration)
    {
        string output = await RunDotnetAsync(
            root,
            [
                "msbuild", project, "-nologo", "-getProperty:TargetPath",
                $"-property:Configuration={configuration}",
            ]).ConfigureAwait(false);
        string path = output.Trim();
        Assert.True(Path.IsPathRooted(path), $"MSBuild did not return an absolute TargetPath: {path}");
        return path;
    }

    private static async Task<JsonDocument> EvaluateProjectAsync(string root, string project, string configuration)
    {
        string output = await RunDotnetAsync(
            root,
            [
                "msbuild", project, "-nologo", "-getItem:ProjectReference,PackageReference,Reference,FrameworkReference",
                "-getProperty:TargetFramework,TreatWarningsAsErrors,EnforceCodeStyleInBuild,EnableNETAnalyzers,RunAnalyzers,RunAnalyzersDuringBuild,GenerateDocumentationFile,Nullable,LangVersion,AnalysisLevel,WarningsNotAsErrors,NoWarn,AvaloniaUseCompiledBindingsByDefault", $"-property:Configuration={configuration}",
            ]).ConfigureAwait(false);
        return JsonDocument.Parse(output);
    }

    private static async Task<string> RunDotnetAsync(string root, IReadOnlyList<string> arguments, int timeoutSeconds = 60)
    {
        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = root,
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
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
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
        return await output.ConfigureAwait(false);
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
