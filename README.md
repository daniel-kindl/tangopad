# Tangopad

Tangopad is the foundation for an open-source, offline-first desktop text and
document editor for Windows, Linux, and macOS. The first planned formats are plain
text and Markdown. The design prioritizes performance, reliability, and independent modules.

Tangopad currently edits one plain-text document. New, Open, Save, and Save As
use the existing document session and strict UTF-8 file store.
Markdown, highlighting, export, and multiple tabs remain unimplemented.

Install the exact .NET SDK from [global.json](global.json). Run the shared verification command from the repository root:

```sh
dotnet msbuild eng/verify.proj
```

The command checks locked dependencies, known vulnerabilities, formatting, static
analysis, the Release build, architecture tests, and native editor startup.
It requires network access to NuGet.
Linux native startup needs an X11 display.
The check starts Xvfb when no display is open and the host has the Xvfb command.
[Native startup](docs/native-startup.md) records the supported hosts, native libraries, and results.

To start the editor, run:

```sh
dotnet run --project src/Editor.App --configuration Release
```

Type in the new document. Use Open to select a UTF-8 file.
Use Save to write the document, or Save As to select another destination.
An asterisk in the title marks unsaved changes. The title shows the full file path.
New, Open, and window close ask whether to save unsaved changes.
Cancel leaves the document open. Errors appear in a dialog.

| Action | Linux and Windows | macOS |
| --- | --- | --- |
| New | Ctrl+N | Command+N |
| Open | Ctrl+O | Command+O |
| Save | Ctrl+S | Command+S |
| Save As | Ctrl+Shift+S | Command+Shift+S |
| Find | Ctrl+F | Command+F |
| Replace | Ctrl+H | Command+H |

Word Wrap in the toolbar fits long lines to the editor width. It starts off and
stays selected across New and Open until you change it or close the window.
Wrapping changes only the display. It preserves text, saved line endings, and undo history.

Find searches literal text with optional case sensitivity. Enter finds the next match
while focus is in a search field. Shift+Enter finds the previous match.
Both directions wrap. Escape closes the panel and returns focus to the editor.
Paste multiline text into either search field to preserve its exact line endings.
Replace current changes only a selected match. Replace all replaces non-overlapping
matches from left to right and reports the replacement count. One Undo restores the operation.
[Search behavior](docs/find-replace.md) describes matching and command availability.

You can continue editing during a save. Edits after the saved snapshot remain dirty.
Cancel operation requests cancellation of pending file I/O.
Close an open file picker to complete a cancellation request.

Files load as strict UTF-8. Saves write UTF-8 without an added BOM.
Text and line endings remain unchanged. See [persistence rules](docs/adr/0006-local-text-persistence.md)
for BOM handling, file replacement, and metadata limits.
[Editor verification](docs/editor-verification.md) records tested behavior and desktop limits.

The repository contains these modules and support directories:

```text
src/
  Editor.App/                 Desktop startup and presentation boundary
  Editor.Application/         Plain-text load and save use cases
  Editor.Core/                Plain-text document behavior
  Editor.Infrastructure/      Local UTF-8 file persistence
tests/
  Editor.App.Tests/           Presentation commands and editor control
  Editor.Application.Tests/   Document load and save behavior
  Editor.Infrastructure.Tests/ Real filesystem integration
  Editor.Architecture.Tests/  Executable dependency policy
  Editor.Core.Tests/          Document identity, text, and baseline
  Editor.Desktop.SmokeTests/  Native editor startup
eng/                         Verification and dependency policy
docs/                        Architecture, decisions, research, and planned issues
.github/                     CI and contribution templates
```

Read [architecture](docs/architecture.md) for module ownership and the dependency
diagram. The [file placement rules](docs/architecture.md#file-placement) describe
presentation directories and when to add feature folders.
Read [CONTRIBUTING.md](CONTRIBUTING.md) for the development workflow.
[Tooling research](docs/tooling-research.md) records version checks and optional
agent skills. [Planned issues](docs/backlog.md) define M0 and M1 work.
[Verification results](docs/bootstrap-verification.md) record the bootstrap checks.
[Native startup](docs/native-startup.md) records the native startup check and its platform limits.

Tangopad uses the [MIT license](LICENSE).
