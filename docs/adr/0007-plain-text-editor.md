# ADR 0007: Connect AvaloniaEdit to one document session

Status: Accepted

Date: 2026-10-17

## Context

[Issue #19](https://github.com/daniel-kindl/tangopad/issues/19) needs a usable
plain-text window. Core and Application already own exact text and save
acknowledgement. The UI must preserve those rules while file operations wait.

## Decision

Editor.App uses Avalonia.AvaloniaEdit 12.0.0.
Its [published dependencies](https://www.nuget.org/packages/Avalonia.AvaloniaEdit/12.0.0)
require Avalonia >= 12.0.0 and include net10.0 assets.
The pinned Avalonia 12.1.4 satisfies that requirement.
Only this editor package enters production. Avalonia.Headless 12.1.4 supports
control tests without adding a second test framework.

Program.Main composes FileTextDocumentStore and the application.
App.axaml owns the Fluent theme and the AvaloniaEdit Fluent theme.
App code-behind loads that markup.
App code-behind opens the desktop window.
EditorWindow.axaml owns the window size, toolbar, editor control, and their fixed properties.
UnsavedChangesDialog.axaml owns the unsaved-changes prompt.
ErrorDialog.axaml owns the file-operation error prompt.
Code-behind loads each view.
Code-behind keeps shortcut selection, tooltips, commands, text synchronization, cancellation, and close behavior.
AXAML is the only definition of those controls.
DesktopEditorDialogs calls the platform file pickers.
The project leaves Avalonia developer tools unloaded.
The Avalonia XAML compiler writes absolute paths into the Editor.App debug output.
The byte-rewriting build target was removed after review.
[Compiled AXAML path mapping](../avalonia-pathmap.md) records the remaining reproducibility blocker.
View models and design tokens remain separate follow-up work.
EditorWindow owns AvaloniaEdit, toolbar actions, shortcuts, and close handling.
EditorWorkspace coordinates presentation state through DocumentSession.
IEditorDialogs isolates the file pickers and confirmation dialogs for tests.
It stays internal to Editor.App. It adds no application persistence contract.
There is no DI container or format layer.

DocumentSession.Document is authoritative for text and dirty state.
AvaloniaEdit retains its editing buffer, selection, caret, and undo history.
Each text-change event transfers exact text to the document.
Only a new document identity replaces the control text and clears undo history.
State notifications update the title and controls without assigning text.
Undo therefore follows the same text-change path as typing.
The UI does not keep another clean baseline or infer saved state.

The window title displays the full destination or Untitled.
An asterisk marks dirty text. The toolbar exposes New, Open, Save, and Save As.
Shortcuts use Control on Linux and Windows, and Command on macOS.

The workspace serializes commands on the UI thread.
It disables replacement commands while an operation is pending.
Open and replacement confirmation make the editor read-only.
Save permits editing while the store writes its captured snapshot.
After Save in an unsaved-changes confirmation, replacement proceeds only if
the document is still clean. Newer edits therefore prevent replacement or close.
A close request during another command keeps the window open.
Successful close posts the final Close call to avoid recursive close handling.

Pickers use the window StorageProvider. The platform selects the desktop backend.
Only local filesystem paths are accepted.
Cancelled pickers preserve the current session.
Open first selects a path, then confirms unsaved changes, then loads a new session.
A failed or cancelled load preserves the current session.
A successful Save before a later failed load can still update its baseline.
Save As changes the destination only after successful persistence.

Cancel operation requests cancellation of pending I/O.
Native picker APIs do not accept cancellation tokens.
An open picker must close before the workspace observes a cancellation request.
Expected file, encoding, and path errors appear in an owned dialog.
The workspace restores command availability after success, failure, or cancellation.
Persistence retains the UTF-8 and replacement rules in ADR 0006.
The UI does not normalize text or line endings.

## Alternatives

Binding two mutable text properties would require feedback suppression and
could replace the control buffer during ordinary state updates.
Direct file access in the window would duplicate persistence policy.
A general command framework or view-model package would add dependencies for
four actions. An internal ICommand adapter supplies the existing shortcut API.

## Verification

Editor.App.Tests drives workspace commands with controlled stores and dialogs.
A headless test uses the production styles, the actual editor control, and
FileTextDocumentStore. It checks keyboard text input, exact mixed line endings,
Save As output, dirty state, and undo history.
The same fixture checks compiled view names, theme resources, and dialog decisions.
Native startup remains a separate process test on each supported CI host.
See [editor verification](../editor-verification.md) for results and platform limits.
