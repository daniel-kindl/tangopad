# Plain-text editor verification

Date: 2026-10-17

Change: [issue #19](https://github.com/daniel-kindl/tangopad/issues/19)

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

The first full run failed during reproducibility restore because the temporary
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

Host: Fedora Linux 44 container, x64, X11 display :0 through Xwayland.

The native smoke test launched the built editor, requested native window close,
and observed exit code 0.
A separate native session accepted synthetic X11 text input.
Its title changed from Untitled to Untitled *.
A captured image showed the toolbar, line numbers, and entered text.
The image is a local artifact at artifacts/editor-acceptance/editor.png.

The native Save action entered a pending picker operation.
The desktop picker was outside the windows accessible through this Xwayland bridge.
Native picker selection, cancellation, and overwrite confirmation remain untested here.
The headless control test uses controlled picker responses and real file persistence.
It does not prove native dialog behavior.

Windows and macOS desktop acceptance remains untested locally.
CI repeats the full verification command, including native startup, on all three platforms.
CI startup does not prove each platform's native picker interactions.

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

A failed save can leave complete output if cancellation races with file replacement.
See ADR 0006 for that persistence limit.
