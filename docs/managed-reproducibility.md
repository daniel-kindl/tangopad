# Managed build reproducibility

Run the shared verification command from the repository root:

```sh
dotnet msbuild eng/verify.proj
```

The final stage builds the same source snapshot twice. Each build starts in a
new temporary directory with separate source, output, and NuGet cache paths.
Git must be available. Restore requires NuGet access for both fresh caches.
The tool removes temporary build directories and caches after the comparison.
Isolated builds set `AVALONIA_TELEMETRY_OPTOUT=1` to prevent the background
collector from keeping temporary package files open on Windows.
The temporary filesystem needs space for two restored package caches and builds.
On Unix, set `TMPDIR` to a disk-backed directory if the default temporary
filesystem has insufficient space.

## Compared outputs

The tool reads every project from `Tangopad.slnx`. For each project, it evaluates
Release `TargetPath`, `DebugType`, and `MSBuildProjectDirectory` through MSBuild.
Relative output paths use the evaluated project directory, which accounts for
macOS temporary path aliases. It compares that project's managed DLL and adjacent portable PDB by SHA-256. All six solution projects
currently produce twelve eligible files, including the verification executable
and both test assemblies. The tool checks assembly and portable PDB metadata
before comparing hashes.

The comparison excludes copied dependency DLLs, native package assets, native
apphosts, satellite resources, and packaging outputs. Those files are outside
the project DLL and PDB selection. Native outputs need their own reproducibility
criteria. A successful comparison does not prove installer or native reproducibility.

## Recorded evidence

Each run replaces `artifacts/reproducibility` with these files:

- `inputs.json` records the base commit, every source file's SHA-256 hash, SDK,
  host, configuration, restore arguments, collector opt-out, and absolute source
  and cache paths.
- `build-1.log` and `build-2.log` contain successful restore and build output.
- `hashes-1.json` and `hashes-2.json` contain the selected outputs and their hashes.

A failed stage can leave partial evidence. Command failures also appear in the
verification output. CI uploads available evidence for each host, including
failed comparisons. Git-tracked and non-ignored untracked working files form the
source snapshot. Deleted files, build outputs, and Git metadata are excluded.
Thus, local verification includes pending source changes without requiring a commit.

## Limits

The comparison uses the same host, SDK installation, configuration, and locked
package inputs. It does not compare outputs between operating systems or SDKs.
The separate caches test package-path independence on that host. They do not
establish reproducibility against a different package feed or advisory database.
The check covers the solution's current Release target frameworks and defaults.
It does not test other runtime identifiers or publish configurations.

[ADR 0002](adr/0002-reproducible-verification.md) records the decision.
[Verification results](bootstrap-verification.md) record tested hosts and controls.
