# ADR 0005: Save captured text through an application persistence port

Status: Accepted

Date: 2026-10-17

Concurrency and persistence contracts are updated by [ADR 0009](0009-optimistic-file-concurrency.md).

## Context

[Issue #15](https://github.com/daniel-kindl/tangopad/issues/15) adds plain-text
load and save use cases. Persistence can wait while the user edits the document.
Acknowledging current text after that wait would incorrectly clear newer edits.
Core must retain exact text semantics without filesystem or encoding policy.

## Decision

`DocumentSession` is the application service for one open document.
It owns a `TextDocument`, an `ITextDocumentStore`, and a nullable destination string.
`CreateUntitled` creates an empty, clean document without a destination.
`LoadAsync` creates a new document with loaded text as its clean baseline.
A load never replaces an existing document's identity.

`ITextDocumentStore` has two operations, `LoadAsync` and `SaveAsync`.
They transfer strings without normalization. The adapter owns encoding,
filesystem operations, and destination interpretation. Empty text is valid.
The application rejects null, empty, or whitespace-only destinations.
It preserves other destination strings exactly. It does not resolve paths,
check file existence, or equate destinations with document identity.

`SaveAsync` requires an existing destination, even when the document is clean.
An untitled document requires `SaveAsAsync`. Both operations persist clean
and dirty documents. Save-as updates the destination only after success.
Edits, saves, and destination changes preserve the document identity.

The service captures `Document.Text` before it calls the port.
A successful save acknowledges that captured string through
`TextDocument.AcceptChanges(string text)`. This replaces the parameterless API.
Current text remains unchanged. Dirty state compares current text with the saved
snapshot using ordinal equality. Reverting to that snapshot clears dirty state.
Reverting to an older baseline can now make the document dirty.
This updates the acknowledgement contract in [ADR 0004](0004-plain-text-document.md).

Port failures propagate as exceptions. Cancellation propagates as
`OperationCanceledException`. Neither result changes the baseline or destination.
The session checks cancellation before and after each port operation.
If an adapter writes data but returns success after cancellation, the session
reports cancellation and preserves its previous baseline and destination.
The external data might already have changed. These contracts do not promise
transactional disk writes or rollback. The future adapter must define those guarantees.

Only one save can remain pending in a session. Another save fails with
`InvalidOperationException` before it calls the port. A `finally` block clears
the pending state after success, failure, or cancellation. Different sessions
can save independently. They do not coordinate writes to the same destination.
The caller must prevent conflicting sessions from writing the same external resource.

Callers must serialize session and document access, including asynchronous
continuations. The service retains the caller's synchronization context.
Edits can occur while persistence is pending. The service does not make
`TextDocument` thread-safe. A future UI can use its dispatcher for this contract.
Rejecting overlapping saves avoids out-of-order baseline and destination updates
without adding queues, revision counters, or background synchronization.

## Architecture checks

`Editor.Application.Tests` references Application and the existing xUnit package.
In-memory fakes test observable load, save, error, cancellation, and pending-save
behavior without disk access. The solution and dependency policy include this project.
The shared verification entry point discovers its unique TRX report.

Metadata checks inspect both Core and Application. Application may reference
only `Editor.Core` and `System.Runtime`. Its namespace allowlist adds
`Editor.Core`, `System.Threading`, and `System.Threading.Tasks` to the Core list
in ADR 0004. Native imports remain prohibited. These checks exclude `System.IO`,
UI frameworks, encoding APIs, and concrete persistence dependencies.
Static metadata checks do not prove semantic ownership or detect dynamic code generation.

## Consequences

Load and save now have executable contracts without a concrete persistence adapter.
There is no disk access, format abstraction, or UI integration in this change.
Adapter implementations must preserve exact text and propagate errors and cancellation.
A failed or cancelled save does not prove that external data remained unchanged.
