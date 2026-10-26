# Troubleshooting

## A file will not open or save

Read the **File operation failed** message before retrying.
Check that the file and parent directory still exist and your account can access them.
Close other programs that can hold the file open, particularly on Windows.
Use **Save As** to a writable local directory to preserve the current text.
Do not close the editor until important edits are saved.

Invalid UTF-8 files cannot be opened. Convert a copy to UTF-8 with a tool that supports the original encoding.
Keep the original until you check the converted text.
There is no encoding selector or automatic legacy-encoding detection.

**Cancel operation** requests cancellation of pending file I/O.
If a native file picker is open, close that picker to finish cancellation.
Cancellation near the final replacement can occur after bytes reach disk.
If the result is uncertain, compare a separate disk copy before discarding the open text.

## The file changed on disk

An external-change notification does not replace your open text.
**Dismiss** hides that notification for the observed revision. It does not authorize an overwrite.
Save compares the file with the last successful load or save and can show **File changed on disk**.

1. Select **Cancel** if you need to keep both versions.
2. Use **Save As** with a new filename to preserve your open text.
3. Open the original file separately to inspect its disk contents.

**Reload from Disk** loads disk text through the unsaved-change confirmation.
**Overwrite anyway** explicitly permits replacement or recreation of the destination.
Choose it only when your open text is the version you want to keep.
These checks reduce conflicts. They do not lock the file against every concurrent writer.

## Unsaved text disappeared after a crash

Tangopad has no autosave, crash-recovery scan, or persistent session buffer.
Preferences and recent paths do not contain document text.
Reopen the last saved file or restore your own backup.
Abandoned temporary files are not a supported recovery mechanism.
Save regularly and use external backups for important documents.

## Preferences or recent files do not persist

Check whether you started in installed or portable mode.
Each mode uses a different [state location](../app-state.md#locations-and-mode-selection).
Portable state requires a writable distribution directory.
An invalid or newer state file produces an error and disables writes for that process.
The editor can continue with defaults in memory.

1. Close all Tangopad processes that use the state directory.
2. Back up `state.bin` and any retained legacy JSON files.
3. Restore a compatible backup, or move those state files outside the directory to reset preferences and history.
4. Restart in the same mode.

Do not edit the binary state file manually.
Removing only `state.bin` can import retained legacy JSON again.
Clearing Recent Files does not erase old JSON, backups, or document files.

## Preview is missing or incomplete

Save the file as `.md`, `.markdown`, or `.textile`, then select **Show Preview**.
A format transition hides preview, even when another supported format becomes active.
Large input or complex markup can exceed preview limits. Source remains editable and saveable.
For **Preview unavailable**, edit the source or hide and show preview to retry.
Read the [format limits](editor.md#markup-and-source-highlighting) before reporting an unsupported form as a defect.

## Update or launch failed

An offline or failed update check does not prevent editing. Retry when the connection is available.
A development build may have no production feed or published release.
Do not replace files in a package-managed or protected installation through the application.
Use its distribution's supported update method.
After a failed native replacement, retain the existing installation and state backups.
Report the version, package source, OS message, and whether the old version can still start.
Do not delete portable `data/` while trying another package.

## Known limitations

- One document per window. There are no tabs, exports, autosave, or crash recovery of unsaved text.
- Local UTF-8 files only. File replacement does not retain all filesystem metadata.
- Preview is bounded and read-only. Document links and images do not load external resources.
- Linux uses X11 or XWayland. Native Wayland file drops need separate acceptance.
- The Linux source editor lacks an AT-SPI Text interface in the recorded audit. Screen-reader access to source text is blocked.
- OS high-contrast behavior, physical monitor transitions, and native screen-reader workflows still need platform checks.
- Candidate formats do not establish minimum OS versions or successful production installation on every platform.

The [accessibility record](../ux/47-acceptance.md) and [release requirements](../release-distribution.md#required-native-evidence)
contain the detailed acceptance gaps.

## Report a problem

Use [GitHub Issues](https://github.com/daniel-kindl/tangopad/issues/new/choose).
Include the version from **Help > About Tangopad**, OS and architecture, package source,
storage mode, reproduction steps, expected result, and actual result.
Add a small synthetic file if the problem depends on content.
Remove private paths, document text, and credentials from attachments.
Use the [security reporting route](../../README.md#build-and-contribute) for a suspected vulnerability.
