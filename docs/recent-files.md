# Recent files

Select Recent Files in the toolbar to reopen a local document. Each entry shows
its full path, so files with the same name remain distinguishable. The list holds
up to ten successfully opened or saved destinations, with the most recent first.
Picker Open, file drops, Save, Save As, and recent-file selection update the same list.
New and failed or cancelled operations do not add paths. A successful save before
an Open adds its destination even if the subsequent Open fails.

Reopening uses the existing Open workflow, including Save, Discard, Cancel,
strict UTF-8 decoding, and the operation guard. A successful reopen creates a new
clean document identity and resets undo. Format identification, source highlighting,
and preview follow the destination as they do for picker Open.
A failed reopen preserves the active document and editor state.
The existing error dialog reports the failure without blocking the UI thread.
Missing files, denied access, and invalid UTF-8 do not remove history entries.
This policy permits retry after a moved file returns or permissions change.

## Privacy and clearing

History contains paths only. Tangopad does not index content, create previews
for history, scan destinations, or send paths elsewhere.
Select Clear Recent Files to remove the list and queue an empty stored list.
This action preserves the active document, saved files, and undo history.
Future successful opens and saves start a new list.

History storage is best effort. The list remains usable in memory if storage fails.
The menu reports a failed write because previously stored paths can return after
restart if Clear cannot reach storage. Retry Clear after restoring access, or
close Tangopad and remove the settings file directly.
Clearing does not erase filesystem backups or guarantee recovery-proof deletion.

## Storage format and locations

`Editor.Infrastructure/RecentFileHistory.cs`, namespace `Editor.Infrastructure`,
owns the bounded history and filesystem storage. `EditorViewModel` exposes its
paths and commands. `Program.Main` composes persistent history for the desktop app.
Tests can supply memory-only history or an isolated settings path.
No Core or Application API or dependency changes are required.

The settings file is `Tangopad/recent-files.json` under the per-user data directory:

| Platform | Location |
| --- | --- |
| Linux | `$XDG_DATA_HOME/Tangopad/recent-files.json`, normally `~/.local/share/Tangopad/recent-files.json` |
| Windows | `%LOCALAPPDATA%\Tangopad\recent-files.json` |
| macOS | `~/Library/Application Support/Tangopad/recent-files.json` |

Linux and Windows use .NET's `LocalApplicationData` directory. macOS explicitly
uses the user's `Library/Application Support` directory. No administrator access
is required. A missing per-user directory produces memory-only history.

The UTF-8 JSON format has two properties, `version` with integer value `1`, and
`paths` with an array of absolute local path strings. No timestamps, document
contents, or other settings are stored. Paths normalize through `Path.GetFullPath`.
Windows uses ordinal case-insensitive comparison. Linux and macOS use ordinal
comparison to preserve distinct names on case-sensitive filesystems.
Symbolic links and filesystem identities are not resolved.

Loading happens in the background without delaying document commands. Settings
larger than 64 KiB, corrupt JSON, unknown versions, and inaccessible files produce
an empty initial list. Non-string and relative stored entries are ignored.
Valid paths are normalized, deduplicated, and bounded again during loading.
Early opens merge ahead of loaded paths. Clear suppresses the initial loaded list.
A later history change replaces corrupt or unsupported settings with version 1.

Updates run in order on background tasks. Each update writes a unique temporary
file in the settings directory, flushes and closes it, then replaces the settings
file with `File.Move(..., overwrite: true)`. There is no truncate or delete fallback.
Atomicity depends on the local filesystem, as it does for document replacement.
Unix temporary files permit owner read and write only. Windows inherits directory
permissions. Cleanup removes temporary files where access permits.
Shutdown allows up to 250 milliseconds for queued writes. Crashes or unavailable
storage can lose recent updates. Separate application processes use the last
successful write. There is no cross-process history merge or session restoration.

## Verification

Infrastructure tests check ordering, bounds, normalization, platform comparison,
corrupt and oversized storage, persistence, early updates, and Clear during loading.
ViewModel tests check denied document access and settings failures during Save and Clear.
Headless tests activate the compiled recent-file buttons. They check Save, Discard,
Cancel, failed opens, identity, exact text, highlighting, font size, Word Wrap,
selection, undo, picker updates, Save As, and clearing without document changes.
File-drop tests also check that a successful drop records its path.

On 2026-10-18, native acceptance on Fedora 44 under Xvfb reopened a persisted
Markdown path from the menu and showed syntax highlighting. Clear wrote an empty
stored list and preserved the active document. The editor then closed through
`WM_DELETE_WINDOW`. This was native X11 acceptance, not a Wayland session.
Windows and macOS native feature interaction remains untested locally.
The existing CI matrix runs the same unit, headless, and native startup checks on
Linux, Windows, and macOS. Native startup checks do not cover every menu interaction.
