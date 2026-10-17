# Compiled AXAML path mapping

PR #26 remains blocked by managed output reproducibility.
Avalonia 12.1.4 writes absolute paths after the C# compiler applies PathMap.
The custom DLL and PDB byte rewrite was removed after review.
The compiled views remain in the branch for review.
[Issue #28](https://github.com/daniel-kindl/tangopad/issues/28) tracks the build solution separately.

## Upstream limitation

The pinned [Avalonia build target](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/packages/Avalonia/AvaloniaBuildTasks.targets#L149-L191)
passes `MSBuildProjectDirectory` to `CompileAvaloniaXamlTask`.
The task has no PathMap input.
The [resource helper](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.Helpers.cs#L89-L104)
joins this absolute directory with each AXAML resource name.
The [compiler](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs#L293-L302)
uses that path as the document name.
Its [Cecil writer](https://github.com/AvaloniaUI/Avalonia/blob/12.1.4/src/Avalonia.Build.Tasks/XamlCompilerTaskExecutor.cs#L84-L96)
then writes the assembly and symbols without a path-mapping option.

The reproduction enables these build mechanisms together:

| Mechanism | Observed result on Linux |
| --- | --- |
| `Deterministic`, `ContinuousIntegrationBuild`, and `DeterministicSourcePaths` | C# document paths are mapped. AXAML document paths remain absolute. |
| `PathMap` and SourceLink | The XAML task still writes absolute AXAML document paths and an absolute CodeView PDB path. |
| `EmitCompilerGeneratedFiles` with a fixed `CompilerGeneratedFilesOutputPath` | Generated C# document paths are mapped. AXAML paths remain absolute. |
| `AvaloniaXamlCreateSourceInfo=false` | Both path leaks remain. This is already the Release default. |

These results do not establish that every possible build integration fails.
The pinned task exposes no setting that applies PathMap to its symbol writer.
Replacing its target or writer needs separate build review.
Removing symbols or excluding Editor.App would violate the current reproducibility policy.

## Minimal reproduction

Run this command from the repository root:

```sh
python3 eng/reproduce-avalonia-pathmap.py
```

The script requires Python 3, the pinned .NET SDK, and NuGet access.
It generates one UserControl and its code-behind at two paths of different lengths.
Both builds use Avalonia 12.1.4 and identical source text.
Empty MSBuild import files isolate the fixture from repository build settings.
The script uses the normal NuGet cache. The full verification uses separate caches.

A read-only inspector uses `System.Reflection.Metadata` and `PEReader`.
It prints CodeView paths and portable PDB document names.
Both DLL and PDB SHA-256 hashes differ on Linux.
The script prints `REPRODUCED` when the hashes differ.
If the hashes match, the script exits with an error because the limitation did not reproduce.
Build errors also return a nonzero exit code.
The script does not change compiled bytes.

Generated inputs, outputs, and `hashes.json` remain under
`artifacts/avalonia-pathmap/repro-*` for inspection.
The fixture uses an example SourceLink URL. It does not test a debugger or source downloads.

## Required resolution

Keep the managed reproducibility check unchanged.
Prefer an upstream-supported compiler solution that preserves portable symbols and source mapping.
If binary processing remains necessary, review its implementation in a separate PR.
That review needs corruption tests, independent metadata validation, and debugger checks on all three platforms.
Hash equality alone does not establish valid debug metadata.

Before merge, rerun repository verification on Linux, Windows, and macOS.
Repeat native editor acceptance where supported.
This Linux investigation does not establish Windows or macOS debugger behavior.
