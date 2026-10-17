#!/usr/bin/env python3
"""Build a minimal compiled AXAML view at two paths without binary changes."""

import hashlib
import json
import os
from pathlib import Path
import subprocess
import tempfile


REPOSITORY = Path(__file__).resolve().parent.parent
PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
    <DeterministicSourcePaths>true</DeterministicSourcePaths>
    <PathMap>$(MSBuildProjectDirectory)/=/_/</PathMap>
    <DebugType>portable</DebugType>
    <AvaloniaXamlCreateSourceInfo>false</AvaloniaXamlCreateSourceInfo>
    <AvaloniaNameGeneratorAttachDevTools>false</AvaloniaNameGeneratorAttachDevTools>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>obj/generated</CompilerGeneratedFilesOutputPath>
    <SourceLink>$(MSBuildProjectDirectory)/sourcelink.json</SourceLink>
  </PropertyGroup>
  <ItemGroup>
    <SourceRoot Include="$(MSBuildProjectDirectory)/" SourceControl="git"
                SourceControlUrl="https://github.com/example/repro"
                RevisionId="0000000000000000000000000000000000000000" />
    <PackageReference Include="Avalonia" Version="12.1.4" />
  </ItemGroup>
</Project>
"""
VIEW = """<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="Repro.View">
  <TextBlock Text="Reproduction" />
</UserControl>
"""
CODE = """namespace Repro;
public partial class View : Avalonia.Controls.UserControl
{
    public View() => InitializeComponent();
}
"""
INSPECTOR = """using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

foreach (string assembly in args)
{
    Console.WriteLine(assembly);
    using FileStream dll = File.OpenRead(assembly);
    using PEReader pe = new(dll);
    foreach (DebugDirectoryEntry entry in pe.ReadDebugDirectory())
    {
        if (entry.Type == DebugDirectoryEntryType.CodeView)
            Console.WriteLine("CodeView: " + pe.ReadCodeViewDebugDirectoryData(entry).Path);
    }
    using FileStream pdb = File.OpenRead(Path.ChangeExtension(assembly, ".pdb"));
    using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbStream(pdb);
    MetadataReader metadata = provider.GetMetadataReader();
    foreach (DocumentHandle handle in metadata.Documents)
        Console.WriteLine("Document: " + metadata.GetString(metadata.GetDocument(handle).Name));
}
"""


def run(*arguments, cwd):
    subprocess.run(arguments, cwd=cwd, check=True,
                   env=os.environ | {"AvaloniaTelemetryOptOut": "1"})


def main():
    evidence = REPOSITORY / "artifacts" / "avalonia-pathmap"
    evidence.mkdir(parents=True, exist_ok=True)
    workspace = Path(tempfile.mkdtemp(prefix="repro-", dir=evidence))
    # Prevent repository-wide imports from adding unrelated settings or dependencies.
    (workspace / "Directory.Build.props").write_text("<Project />\n", encoding="utf-8")
    (workspace / "Directory.Build.targets").write_text("<Project />\n", encoding="utf-8")
    (workspace / "Directory.Packages.props").write_text("<Project />\n", encoding="utf-8")
    hashes = {}
    assemblies = []
    for name in ("a", "longer-build-root"):
        source = workspace / name
        source.mkdir()
        (source / "Repro.csproj").write_text(PROJECT, encoding="utf-8")
        (source / "View.axaml").write_text(VIEW, encoding="utf-8")
        (source / "View.cs").write_text(CODE, encoding="utf-8")
        (source / "sourcelink.json").write_text(json.dumps({
            "documents": {"/_/*": "https://example.invalid/repro/*"}
        }), encoding="utf-8")
        run("dotnet", "build", "Repro.csproj", "-c", "Release",
            "--disable-build-servers", cwd=source)
        output = source / "bin" / "Release" / "net10.0"
        assemblies.append(str(output / "Repro.dll"))
        hashes[name] = {
            suffix: hashlib.sha256((output / ("Repro." + suffix)).read_bytes()).hexdigest()
            for suffix in ("dll", "pdb")
        }

    inspector = workspace / "inspector"
    inspector.mkdir()
    (inspector / "Inspector.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        '<TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType>'
        '</PropertyGroup></Project>\n', encoding="utf-8")
    (inspector / "Program.cs").write_text(INSPECTOR, encoding="utf-8")
    run("dotnet", "run", "--project", "Inspector.csproj", "--", *assemblies, cwd=inspector)
    (workspace / "hashes.json").write_text(json.dumps(hashes, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(hashes, indent=2), flush=True)
    print(f"Evidence: {workspace}", flush=True)
    if hashes["a"] == hashes["longer-build-root"]:
        raise SystemExit("The outputs match. The path limitation did not reproduce.")
    print("REPRODUCED: Compiled AXAML outputs differ across build paths.", flush=True)


if __name__ == "__main__":
    main()
