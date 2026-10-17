# Bootstrap verification record

Date: 2026-10-17

Host: Fedora 44, Linux x64. SDK: 10.0.402. Runtime: 10.0.13.

## Initial bootstrap results

| Check | Result |
| --- | --- |
| `dotnet msbuild eng/verify.proj` | Passed |
| Locked restore and NuGet audit | Passed with all severities enabled for direct and transitive packages |
| Formatting and analyzers at info severity | Passed |
| Release compilation with SDK analyzers | Zero warnings and errors |
| Architecture tests | Three passed, none skipped |
| Prohibited dependency control | A temporary Debug-only Avalonia reference in Core failed the Debug policy test. The reference was removed. |
| Managed reproducibility | Two clean copies at different absolute paths produced identical SHA-256 hashes for all four project DLLs and portable PDBs |
| Native Linux startup | X11 contained an app window whose process ID matched the launched app. The window accepted `WM_DELETE_WINDOW`. The app exited with code zero. |
| Workflow validation | actionlint 1.7.12 accepted `.github/workflows/verify.yml` |

The first startup attempt found missing Linux native libraries. Installing
`fontconfig` and the required X11 and OpenGL libraries allowed startup and normal
close. No application fallback code was added for a host prerequisite.

The reproducibility check initially found xUnit's path-derived generated namespace.
A fixed test entry point removed the difference. See [ADR 0002](adr/0002-reproducible-verification.md).
The check used one SDK installation and one NuGet cache on the same host. It does
not establish equivalence across SDKs, operating systems, or native package outputs.

## Automated managed verification for issue #9

On the same Fedora 44 host, this command passed with SDK 10.0.402:

```sh
TMPDIR=/var/tmp dotnet msbuild eng/verify.proj
```

Locked restore and audit, format verification, Release compilation, all 18 tests,
and the automated managed comparison passed. Compilation produced zero warnings
and errors. The tests included three architecture cases, fourteen reproducibility
cases, and native Linux startup.

Two fresh source trees and two empty NuGet caches produced matching SHA-256
hashes for twelve files. These were the six solution projects' DLLs and portable
PDBs. Both isolated build logs reported zero warnings and errors. Inspection of
`artifacts/reproducibility` confirmed distinct absolute source and cache paths,
matching output manifests, no prior build outputs in the source manifest, and
removal of temporary build and cache directories.

The negative control disabled hash mismatch detection. Both changed-content
tests then failed because no exception occurred. Restoring mismatch detection
made the tests pass. Regression cases also reject missing outputs, overlapping
build paths, absolute or escaping output paths, and an empty output selection.
Source-copy tests reject stale destination directories, exclude prior outputs,
and verify that both builds use the original snapshot after a working file changes.

The first full run passed its existing stages but failed during isolated restore
because this host's `/tmp` quota was too small. Setting `TMPDIR=/var/tmp` moved
temporary files to the disk-backed filesystem. The successful run retained
separate empty caches. It did not reuse the normal package cache.

The [initial hosted run](https://github.com/daniel-kindl/tangopad/actions/runs/39038149179)
passed Ubuntu. macOS rejected an output path after MSBuild resolved `/var` to
`/private/var`. The path guard prevented an invalid comparison. Output selection
now uses MSBuild's evaluated project directory, with a regression case for this
path difference. Windows compared all twelve files successfully, then failed to
delete `Avalonia.BuildServices.Collector.dll`. Isolated builds now set the package's
documented `AVALONIA_TELEMETRY_OPTOUT=1` to prevent that background collector.
The [next hosted run](https://github.com/daniel-kindl/tangopad/actions/runs/39038534123)
passed Ubuntu and macOS. Windows again matched all twelve files, then failed to
delete the task assembly `Avalonia.BuildServices.dll`. The shared build command
now disables persistent build servers so task assemblies are released before
cache cleanup.

This local record covers Linux only. Hosted results appear in the pull request's
three Verify checks and per-host reproducibility artifacts. The comparison makes
no claim of matching hashes between operating systems. See
[managed reproducibility](managed-reproducibility.md) for output scope and limits.

## Remaining checks and setup

The GitHub Actions matrix is configured for Ubuntu 24.04, Windows 2025, and
macOS 15. This environment did not execute hosted CI or native Windows and macOS
startup. The workflow has read-only repository permissions and no secrets.

Submission created an empty initial commit on `main` to provide a pull request
base. [Issue #1](https://github.com/daniel-kindl/tangopad/issues/1) tracks this
bootstrap in the M0 milestone. The `dev` branch and branch protection remain
setup tasks after acceptance. [The backlog](backlog.md) contains further issue
drafts and acceptance criteria.

Core and Application have no behavior, so they have no unit test projects yet.
Project dependency checks do not prohibit filesystem calls through .NET base APIs.
Add API-level constraints with the first domain behavior. No editing or document
features are present.
