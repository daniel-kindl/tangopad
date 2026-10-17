# Tangopad

Tangopad is the foundation for an open-source, offline-first desktop text and
document editor for Windows, Linux, and macOS. The first planned formats are plain
text and Markdown. The design prioritizes performance, reliability, and independent modules.

This repository currently contains build infrastructure and a blank Avalonia window.
It has no editing, file access, parsing, rendering, highlighting, or export features.
Core owns `TextDocument` for plain-text document behavior.
Application contains no source types.

Install the exact .NET SDK from [global.json](global.json). Run the shared verification command from the repository root:

```sh
dotnet msbuild eng/verify.proj
```

The command checks locked dependencies, known vulnerabilities, formatting, static
analysis, the Release build, architecture tests, native startup of the blank shell, and managed build reproducibility.
It requires network access to NuGet.
Linux native startup needs an X11 display.
The check starts Xvfb when no display is open and the host has the Xvfb command.
[Native startup](docs/native-startup.md) records the supported hosts, native libraries, and results.

To open the blank window yourself, run:

```sh
dotnet run --project src/Editor.App --configuration Release
```

The app displays an empty window. Close the window to stop the app.

The repository contains these modules and support directories:

```text
src/
  Editor.App/                 Desktop startup and presentation boundary
  Editor.Application/         Application boundary, no types yet
  Editor.Core/                Plain-text document behavior
tests/
  Editor.Architecture.Tests/  Executable dependency policy
  Editor.Core.Tests/          Document identity, text, and baseline
  Editor.Desktop.SmokeTests/  Native startup of the blank shell
eng/                         Verification and dependency policy
docs/                        Architecture, decisions, research, and planned issues
.github/                     CI and contribution templates
```

Read [architecture](docs/architecture.md) for module ownership and the dependency
diagram. Read [CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow.
[Tooling research](docs/tooling-research.md) records version checks and optional
agent skills. [Planned issues](docs/backlog.md) define M0 and M1 work.
[Verification results](docs/bootstrap-verification.md) record the bootstrap checks.
[Native startup](docs/native-startup.md) records the blank-shell startup check and its platform limits.

Tangopad uses the [MIT license](LICENSE).
