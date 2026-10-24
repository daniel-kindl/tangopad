# Application state and portable mode

Tangopad stores editor preferences and up to ten recent paths in one `state.bin`.
The file contains no document text, credentials, or secrets. Open and Save retain
their existing encoding, dirty-state, and conflict rules.

## Locations and mode selection

| Mode or platform | State file |
| --- | --- |
| Windows default | `%LOCALAPPDATA%\Tangopad\state.bin` |
| Linux default | `$XDG_DATA_HOME/Tangopad/state.bin`, or `~/.local/share/Tangopad/state.bin` |
| macOS default | `~/Library/Application Support/Tangopad/state.bin` |
| Portable | `<distribution>/data/state.bin` |

Linux ignores a relative `XDG_DATA_HOME`. Application state uses the data root.
XDG config, cache, logs, and temporary roots do not hold durable state.
A single-file executable still uses installed mode by default.
Writable application directories do not select portable mode.

Create an empty `tangopad.portable` file beside the executable to select portable
mode. Alternatively, start the executable with `--portable`.
`--installed` overrides the marker. Supplying both switches is an error.
Mode switches are case-sensitive. Tangopad resolves the distribution from
`Environment.ProcessPath`, independently of the working directory or extraction
location of managed assemblies. Start the actual executable rather than a
`dotnet Editor.App.dll` host when selecting portable storage.

For `Tangopad.app/Contents/MacOS/<executable>`, the distribution is the directory
that contains `Tangopad.app`. Put the marker and `data/` beside the bundle.
Tangopad never puts portable state inside that bundle.
Two portable distributions in different directories have independent state.
Installed distributions for the same user share the OS data directory.

Changing modes selects a different file. Tangopad never copies state between modes.
A read-only or unavailable portable directory produces a startup error dialog.
It also writes the error to standard error where available. The application can
continue with in-memory choices, but does not switch to installed storage.
Later write failures retain in-memory choices. Recent Files reports failed writes.
A subsequent change retries an ordinary write failure.

## Binary schema version 1

All integers and the IEEE 754 binary64 font size use little-endian byte order.
The schema has no padding, object types, or executable data.
The same state produces the same bytes.

| Offset | Type | Meaning |
| --- | --- | --- |
| 0 | 8 bytes | ASCII `TANGOST` followed by NUL |
| 8 | uint32 | Schema version, exactly 1 |
| 12 | byte | Word Wrap, 0 or 1 |
| 13 | byte | Line Numbers, 0 or 1 |
| 14 | binary64 | Font size, finite and within 8 through 48 |
| 22 | uint32 | Recent path count, 0 through 10 |
| 26 | Repeated path records | uint32 UTF-8 byte length, then strict UTF-8 bytes |
| After paths | 32 bytes | SHA-256 of all preceding bytes |

Each path has 1 through 4096 UTF-8 bytes. Paths must be absolute and contain no
NUL. The file limit is 65536 bytes, enforced during the read. Extra bytes,
invalid UTF-8, invalid fields, truncation, and checksum failures reject the file.
The checksum detects accidental corruption. It does not authenticate the file.
Paths retain the existing platform normalization and comparison rules.

An invalid or future-version binary file remains unchanged. Tangopad reports
the load error and uses defaults in memory. Writes remain disabled for that
process, so an older executable cannot destroy state from a newer schema.
Future schema changes must define their reader and migration before deployment.

## Writes, migration, and recovery

`AppStateStore` owns the file and serializes preference and history updates.
The preference and history consumers preserve early user changes during loading.
Each write creates a unique `.state-<guid>.tmp` in the state directory.
It writes, flushes, requests a disk flush, closes, and validates that file.
Only then does `File.Move(..., overwrite: true)` replace `state.bin`.
There is no direct truncate, delete, or copy fallback.
Unix temporary files allow owner read and write only. Windows inherits directory permissions.

On local filesystems with atomic replacement, readers see the previous complete
file or the new complete file. Failed writes before replacement preserve the
previous file. Filesystem and hardware guarantees still limit power-loss durability.
Tangopad does not synchronize directory metadata or promise recovery after disk failure.
Abandoned temporary files are ignored. Cleanup removes this process's temporary
file where access permits. Separate processes use the last successful replacement.
There is no cross-process merge. Shutdown waits up to 250 milliseconds for writes.

When `state.bin` is absent, startup reads `editor-preferences.json` and
`recent-files.json` from the selected state directory. It validates their version 1
schemas and original size limits, then writes and validates the binary state.
The original JSON files remain unchanged, including after success.
Once binary state exists, startup ignores the JSON files. Clear Recent Files
therefore cannot restore legacy paths on a later launch.
Invalid or inaccessible legacy input blocks migration and subsequent writes for
that process. Tangopad reports the error and leaves the legacy data intact.
A failed binary write leaves the legacy data available for a later launch.
Portable mode never imports installed-mode JSON.

To back up or restore state:

1. Close every Tangopad process that uses the state directory.
2. Copy `state.bin` to a backup location, or copy a known valid backup into the state directory.
3. Start Tangopad in the same mode.

To recover from invalid or unsupported state, close Tangopad and move `state.bin`
to a backup location. Restore a compatible backup before restarting.
To reset all state, move both the binary file and legacy JSON files out of the
state directory. Removing only `state.bin` can import retained legacy JSON again.
Clear Recent Files does not erase retained JSON or external backups.
Remove retained legacy files manually after verifying migration if that history
must no longer remain in the state directory.

## Packaging, installation, and updates

Installation and application removal must preserve per-user state by default.
Deleting a portable distribution must be a separate choice from deleting its
`data/`. Mode changes do not migrate state automatically.

Updater #45 must replace application files only. It must preserve portable
`data/` and `tangopad.portable`, as well as installed-mode user data.
An update archive must exclude `state.bin`, legacy user JSON, and user `data/`.
For macOS, replace the application bundle while preserving its sibling data directory.
Never replace the enclosing portable distribution directory wholesale.

Use self-contained single-file publishing where supported by the target runtime.
Keep native libraries and required license notices in the distribution as needed.
Use an application bundle on macOS. These storage rules apply to both ordinary
build output and published distributions. This issue does not implement updater #45.

## Verification

Infrastructure tests check schema fields, corruption, truncation, size limits,
shared writes, protected future versions, migration, failed writes, and mode rules.
Headless control tests check restored preferences and early changes.
`python3 eng/check-native-editor.py` checks native preference restoration in both
modes after simulated executable replacement. It also checks installed-state
isolation and document bytes. The native script requires its documented Linux
X11 dependencies. The hosted verification workflow runs on all three operating systems.

On 2026-10-23, native acceptance passed on Fedora Linux 44 under Xvfb.
Both storage modes restored Word Wrap, Line Numbers, font size, and recent-file
reopening after executable replacement. Portable mode preserved installed state.
Evidence is in `artifacts/native-editor-tn5o5g61/result.json`.
Windows and macOS native interaction and published single-file packages remain
untested locally.

The same day's `dotnet msbuild eng/verify.proj` run passed on Linux with zero
diagnostics. Eight TRX reports contained 661 passing tests and one skipped Windows
sharing test. Hosted Windows and macOS verification remains pending.
