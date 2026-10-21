# ADR 0007: Connect AvaloniaEdit to one document session

Status: Accepted

Date: 2026-10-17

Concurrency and persistence contracts are updated by [ADR 0009](0009-optimistic-file-concurrency.md).

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
Code-behind keeps platform shortcuts, focus, and native close integration.
AXAML binds the title, command availability, cancellation, and edit availability to EditorViewModel.
EditorBufferAdapter transfers exact text between AvaloniaEdit and the ViewModel.
DesktopEditorDialogs calls the platform file pickers.
The project leaves Avalonia developer tools unloaded.

CommunityToolkit.Mvvm 8.4.2 is the sole MVVM utility dependency.
EditorViewModel derives from ObservableObject.
Source-generated observable properties and relay commands publish state and CanExecute changes.
The ViewModel coordinates presentation state through DocumentSession.
It has no Avalonia controls, resource lookups, brushes, or dimensions.
It receives immutable title labels from the presentation resource adapter.
[Presentation resources](../presentation-resources.md) defines text and theme ownership.

IEditorDialogs isolates the file pickers and confirmation dialogs for tests.
It stays internal to Editor.App. It adds no application persistence contract.
There is no DI container or format layer.

DocumentSession.Document is authoritative for text and dirty state.
AvaloniaEdit retains its editing buffer, selection, caret, and undo history.
Each text-change event updates ViewModel.Text and the document.
A new document identity replaces the control text and clears undo history.
A programmatic ViewModel.Text update changes the buffer only if its text differs.
Unchanged text is not assigned during title or command notifications.
Undo therefore follows the same text-change path as typing.
The UI does not keep another clean baseline or infer saved state.

The window title displays the full destination or Untitled.
An asterisk marks dirty text. The toolbar exposes New, Open, Save, Save As, and Reload from Disk.
Shortcuts use Control on Linux and Windows, and Command on macOS.

The ViewModel serializes commands on the UI thread.
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

Reload from Disk uses the current `DocumentSession.Destination` without a picker.
The command is disabled for untitled documents and while another operation is pending.
It uses the same confirmation, load, cancellation, and error paths as Open.
Save writes the current buffer before reload. Discard permits the load without saving.
Cancel preserves the current editing state. New edits during confirmation Save prevent replacement.
A failed or cancelled load preserves identity, text, destination, selection, caret, and undo history.
A successful Save before a later failed load retains its normal baseline and history changes.

Successful reload creates a new session and document identity through `DocumentSession.LoadAsync`.
The destination stays the same. The new identity represents intentional document replacement,
even when the loaded text equals the buffer. The editor clears undo and redo, removes selection,
and resets the caret to line 1, column 1. Format, highlighting, preview, counts, and ending status
follow the loaded session. Visible Markdown preview remains visible and renders the new source.
Recent Files moves the destination to the front without duplicating it.
Core, Application, and Infrastructure retain their existing contracts.

Reload uses F5 on Windows and Linux, following [Kate's Reload shortcut](https://docs.kde.org/stable_kf6/en/kate/kate/the-menu-entries.html).
macOS uses Command+R, following [Apple's reload convention](https://support.apple.com/en-us/102564).
Neither gesture conflicts with the current window shortcuts or AvaloniaEdit editing gestures.
The toolbar tooltip displays the platform gesture. Reload does not watch for external changes.

Cancel operation requests cancellation of pending I/O.
Native picker APIs do not accept cancellation tokens.
An open picker must close before the ViewModel observes a cancellation request.
Expected file, encoding, and path errors appear in an owned dialog.
The ViewModel restores command availability after success, failure, or cancellation.
Persistence retains the UTF-8 and replacement rules in ADR 0006.
The UI does not normalize text or line endings.

## Alternatives

A general two-way text binding could replace the control buffer during ordinary state updates.
The small adapter checks text equality and resets undo only for a new document identity.
Direct file access in the window would duplicate persistence policy.
CommunityToolkit.Mvvm supplies notifications and asynchronous commands without reactive streams.
ReactiveUI, Rx.NET, messaging, and a DI container have no concrete consumer here.
Reconsider reactive composition only when an actual use case needs it.

## Verification

Editor.App.Tests drives ViewModel commands with controlled stores and dialogs.
A headless test uses the production styles, the actual editor control, and
FileTextDocumentStore. It checks keyboard text input, exact mixed line endings,
Save As output, dirty state, and undo history.
The same fixture checks compiled view names, theme resources, and dialog decisions.
Native startup remains a separate process test on each supported CI host.
See [editor verification](../editor-verification.md) for results and platform limits.
