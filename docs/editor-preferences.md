# Editor preferences

Tangopad restores Word Wrap, Line Numbers, and editor font size on desktop startup.
The defaults are Word Wrap off, Line Numbers on, and font size 15.
Font size stays between 8 and 48. Theme follows the existing application policy.
There is no theme selection or general preferences dialog.

Preferences apply to the window. New, Open, Save As, Reload, file drops, and
Markdown preview preserve them. Preference changes do not modify document identity,
text, dirty state, selection, caret, undo, redo, or saved document bytes.

## Storage and startup

Preferences share the versioned binary file with Recent Files.
See [application state](app-state.md) for locations, portable mode, the schema,
JSON migration, error handling, backup, and updater requirements.
`EditorPreferences` retains its immutable `EditorPresentation` snapshot.
`AppStateStore` owns the single writer in `Editor.Infrastructure`.
`Program.Main` composes both consumers from the same store.

Defaults apply immediately. Each explicit change takes precedence over the
stored value for that field. Untouched fields receive their stored values when
loading completes. Reset Font Size also takes precedence when the current size
is already 15. Finite font sizes are clamped to 8 through 48.
Non-finite input uses 15. Failed writes preserve in-memory choices.

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
