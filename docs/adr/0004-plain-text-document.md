# ADR 0004: Track document changes against acknowledged text

Status: Accepted

Date: 2026-10-17

## Context

[Issue #13](https://github.com/daniel-kindl/tangopad/issues/13) introduces the first
Core behavior. A document needs identity, exact text, and a clean baseline.
Core must not read files, parse content, or depend on the desktop framework.
[Issue #35](https://github.com/daniel-kindl/tangopad/issues/35) later adds
[filename-based format identification](../document-formats.md) in Core.

## Use cases and invariants

| Use case | Result |
| --- | --- |
| Create an untitled document | New nonempty identity, empty text, clean state |
| Open supplied text | New identity, exact supplied text as the clean baseline |
| Replace text | Preserve identity and baseline, replace the entire text |
| Supply unchanged text | Preserve the current clean or dirty state |
| Revert to the baseline | Clear dirty state |
| Acknowledge supplied text | Make supplied text the clean baseline, preserve identity and current text |
| Supply null text for creation, editing, or acknowledgement | Throw `ArgumentNullException` without changing existing state |

Empty text and all .NET string contents are valid. Core does not normalize
Unicode, line endings, or whitespace. Ordinal comparison distinguishes canonically
equivalent Unicode sequences with different UTF-16 contents.

## Decision

Use the sealed `TextDocument` class with `CreateUntitled`, `FromText`,
`ReplaceText`, and `AcceptChanges(string text)`. Expose read-only `Id`, `Text`, and `IsDirty`
properties. The model stores current text and baseline text. `IsDirty` compares
them with `StringComparison.Ordinal`. Immutable strings let both values share
storage until an edit. Comparison can take time proportional to text length.
This cost is acceptable for the first whole-text use case.

Generate a `Guid` once for each open document. Identity survives edits and
acknowledgement. Opening the same text twice creates distinct documents.
Identity is not a content hash or a filesystem identity. Path and display name
are outside this model. A later path change must not replace document identity.
Persistent identity, serialization, and identity restoration remain deferred.

A new untitled document has an empty clean baseline. Its dirty state answers
whether text changed, not whether a file exists. `DocumentSession` requires
save-as for an untitled document, as defined in ADR 0005.

`AcceptChanges(string text)` acknowledges supplied text. It performs no persistence.
It can acknowledge an older saved snapshot while newer edits remain dirty.
Callers must serialize access. [ADR 0005](0005-document-persistence-use-cases.md)
defines asynchronous save acknowledgement and replaces the original parameterless API.

Reject an edit counter or sticky dirty flag because reverting text must clear
dirty state. Defer revision identifiers, events, interfaces, undo history, and
incremental text storage until their consumers exist.

## Architecture checks

Add `Editor.Core.Tests` with a reference only to Core and the existing xUnit
package. Keep architecture tests independent of production assembly references.
They inspect the built Core assembly metadata in the test configuration.
The Core API test builds Core for the active test configuration.
That test reads the assembly at the evaluated `TargetPath`.

Core may reference only `System.Runtime`. Referenced type namespaces are limited
to `System`, `System.Diagnostics`, `System.Reflection`,
`System.Runtime.CompilerServices`, and `System.Runtime.Versioning`.
`System.Diagnostics`, `System.Reflection`, `System.Runtime.CompilerServices`, and
`System.Runtime.Versioning` are permitted because the compiler emits assembly and
compiler attributes in those namespaces. Native imports are prohibited.
The namespace rule excludes filesystem APIs in `System.IO`, including filesystem
types from the base runtime. The assembly rule excludes Avalonia, rendering
libraries, and external format parsers.

These checks inspect static references. They do not prove semantic ownership
or detect arbitrary runtime code generation. Review remains necessary. A new
runtime API requires a boundary review before the allowlist changes.

## Consequences

Core has no package, framework, or adapter dependencies beyond the base runtime.
The shared verification command discovers the new test project and validates its
TRX report.
[ADR 0005](0005-document-persistence-use-cases.md) adds application orchestration.
Filesystem integration and UI behavior remain deferred.
