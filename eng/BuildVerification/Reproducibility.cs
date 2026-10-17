using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;

namespace Tangopad.BuildVerification;

internal static class Reproducibility
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] RestoreArguments = ["--locked-mode", "--force", "--no-http-cache"];

    internal static async Task VerifyAsync(string repository)
    {
        string evidence = Path.Combine(repository, "artifacts", "reproducibility");
        if (Directory.Exists(evidence))
        {
            Directory.Delete(evidence, recursive: true);
        }

        _ = Directory.CreateDirectory(evidence);
        string workspace = Path.Combine(Path.GetTempPath(), $"tango-{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(workspace);
        try
        {
            string snapshot = Path.Combine(workspace, "snapshot");
            string files = await RunAsync(repository, null, "git", "ls-files", "--cached", "--others", "--exclude-standard", "-z").ConfigureAwait(false);
            SortedDictionary<string, string> inputs = SourceSnapshot.Copy(repository, snapshot, files.Split('\0', StringSplitOptions.RemoveEmptyEntries));

            string sdk = (await RunAsync(snapshot, null, "dotnet", "--version").ConfigureAwait(false)).Trim();
            string commit = (await RunAsync(repository, null, "git", "rev-parse", "HEAD").ConfigureAwait(false)).Trim();
            string[] builds = [Path.Combine(workspace, "a", "source"), Path.Combine(workspace, "b", "source")];
            string[] caches = [Path.Combine(workspace, "a", "cache"), Path.Combine(workspace, "b", "cache")];
            await WriteJsonAsync(Path.Combine(evidence, "inputs.json"), new
            {
                Commit = commit,
                Sdk = sdk,
                Configuration = "Release",
                Host = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,
                Sources = inputs,
                Builds = builds.Select((path, index) => new { Source = path, Cache = caches[index] }),
                RestoreArguments,
            }).ConfigureAwait(false);

            List<string> outputs = [];
            for (int index = 0; index < builds.Length; index++)
            {
                string build = builds[index];
                SortedDictionary<string, string> copied = SourceSnapshot.Copy(snapshot, build, inputs.Keys);
                if (!inputs.SequenceEqual(copied))
                {
                    throw new InvalidDataException($"Source inputs differ in build {index + 1}.");
                }

                _ = Directory.CreateDirectory(caches[index]);
                string buildSdk = (await RunAsync(build, caches[index], "dotnet", "--version").ConfigureAwait(false)).Trim();
                if (buildSdk != sdk)
                {
                    throw new InvalidDataException($"SDK differs in build {index}: {buildSdk}. Expected: {sdk}.");
                }

                Console.WriteLine($"Reproducibility build {index + 1}: {build}. NuGet cache: {caches[index]}");
                string log = await RunAsync(build, caches[index], "dotnet", "msbuild", "eng/verify.proj", "-target:Restore,Build").ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(evidence, $"build-{index + 1}.log"), log).ConfigureAwait(false);
                if (index == 0)
                {
                    foreach (XElement project in XDocument.Load(Path.Combine(build, "Tangopad.slnx")).Descendants("Project"))
                    {
                        string projectPath = project.Attribute("Path")!.Value;
                        string evaluation = await RunAsync(build, caches[index], "dotnet", "msbuild", projectPath, "-nologo", "-property:Configuration=Release", "-getProperty:TargetPath,DebugType").ConfigureAwait(false);
                        using JsonDocument properties = JsonDocument.Parse(evaluation);
                        JsonElement values = properties.RootElement.GetProperty("Properties");
                        if (values.GetProperty("DebugType").GetString() != "portable")
                        {
                            throw new InvalidDataException($"The project must produce portable PDBs: {projectPath}");
                        }

                        string assembly = values.GetProperty("TargetPath").GetString()!;
                        outputs.Add(Path.GetRelativePath(build, assembly));
                        outputs.Add(Path.GetRelativePath(build, Path.ChangeExtension(assembly, ".pdb")));
                    }
                }

                foreach (string output in outputs)
                {
                    if (output.EndsWith(".dll", StringComparison.Ordinal))
                    {
                        using FileStream stream = File.OpenRead(Path.Combine(build, output));
                        using PEReader reader = new(stream, PEStreamOptions.LeaveOpen);
                        if (!reader.HasMetadata || !reader.GetMetadataReader().IsAssembly)
                        {
                            throw new InvalidDataException($"The output is not a managed assembly: {output}");
                        }
                    }
                    else
                    {
                        using FileStream stream = File.OpenRead(Path.Combine(build, output));
                        using MetadataReaderProvider reader = MetadataReaderProvider.FromPortablePdbStream(stream, MetadataStreamOptions.LeaveOpen);
                        _ = reader.GetMetadataReader();
                    }
                }

                SortedDictionary<string, string> hashes = new(StringComparer.Ordinal);
                foreach (string output in outputs)
                {
                    hashes.Add(output.Replace('\\', '/'), BuildComparison.Hash(Path.Combine(build, output)));
                }

                await WriteJsonAsync(Path.Combine(evidence, $"hashes-{index + 1}.json"), hashes).ConfigureAwait(false);
            }

            SortedDictionary<string, string> matched = BuildComparison.Compare(builds[0], builds[1], outputs);
            Console.WriteLine($"Managed reproducibility passed: {matched.Count} DLL and PDB files have identical SHA-256 hashes. Evidence: {evidence}");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions)).ConfigureAwait(false);
    }

    private static async Task<string> RunAsync(string directory, string? cache, string command, params string[] arguments)
    {
        ProcessStartInfo startInfo = new(command)
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

        if (cache is not null)
        {
            startInfo.Environment["NUGET_PACKAGES"] = Path.Combine(cache, "packages");
            startInfo.Environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(cache, "http");
            startInfo.Environment["NUGET_PLUGINS_CACHE_PATH"] = Path.Combine(cache, "plugins");
            startInfo.Environment["NUGET_SCRATCH"] = Path.Combine(cache, "scratch");
            startInfo.Environment["NUGET_FALLBACK_PACKAGES"] = "";
        }

        using Process process = Process.Start(startInfo)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(8));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"Command timed out: {command} {string.Join(' ', arguments)}");
        }

        string log = await output.ConfigureAwait(false) + await error.ConfigureAwait(false);
        return process.ExitCode == 0
            ? log
            : throw new InvalidOperationException($"Command failed with exit code {process.ExitCode}: {command} {string.Join(' ', arguments)}{Environment.NewLine}{log}");
    }
}
