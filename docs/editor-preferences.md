# Editor preferences

Tangopad restores Word Wrap, Line Numbers, and editor font size on desktop startup.
The defaults are Word Wrap off, Line Numbers on, and font size 15.
Font size stays between 8 and 48. Theme follows the existing application policy.
There is no theme selection or general preferences dialog.

Preferences apply to the window. New, Open, Save As, Reload, file drops, and
Markdown preview preserve them. Preference changes do not modify document identity,
text, dirty state, selection, caret, undo, redo, or saved document bytes.

## Storage and privacy

`Editor.Infrastructure/EditorPreferences.cs` and `EditorPresentation.cs`, in
namespace `Editor.Infrastructure`, own storage and its immutable snapshot.
`UserDataLocation.cs` shares the per-user location rule with Recent Files.
`Editor.App/ViewModels/EditorViewModel.cs` owns UI state and startup synchronization.
`Program.Main` composes persistent storage. Core and Application have no changes.
No new package or project dependency is required.

The file is separate from Recent Files:

| Platform | Location |
| --- | --- |
| Linux | `$XDG_DATA_HOME/Tangopad/editor-preferences.json`, normally `~/.local/share/Tangopad/editor-preferences.json` |
| Windows | `%LOCALAPPDATA%\Tangopad\editor-preferences.json` |
| macOS | `~/Library/Application Support/Tangopad/editor-preferences.json` |

Linux and Windows use .NET's `LocalApplicationData` directory.
macOS uses the user's `Library/Application Support` directory.
An unavailable per-user location selects memory-only preferences.
Tangopad creates the directory on the first change.

The UTF-8 JSON document uses this format:

```json
{"version":1,"wordWrap":false,"showLineNumbers":true,"fontSize":15}
```

The file contains no document text, document paths, telemetry, or credentials.
Tangopad does not send preferences elsewhere. Unix temporary files permit owner
read and write only. Windows inherits directory permissions.

## Startup and failures

Defaults apply immediately. Loading runs in the background.
Each explicit user change takes precedence over the stored value for that field.
Untouched fields receive their stored values when loading completes.
Reset Font Size also takes precedence when the current size is already 15.

Missing, inaccessible, malformed, partially written, oversized, and unknown-version
files leave defaults in effect. The size limit is 4 KiB, enforced during the read.
Missing or invalid fields use their defaults. Unknown keys are ignored.
Finite font sizes are clamped to 8 through 48. Non-finite numbers use 15.
A later user change can replace invalid or unsupported storage with version 1.

Updates run asynchronously and combine pending changes into the latest snapshot.
Unchanged values do not cause another write after startup.
Each write flushes and closes a unique `.preferences-<guid>.tmp` file in the same
directory, then uses `File.Move(..., overwrite: true)` to replace the destination.
There is no truncate or delete fallback. Cleanup removes temporary files where
access permits. Replacement atomicity depends on the filesystem.
Failed writes preserve in-memory choices and never block document operations.
A later change retries storage. Failures do not open a dialog.

Desktop shutdown allows up to 250 milliseconds total for pending Recent Files
and preference writes. A crash, storage failure, or expired shutdown wait can lose
recent changes. Separate processes use the last successful replacement.
There is no cross-process merge or session restoration.

## Reset to defaults

1. Close all Tangopad processes.
2. Delete `editor-preferences.json` from the location above.
3. Start Tangopad.

This procedure preserves Recent Files and document files.

## Verification

Infrastructure tests cover defaults, corruption, unsupported versions, size limits,
font bounds, concurrent updates, duplicate writes, failures, retries, and location
selection. Headless Avalonia tests check restored editor properties, early changes,
and Reset Font Size before startup values reach the controls.
Existing control tests check document transitions, exact saved bytes, selection,
caret, undo, redo, file drops, and Markdown preview.
The first persistence test failed to compile before the storage API existed.

On 2026-10-20, `dotnet msbuild eng/verify.proj` passed on Linux with zero build
warnings or errors. All 325 applicable tests passed. One Windows sharing test was
skipped on Linux. Native acceptance on Fedora 44 under Xvfb also passed through
`python3 eng/check-native-editor.py`. It changed all three preferences, closed the
process, and compared the restored toolbar and editor pixels after relaunch.
The restored values were Word Wrap on, Line Numbers off, and font size 18.
The fixture retained its exact bytes. The native processes exited with code 0.
Windows and macOS native feature interaction remains untested locally.
The existing hosted CI matrix runs verification on Linux, Windows, and macOS.
