# Bootstrap verification record

Date: 2026-10-17

Host: Fedora 44, Linux x64. SDK: 10.0.402. Runtime: 10.0.13.

## Results

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
