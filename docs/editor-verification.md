# Plain-text editor verification

Date: 2026-10-17

Changes: [issue #19](https://github.com/daniel-kindl/tangopad/issues/19) and
[native acceptance issue #21](https://github.com/daniel-kindl/tangopad/issues/21).

## Repository verification

The full command passed on this host:

```sh
TMPDIR=/workspace/tangopad/artifacts/tmp dotnet msbuild eng/verify.proj
```

Locked restore and audit, formatting, analyzers, and Release compilation passed
with zero warnings or errors. Six distinct TRX reports contain 85 passed tests
and one Windows-only skip. Native editor startup passed.
Two isolated builds used fresh NuGet caches.
All 22 managed DLL and portable PDB files had identical SHA-256 hashes.

The issue #19 verification first failed during reproducibility restore because the temporary
filesystem exceeded its quota. The successful rerun used the workspace disk
for temporary files. It used the same verification entry point and checks.
Reports and reproducibility manifests are in artifacts/test-results and
artifacts/reproducibility.

## Automated evidence

The first test run failed because the presentation types did not exist.
After implementation, the workspace tests passed.
A control test then exposed an input-focus failure.
The window now focuses AvaloniaEdit's TextArea when it opens.

Editor.App.Tests checks these observable behaviors:

- Edits made during Save remain dirty and prevent New after a save confirmation.
- Cancelled pickers, failed Open, and cancelled pending Open preserve the document.
- Cancelled confirmation and cancelled Save prevent close.
- Busy commands cannot replace a document or overlap a pending Save.
- Failed Save preserves the destination and dirty baseline and displays the error.
- Successful Save before close permits close. Later Save uses the saved destination.
- The actual AvaloniaEdit control accepts text input and preserves mixed line endings.
- The Save As shortcut writes exact text through FileTextDocumentStore.
- New and Open clear undo history. Undo updates the document and dirty title.
- Window close preserves unsaved text after Cancel and closes after Discard.

The existing application and filesystem tests continue to check captured saves,
strict UTF-8, BOM rules, replacement errors, cancellation, and cleanup.
The existing Windows-only filesystem test remains untested on Linux.

## Native desktop evidence

The first integration check used Fedora Linux 44, x64, and XWayland `:0`.
Native startup, synthetic typing, and the dirty title passed.
The desktop picker was outside the X11 input connection.
Picker completion on that desktop remains NOT TESTED.

| Host and session | Result | Follow-up |
| --- | --- | --- |
| Fedora Linux 44 container, x64, .NET 10.0.13, Xvfb `:1`, private D-Bus, GTK portal | PASS. All 11 native acceptance groups passed. | Repeat on the hosted Ubuntu runner. |
| Fedora Linux 44 container, XWayland `:0` on a Wayland desktop | PASS for startup, typing, and dirty title. Native picker completion is NOT TESTED. | Repeat the interactive procedure with input access to the desktop picker. |
| Windows desktop | NOT TESTED. This host cannot run a Windows desktop session. | Run the interactive procedure on Windows. Check Ctrl shortcuts and record OS, session, and results. |
| macOS desktop | NOT TESTED. This host cannot run a macOS desktop session. | Run the interactive procedure on macOS. Check Command shortcuts and record OS, session, and results. |

The Fedora Xvfb run used GTK 3.24.52, portal 1.22.1, and GTK portal backend 1.15.3.
Two consecutive native runs passed. Local evidence is in
`artifacts/native-editor-jqai_48f` and `artifacts/native-editor-_sobywq6`.
The `result.json` report lists these completed groups:

- Native typing and dirty title.
- Open, Save, and Save As picker cancellation with unchanged document state.
- Save and Save As through usable local paths, with exact UTF-8 output.
- Native overwrite prompt.
- Overwrite cancellation and confirmed replacement of a test file.
- Open and Save As with Unicode, spaces in paths, and CRLF, LF, and CR newlines.
- New with Cancel, Save, and Discard.
- Invalid UTF-8 error with the unsaved document preserved.
- Permission error with the old file and dirty state preserved, followed by a successful Save As.
- Close with Cancel, a cancelled Save picker, and successful Save.
- Close with Discard.

The script invokes Ctrl+N, Ctrl+O, Ctrl+S, and Ctrl+Shift+S through X11 input.
The output assertions include text typed after cancelled dialogs and failed operations.
No application defect required a production change in this acceptance run.
Slow file I/O and edits during pending I/O retain their existing automated evidence.
Those interactions remain NOT TESTED in native desktop windows.
The interactive procedure below supplies their follow-up.

The native acceptance script starts the unchanged Release executable on Xvfb.
It starts a private D-Bus session and the real GTK file-picker portal.
X11 input drives the editor and dialogs. No dialog responses are mocked.
The script checks exact output bytes and window titles.
Screenshots show the prompts and error messages.
It creates all test files in a private temporary directory.
The script removes that directory after the run.
It does not select existing user files.

[eng/check-native-editor.py](../eng/check-native-editor.py) is separate from the
headless control tests and native startup test.
The Linux hosted Verify job runs this script after repository verification.
That job uploads screenshots, process logs, and `result.json` as `native-editor-ubuntu-24.04`.
The artifact retention is seven days.

## Repeat the Linux native check

Use a non-root account on 64-bit Linux. Build the Release executable first.
The check requires Python 3 with Pillow, `xdotool`, `Xvfb`, `dbus-daemon`,
`xdg-desktop-portal`, and `xdg-desktop-portal-gtk`.
The portal executables must be in `/usr/libexec`.
The GTK dialog text must use English. The script sets the test locale.

On Ubuntu, install the packages with this command:

```sh
sudo apt-get install --no-install-recommends python3-pil xdotool xvfb dbus-daemon xdg-desktop-portal xdg-desktop-portal-gtk
```

Run these commands from the repository root:

```sh
dotnet build Tangopad.slnx --configuration Release
python3 eng/check-native-editor.py
```

A successful run prints `PASS` for each completed group.
A failure exits with a nonzero code and captures `failure.png`.
The final line identifies the evidence directory under `artifacts/native-editor-*`.
The check uses a separate X server. It does not send input to your desktop.
It uses GTK 3 dialog controls and fixed positions in the app's confirmation dialog.
A dialog layout change can require a script update.
This check does not prove behavior through a Wayland portal, a window manager,
a physical keyboard, or another operating system.

## Desktop acceptance procedure

Perform these checks in an interactive desktop session on each supported platform.

1. Start the app with the README command.
2. Type text in the untitled document. Check the asterisk in the title.
3. Use Save. Cancel the picker. Check that the document remains dirty.
4. Use Save again. Select a new path. Check the file contents and clean title.
5. Edit the text. Use Save As with another path. Check both files.
6. Use Open. Select a UTF-8 file with mixed line endings. Check its exact saved output.
7. Edit the text. Use New. Select Cancel. Check that the edits remain.
8. Repeat New with Save, then with Discard. Check the resulting document.
9. Edit the text. Close the window. Check Cancel, Save, and Discard separately.
10. Open invalid UTF-8. Check the error dialog and the unchanged document.
11. Attempt a save to an inaccessible destination. Check the error and dirty state.
12. Check the shortcuts in README, including Save As.
13. During a slow save, make another edit. Check that the edit remains dirty.
14. During a slow load, request cancellation. Check that the original document remains.
15. Use Save As with an existing test file. Cancel overwrite. Check the file and unsaved edits.
16. Repeat Save As. Confirm overwrite. Check the new bytes and clean title.
17. Cancel Open and Save As separately. Check that the text, destination, and title remain unchanged.

Create a temporary directory for the procedure. Use only files in that directory.
Record the OS version, architecture, runtime, display session, and result for each step.
For a permissions check, save a test file first. Remove write access from its directory.
Attempt Save after another edit. Restore access after the check.

A failed save can leave complete output if cancellation races with file replacement.
See ADR 0006 for that persistence limit.
