# External file notifications

Tangopad monitors the current local file. A nonblocking message appears when a
verified disk revision differs from the last successful load or save.
Modification, deletion, rename, and replacement can produce this message.
The editor does not automatically replace text or change the clean baseline.
Source Markdown, undo, redo, caret, and selection remain intact.

Reload from Disk uses the existing unsaved-change confirmation. Save still checks
for conflicts if that confirmation requests a save. Dismiss hides the message for
the observed disk revision. A later revision can show another message.
Dismiss does not acknowledge disk changes or permit an overwrite.

## Ownership and lifecycle

`IDocumentFileMonitor` belongs at the Application project root in
`Editor.Application`. It supplies event hints and reads opaque revisions.
`DocumentSession.Revision` exposes the last successful persistence revision.
The monitor cannot change that revision or the document.

`LocalDocumentFileMonitor` belongs at the Infrastructure project root in
`Editor.Infrastructure`. It uses one `FileSystemWatcher` for the current filename
in its parent directory. It receives modification, creation, deletion, rename,
and backend error events. It reuses `FileTextDocumentStore` revision checks.
These checks hash raw bytes, including a BOM and invalid UTF-8, without decoding.
Monitoring reads permit concurrent writers and deletion. Save-time reads retain
their existing sharing restrictions.
A missing file or parent directory produces a missing revision.
The check uses one 64 KiB buffer and reads at most the captured file length.

`ExternalFileChangeAdapter` belongs in App's `Views` namespace beside its window.
It connects monitoring to presentation without adding Avalonia to the ViewModel.
A bounded channel retains one pending event. A worker coalesces events over
150 milliseconds, then verifies disk state outside the UI thread.
The dispatcher applies a result only to its originating active subscription.
All worker continuations observe cancellation and expected filesystem failures.

The adapter cancels verification and disposes its watcher when a file operation
starts. When the operation ends, it monitors the resulting destination and checks
once immediately after the debounce interval. This also checks changes during
an operation, including failed Open or Reload. Successful New, Open, Reload, and
Save As cannot retain a notification from the previous document or revision.
Own atomic saves establish the accepted revision before monitoring restarts.
Failed operations retain the original revision and dismissal state.
Window close detaches handlers, disposes the watcher, and cancels verification.

No packages, project references, or dependency-policy changes are required.

## Filesystem limits

Monitoring is optional and best effort. Unsupported mounts, permissions, backend
errors, and network drives can prevent monitoring or lose events.
A subscription failure still permits one verification after each file operation.
A verification failure leaves presentation state unchanged. A later event or
file operation can request another check. Monitoring failures do not block
editing, loading, or saving and do not open an error dialog.

Save-time revision checks remain authoritative. Monitoring is not synchronization
or an atomic comparison and replacement guarantee. Identical bytes and metadata
remain indistinguishable, as described in [ADR 0009](adr/0009-optimistic-file-concurrency.md).

## Manual desktop acceptance

Run these checks on Windows, macOS, and Linux Wayland in an interactive session.

1. Open a UTF-8 Markdown file. Make local edits and select text.
2. Edit the file externally. Check that the message appears without a modal dialog.
3. Check that source text, preview state, selection, caret, undo, redo, and dirty state remain unchanged.
4. Use a keyboard and screen reader to reach and identify Reload from Disk and Dismiss.
5. Dismiss the message. Save and cancel the conflict dialog. Check that external bytes remain unchanged.
6. Make another external edit. Check that the message returns.
7. Choose Reload from Disk. Exercise Save, Discard, and Cancel in separate attempts.
8. Check that Save retains conflict protection and Discard loads the external bytes.
9. Delete, rename, and replace the file externally. Check each notification and guarded reload failure or success.
10. Save locally. Check that the accepted save does not leave a message about itself.
11. Exercise Save As, New, successful Open, failed Open, and Reload while events arrive.
12. Close the window while a verification is pending. Check that the application exits normally.
13. Check both themes and a narrow window. Check that message actions remain visible and reachable.
14. If available, repeat on a network mount. Check that monitoring failure does not prevent editing or saving.

The Linux X11 script in `eng/check-native-editor.py` includes a representative
external edit, message capture, and explicit reload. Hosted verification covers
Linux, Windows, and macOS. Native results and untested platforms belong in
[editor verification](editor-verification.md).
