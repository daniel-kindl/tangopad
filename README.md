# Tangopad

Tangopad is the foundation for an open-source, offline-first desktop text and
document editor for Windows, Linux, and macOS. The first planned formats are plain
text and Markdown. The design prioritizes performance, reliability, and independent modules.

This repository currently contains build infrastructure and a blank Avalonia window.
It has no editing, file access, parsing, rendering, highlighting, or export features.
Core and Application contain no source types.

Install the exact .NET SDK from [global.json](global.json). Run the shared verification command from the repository root:

```sh
dotnet msbuild eng/verify.proj
```

The command checks locked dependencies, known vulnerabilities, formatting, static
analysis, the Release build, and architecture tests. It requires network access to NuGet.
To check native startup on a desktop, run:

```sh
dotnet run --project src/Editor.App --configuration Release --no-build
```

The app displays an empty window. Close the window to stop the app.
Linux requires the native libraries listed in the [Avalonia platform policy](https://docs.avaloniaui.net/docs/supported-platforms).

The repository contains these modules and support directories:

```text
src/
  Editor.App/                 Desktop startup and presentation boundary
  Editor.Application/         Application boundary, no types yet
  Editor.Core/                Domain boundary, no types yet
tests/
  Editor.Architecture.Tests/  Executable dependency policy
eng/                         Verification and dependency policy
docs/                        Architecture, decisions, research, and planned issues
.github/                     CI and contribution templates
```

Read [architecture](docs/architecture.md) for module ownership and the dependency
diagram. Read [CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow.
[Tooling research](docs/tooling-research.md) records version checks and optional
agent skills. [Planned issues](docs/backlog.md) define M0 and M1 work.
[Verification results](docs/bootstrap-verification.md) record completed checks and platform limits.

Tangopad uses the [MIT license](LICENSE).
