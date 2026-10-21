# ADR 0009: Check disk revisions before saving

Status: Accepted

Date: 2026-10-20

## Context

[Issue #73](https://github.com/daniel-kindl/tangopad/issues/73) requires explicit
choices before a save replaces external changes. Timestamps and byte lengths
alone cannot detect all edits. The editor must retain strict UTF-8 persistence,
explicit reload, and its existing buffer state on a rejected save.

## Decision

Application owns the persistence contract at its project root in `Editor.Application`.
`StoredDocument` transfers exact text and an opaque revision from `ITextDocumentStore`.
`DocumentSession` retains that revision separately from the document's text baseline.
Save returns false for a conflict. It acknowledges text, revision, destination,
and format only after confirmed successful persistence. Failure and observed
cancellation preserve these values. Pending edits retain their existing snapshot rules.

Infrastructure owns revision interpretation in `FileTextDocumentStore`.
A revision includes the absolute destination, byte length, last-write time,
and SHA-256 of the raw bytes. Loading hashes the same open stream that supplies
strict UTF-8 text. The hash includes a leading BOM even though decoding removes it.
Saving derives the new revision from the complete temporary file before replacement.
It does not read the destination after replacement to establish the new baseline.
An external edit immediately after commit therefore cannot become an accepted baseline.

Ordinary save compares the loaded or last successfully saved revision.
Save As to the same absolute path uses that revision too.
A different destination permits creation only. An existing destination requires
an explicit overwrite choice, including for an untitled document.
Path aliases and differently cased paths can conservatively require confirmation.
The revision does not identify documents by filename or by editor text.

The first check occurs before temporary-file creation. A second check occurs
after writing and closing the complete temporary file, immediately before replacement.
Explicit overwrite accepts the disk state observed when that attempt starts.
The second check still rejects a change during temporary-file preparation.
New destinations use `File.Move` without overwrite, which also rejects a file
that appears after the last check. Existing destinations retain same-directory replacement.
Temporary-file cleanup, strict encoding, private Unix creation permissions, and
error propagation retain the rules in [ADR 0006](0006-local-text-persistence.md).

Comparison rejects different paths, lengths, or timestamps without hashing file contents.
Equal metadata always requires a complete SHA-256 comparison. Each hash uses one
64 KiB buffer and reads at most the byte length captured when its handle opens.
A growing file cannot extend that loop. A short read, changed length, or changed
write time during hashing fails safely through the existing file-error boundary.
Reads receive cancellation. Memory for comparison is constant with file size.
Work is linear in the captured size when metadata matches. There is no sampling,
metadata-only acceptance, background scan, or arbitrary large-file bypass.
A successful save can read the existing file twice and the temporary file once.
Large saves can therefore take longer. The editor remains editable and Cancel operation remains available.

App owns `ConflictDecision` and `IEditorDialogs` in `Services`, command handling
in `ViewModels`, and `FileConflictDialog` in `Views/Dialogs`.
Text resources remain in `Resources/Text.axaml`.
The dialog identifies the destination and explains all actions. Cancel is the
default and Escape action. Closing the window also returns Cancel.
Overwrite anyway is an explicit confirmation that permits replacement or recreation.
Reload from Disk loads the current destination through the existing unsaved-change guard.
For an untitled document, it loads the selected destination through that same guard.
Cancel and failed saves do not assign editor text or document identity.
Caret, selection, undo, redo, dirty state, and recent history remain intact.
New edits during conflict confirmation stop the pending decision without a write.
A reload chosen during another replacement command ends that original command.

No module references, packages, namespaces, or background services are added.
The port and session return types change with all consumers in this repository.

## Filesystem limits

This policy uses optimistic checks. It does not lock other programs or provide
a portable atomic compare-and-replace operation for existing destinations.
Another writer can change or replace a destination after the final check and
before the move. Two simultaneous saves can both pass that check.
Sequential edits by independent Tangopad instances conflict through their separate revisions.
Hashing can also race with an uncooperative in-place writer that restores metadata.
A later save normally detects its changed bytes, but no consistent snapshot is promised.

A replacement with identical bytes and last-write time is indistinguishable from
the previous file. The revision records content and metadata, not a portable filesystem object ID.
Hash collisions are also theoretically possible. Symlink and hard-link behavior,
metadata replacement, unusual mount behavior, and crash durability retain ADR 0006's limits.
No perfect cross-platform exclusion or atomic conflict prevention is claimed.

Cancellation can race with the synchronous final move. As in ADR 0005, observed
cancellation after commit can leave complete saved bytes without session acknowledgement.
A later ordinary save then requires confirmation if its old revision differs.
There is no rollback after a successful move.

## Verification

Real-file tests compare exact bytes for external edits, restored timestamps,
removal, rename and replacement, competing instances, Save As, repeated saves,
Unicode, mixed line endings, deliberate overwrite, and failed strict encoding.
Existing permission, cancellation, locked-file, and cleanup tests remain active.
Application tests check revision retention after conflict and snapshot acknowledgement.
Headless tests check dialog results, cancellation, pending edits, reload confirmation,
and retained identity, selection, caret, undo, and redo.
`eng/check-native-editor.py` exercises conflict decisions and missing-file recreation
through X11 input and the native editor. Platform results and remaining manual
checks are recorded in [editor verification](../editor-verification.md).

## Alternatives

Timestamps alone miss same-size edits with restored timestamps.
File watchers can miss events and require a separate save-time check.
Long-lived exclusive locks restrict other programs and behave differently across filesystems.
Platform-specific file IDs can detect identical-content replacements, but add native
interop and still cannot make a separate check and rename atomic.
A merge engine exceeds the scope of this explicit save and reload workflow.
