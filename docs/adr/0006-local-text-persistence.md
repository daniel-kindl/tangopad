# ADR 0006: Persist strict UTF-8 through same-directory replacement

Status: Accepted

Date: 2026-10-17

Concurrency and persistence contracts are updated by [ADR 0009](0009-optimistic-file-concurrency.md).

## Context

[Issue #17](https://github.com/daniel-kindl/tangopad/issues/17) adds a local
implementation of `ITextDocumentStore`. The port transfers strings and has no
encoding or BOM metadata. Saving directly to an existing file could destroy its
previous contents before encoding, cancellation, or an I/O error interrupts the save.

## Decision

`Editor.Infrastructure` owns `FileTextDocumentStore`. It references Application
only. Core and Application retain their existing contracts and dependency limits.
The adapter has no UI integration and needs no service container or new port.

Destinations are filesystem paths. Relative paths use the process working
directory at operation entry. The adapter resolves each path once. It does not
create parent directories. Callers must use ordinary local files in directories
they trust. Device paths, network filesystems, and hostile directory mutation
are outside the initial guarantees.

### Encoding

Loads decode strict UTF-8. One initial UTF-8 BOM is an encoding signature and
does not become document text. A second BOM or an embedded U+FEFF remains text.
Automatic detection of UTF-16 and UTF-32 is disabled. Malformed UTF-8 throws
`DecoderFallbackException`. Unsupported encodings that happen to contain valid
UTF-8 bytes cannot be distinguished from UTF-8.

Saves encode strict UTF-8 without an added BOM. Unpaired UTF-16 surrogates throw
`EncoderFallbackException`. Unicode, whitespace, NUL characters, and line endings
undergo no normalization. Empty text produces an empty file.

The port cannot preserve the original encoding or BOM. A BOM-bearing input loses
its signature on save. Text that starts with U+FEFF produces the same bytes as a
BOM, so a later load consumes that first character as a signature. Exact byte
preservation and that leading-character ambiguity need a future metadata contract.

### Save replacement and cancellation

Each save creates a unique `.tangopad-<guid>.tmp` file in the destination directory
with `FileMode.CreateNew` and exclusive sharing. Unix creation permissions allow
only owner read and write, subject to the process umask. Windows uses inherited
directory permissions. The adapter writes asynchronously, flushes, and closes
the temporary file before replacement. No operation truncates the destination.

The commit is `File.Move(temporary, destination, overwrite: true)`. The same
directory keeps both paths on the same filesystem for ordinary local files.
The [.NET Unix implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Unix.cs)
uses `rename` for overwrite. The
[Windows implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Windows.cs)
uses `MoveFile`. On local filesystems that support atomic replacement, readers
see either the old file or the complete new file. Filesystem and OS behavior
limit this guarantee. The adapter provides no copy or delete fallback of its own.
The .NET Unix cross-device fallback can copy, so unusual mounts do not carry
the same guarantee. Windows sharing restrictions can prevent replacement.

The adapter checks cancellation before access and before commit. Reads, writes,
and flushes receive the caller's token. The final move is synchronous and cannot
be cancelled. Cancellation after the last check can race with commit.
`DocumentSession` can then report cancellation without acknowledgement even
though the destination contains the complete new text, as specified in ADR 0005.

Before commit, a failure or observed cancellation leaves the previous file
unchanged and removes the temporary file after closing its handles. Original
exceptions propagate. If cleanup also fails, an `AggregateException` retains
both errors. Process termination, power loss, or loss of directory access can
leave a temporary file. There is no automatic recovery scan.

Replacement creates a new filesystem object. It does not preserve the old
file's permissions, ACLs, timestamps, alternate streams, or hard-link identity.
On Unix, directory permissions govern replacement even if the old file is
read-only. A final symbolic link is replaced rather than its target being saved.
Loads follow symbolic links. Callers must account for this difference.
Flush completion does not promise stable storage after power loss.
No file or directory synchronization for crash durability is provided.

Different sessions and processes do not coordinate writes. The last successful
replacement wins. There is no external-change detection or lock across sessions.

## Verification

`Editor.Infrastructure.Tests` uses isolated temporary directories and real files.
It checks exact text and output bytes, BOM policy, invalid input, replacement,
missing paths, failed replacement, partial encoding failure, cancellation,
temporary-file cleanup, and retries. Application-boundary checks require failed
or cancelled save-as to preserve the previous destination and baseline.

The solution and dependency policy include the adapter and integration tests.
Architecture checks retain strict Core and Application API allowlists and
exclude infrastructure from their dependencies. Type definitions must use the
owning module's namespace or its child namespaces. This also rejects adapter
namespaces declared inside an inner assembly. The common verification command
discovers the integration tests on Linux, Windows, and macOS.

## Alternatives

Direct overwrite is smaller but can truncate a valid destination before failure.
Automatic legacy-encoding detection can accept corrupt input as valid text.
`File.Replace` can preserve some metadata on Windows, but requires an existing
destination and a separate creation path. Same-directory move provides one
commit operation for new files and existing files with explicit metadata limits.
