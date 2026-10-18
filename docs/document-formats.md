# Document format identification

[Issue #35](https://github.com/daniel-kindl/tangopad/issues/35) adds filename-based
classification for plain text and Markdown. Classification does not validate
content or select a different editor, encoding, parser, or renderer.

## Filename policy

`Editor.Core.DocumentFormats.Identify` owns the mapping. It returns a
`DocumentFormat` value without file access.

| Final extension | Format |
| --- | --- |
| `.txt` | `PlainText` |
| `.md` | `Markdown` |
| `.markdown` | `Markdown` |
| Any other extension, or no extension | `PlainText` |

Matching uses `StringComparison.OrdinalIgnoreCase`. Only the final filename
component and its last extension count. Both `/` and `\` separate components
on every host. A directory named `draft.md` does not classify its children as Markdown.

A leading dot alone does not introduce an extension. `.md` and `.markdown`
are plain text. `.notes.md` is Markdown. A null or empty name, a trailing dot,
and an empty final component default to plain text. Names are not trimmed or
normalized. Unicode paths follow the same rule.

Unknown extensions do not prevent opening readable files. A recognized extension
does not prove that the text is valid Markdown. Text contents never affect classification.

## Document association

`DocumentSession.Format` derives its value from `Destination`. An untitled
session starts as plain text, including when its text contains Markdown syntax.
A successful open classifies the loaded destination. Save uses the current
destination. A successful Save As classifies the new destination.

While Save As is pending, the session retains its previous destination and format.
Failure or observed cancellation preserves both values. Cancellation of a file
picker does not call the session. Existing unsaved-change protections still apply.

Format is not part of `TextDocument`'s identity or clean baseline. Classification
cannot edit text or change dirty state. A successful save still acknowledges only
the captured text. Edits made during persistence remain dirty.

The [persistence policy](adr/0006-local-text-persistence.md) still controls UTF-8,
BOM handling, replacement, and metadata. Classification does not promise byte-for-byte
preservation for inputs that the existing persistence policy transforms, such as a BOM.

## Placement and verification

`DocumentFormat.cs` and `DocumentFormats.cs` remain beside `TextDocument.cs` in
`src/Editor.Core/`, with namespace `Editor.Core`. `DocumentSession.Format` remains
in `src/Editor.Application/`, with namespace `Editor.Application`. No project,
package, or namespace dependency changes are required. The UI needs no format label yet.

`DocumentFormatsTests` covers the filename table and path edge cases.
`DocumentSessionTests` covers open, new, save, Save As, pending edits, failures,
and cancellation. `FileTextDocumentStoreTests` checks exact UTF-8 bytes during
filename transitions and reopens the saved documents.

## Markdown presentation

Source highlighting and the optional preview consume the authoritative session
format. Neither feature checks file extensions.
[ADR 0008](adr/0008-safe-markdown-preview.md) defines parser ownership, rendering,
security, and resource limits.
