# Recent files

Select Recent Files in the toolbar to reopen a local document. Each entry shows
the filename first, followed by its directory. A tooltip shows the full path.
Files with the same name remain distinguishable, and long directories do not
hide the filename. The list holds
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
close Tangopad and follow the recovery procedure in [application state](app-state.md).
Clearing does not erase filesystem backups or guarantee recovery-proof deletion.

## Storage format and locations

Recent Files shares `state.bin` with editor preferences.
See [application state](app-state.md) for storage locations, portable mode,
schema bounds, migration, backup, and update rules.
`RecentFileHistory` owns ordering and normalization in `Editor.Infrastructure`.
`AppStateStore` serializes writes from both consumers.
No Core or Application dependency changes are required.

Paths normalize through `Path.GetFullPath`. Windows uses ordinal case-insensitive
comparison. Linux and macOS use ordinal comparison. Symbolic links and filesystem
identities are not resolved. Early opens merge ahead of loaded paths.
Clear suppresses the initial loaded list. Invalid or unsupported binary state
remains unchanged and blocks writes until recovery.

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
An isolated Linux process also verified that the per-user factory persists history
when `$XDG_DATA_HOME` points to a directory that does not yet exist.
`eng/check-native-editor.py` repeats the recent-file reopen and persisted Clear
checks through native input. It checks that Clear preserves dirty text and Undo.
Windows and macOS native feature interaction remains untested locally.
The existing CI matrix runs the same unit, headless, and native startup checks on
Linux, Windows, and macOS. Native startup checks do not cover every menu interaction.
