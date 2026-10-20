# Plain-text editor verification

Date: 2026-10-17

Changes: [issue #19](https://github.com/daniel-kindl/tangopad/issues/19),
[native acceptance issue #21](https://github.com/daniel-kindl/tangopad/issues/21),
[compiled AXAML issue #22](https://github.com/daniel-kindl/tangopad/issues/22),
[MVVM issue #23](https://github.com/daniel-kindl/tangopad/issues/23),
[semantic themes issue #24](https://github.com/daniel-kindl/tangopad/issues/24),
and [resource convention issue #27](https://github.com/daniel-kindl/tangopad/issues/27).

## Font size verification

Date: 2026-10-18. [Issue #41](https://github.com/daniel-kindl/tangopad/issues/41).

`dotnet msbuild eng/verify.proj` passed on Linux with zero Debug and Release build diagnostics.
All six TRX reports passed validation. Tests passed 167 cases and skipped one Windows-only filesystem case.
Native startup passed.

The seven shortcut cases first failed because the keys were unhandled.
The ViewModel test first failed to compile because font-size state and commands were absent.
All 10 focused cases now pass.
They cover the default, step, bounds, reset, notifications, platform modifier, Shift, numpad keys, and focus restrictions.
Headless tests cover compiled bindings, visible line-height growth, both themes, caret, selection, scrolling,
Word Wrap, document identity, dirty state, undo and redo, exact save bytes, New, Open, and a new window's default.

`python3 eng/check-native-editor.py` passed all 21 native acceptance groups on Fedora Linux 44,
x64, .NET 10.0.13, with Xvfb, a private D-Bus session, and the GTK portal.
Local evidence is in `artifacts/native-editor-8y5987w_`.
Inspected screenshots show increased and decreased font sizes, the reset baseline, and retained size after New and Open.
Exact-byte and dirty-title checks passed with the existing native editor workflows.
Windows and macOS native interaction remain untested on this host.
Hosted matrix checks must pass on the pull request before a normal merge.

## Word Wrap verification

Date: 2026-10-18. [Issue #39](https://github.com/daniel-kindl/tangopad/issues/39).

`dotnet msbuild eng/verify.proj` passed on Linux. Locked restore and audit,
formatting, analyzers, Debug and Release builds, tests, and all six TRX report checks passed.
Both builds reported zero warnings and errors.

The focused headless tests first failed because the Word Wrap checkbox was absent.
The ViewModel test and both headless theme cases pass with the compiled bindings.
The cases cover long Unicode lines, mixed newlines, horizontal scrolling, selection,
caret positions, dirty state, identity, undo and redo, exact UTF-8 save bytes,
theme changes, and New and Open.

`python3 eng/check-native-editor.py` passed all 16 native acceptance groups on
Fedora Linux 44, x64, .NET 10.0.13, with Xvfb, a private D-Bus session, and the GTK portal.
Local evidence is in `artifacts/native-editor-0uq0ggcx`.
Screenshots show a wrapped Unicode line, wrapping disabled after an edit, and
wrapping retained after New and Open. Output checks confirm exact bytes after undo,
redo, and Save. Existing native shortcut, search, picker, and close checks pass.
Windows and macOS native interaction remain untested on this host.

## Folder refactor verification

[Issue #30](https://github.com/daniel-kindl/tangopad/issues/30) reorganizes
presentation files, including the merged find/replace implementation.
`dotnet msbuild eng/verify.proj` passed on Linux with zero build warnings or errors.
All six TRX reports passed validation. Tests passed 99 cases and skipped one
Windows-only filesystem case. Native startup and headless compiled-view, binding,
dialog, theme, and search checks passed.

`python3 eng/check-native-editor.py` passed all 13 native acceptance groups.
Local evidence is in `artifacts/native-editor-_r4oh8d0`.
The move comparison found only namespace, import, and AXAML type-reference changes
in the 17 moved files. Public production type identities and dependencies remain
unchanged. Windows and macOS native interaction remain untested on this host.
Hosted matrix checks must pass on the pull request before a normal merge.

## Repository verification

The latest PR #26 run used this command:

```sh
dotnet msbuild eng/verify.proj
```

Locked restore and audit, formatting, analyzers, and Release compilation passed
with zero warnings or errors. Six distinct TRX reports contain 82 passed tests
and one Windows-only skip. Native editor startup passed.
The project owner removed the managed reproducibility requirement in this combined change.
The comparison tool, its tests, CI uploads, and fixed test runners are deleted.
[ADR 0002](adr/0002-reproducible-verification.md) records the current policy.
TRX reports remain in artifacts/test-results.

## Compiled AXAML views

[Issue #22](https://github.com/daniel-kindl/tangopad/issues/22) moved the editor
views into compiled AXAML. The repository command above includes that change.

Editor.App.Tests passed 13 tests.
Eight ViewModel tests run without initializing Avalonia.
Five headless tests check compiled views, editor behavior, command bindings, dialogs, and theme switching.
Architecture tests also enforce the AXAML resource convention and unique resource keys.
[Presentation resources](presentation-resources.md) records the ownership and resource exceptions.
One test checks the window size, title, named toolbar buttons, editor control,
shortcuts, and Fluent theme resources.
One test checks the unsaved-changes dialog and the error dialog.
The unsaved-changes dialog stays 460 pixels wide.
Its buttons stay Save, Discard, and Cancel, in that order.
Save returns `UnsavedDecision.Save`.
The error dialog returns `UnsavedDecision.Cancel`.

This host ran the Linux native startup smoke test.
After the combined presentation refactor, `eng/check-native-editor.py` passed all 11 groups.
The evidence directory is `artifacts/native-editor-yan2r48e`.
Windows and macOS interactive desktop checks remain NOT TESTED.
The Windows-only filesystem test skipped on Linux.

## Automated evidence

The first test run failed because the presentation types did not exist.
After implementation, the presentation tests passed.
A control test then exposed an input-focus failure.
The window now focuses AvaloniaEdit's TextArea when it opens.

Editor.App.Tests checks these observable behaviors:

- Edits made during Save remain dirty and prevent New after a save confirmation.
- Cancelled pickers, failed Open, and cancelled pending Open preserve the document.
- Cancelled confirmation and cancelled Save prevent close.
- Busy commands cannot replace a document or overlap a pending Save.
- Property and command notifications expose busy state, cancellation, dirty state, and title changes.
- Compiled bindings disable replacement and editing during Open without resetting the buffer.
- Existing windows and error dialogs update semantic brush resources when the theme changes.
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
| Fedora Linux 44 container, x64, .NET 10.0.13, Xvfb `:1`, private D-Bus, GTK portal | PASS. All 11 groups passed after the combined MVVM and resource refactor. | Repeat on the hosted Ubuntu runner. |
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

## Go To Line verification

Date: 2026-10-18. [Issue #43](https://github.com/daniel-kindl/tangopad/issues/43).

`dotnet msbuild eng/verify.proj` passed on Linux. Debug and Release builds reported
zero warnings and errors. All six TRX reports passed validation.
Tests passed 170 cases and skipped one Windows-only filesystem case.

The two initial headless cases failed because the shortcut did not open a dialog.
All three Go To Line cases now pass. They cover mixed newlines, wrapping, empty
and trailing lines, clamping, validation, focus, cancellation, selection, scrolling,
status updates, document identity, dirty state, and undo/redo.

`python3 eng/check-native-editor.py` passed all 24 groups on Fedora Linux 44,
x64, .NET 10.0.13, with Xvfb, a private D-Bus session, and the GTK portal.
Local evidence is in `artifacts/native-editor-9798bode`.
The inspected screenshot shows the invalid input message and retained editor selection.
Windows and macOS native interaction remain untested on this host.

### Desktop acceptance

Use Ctrl+G on Windows/Linux or Command+G on macOS with editor focus.
The dialog selects the current logical line number.

1. Open a UTF-8 file with CRLF, LF, and CR line endings.
2. Select text. Open Go To Line. Enter `0`, a negative number, or nonnumeric text.
3. Confirm the input. Check the error message and unchanged caret and selection.
4. Cancel with Escape. Check editor focus and unchanged selection.
5. Repeat with Cancel and the window close button.
6. Enter a valid line. Check column 1, editor focus, and the visible target line.
7. Enter a number beyond the last line. Check column 1 of the last logical line.
8. Repeat with Word Wrap enabled and a long first line.
9. Repeat on an empty document and a document with a trailing newline.
10. Save without edits. Compare the output bytes with the original file.
11. Make an edit before navigation. Check that Undo and Redo still change that edit.
12. Check Find/Replace, Word Wrap, and font shortcuts after navigation.

The headless tests cover validation, focus, selection, logical positions, scrolling,
identity, dirty state, and undo history. The Linux script checks navigation through
saved insertion offsets, mixed line endings, clamping, invalid input, cancellation,
and exact bytes with wrapping enabled. Native Windows and macOS interaction requires
this procedure. Hosted CI runs the headless tests and native startup on all three platforms.

## Markdown preview verification

Date: 2026-10-18. [Issue #53](https://github.com/daniel-kindl/tangopad/issues/53).

`python3 eng/check-native-editor.py` passed all 29 groups on Fedora Linux 44,
x64, with Xvfb, a private D-Bus session, and the GTK portal.
Local evidence is in `artifacts/native-editor-nfdwx8f7`.
The preview screenshot shows rendered headings, emphasis, lists, code, inactive
link labels, image descriptions, and literal script text.
A local listening socket detected no image request. An inotify monitor detected
no open or read of the referenced local SVG while preview rendered.
Native editing, live updates, exact saved bytes, Find, undo, Hide Preview,
Save As to plain text, Word Wrap, font shortcuts, Go To Line, and close all passed.

The initial focus regression test failed because the preview button retained focus.
It passes after the toggle returns focus to the editor.
Four framework-independent parser tests cover the baseline, hostile text,
malformed text, and input, nesting, block, and run limits.
Nine headless preview cases cover toggles, save success/failure/cancellation,
format and theme transitions, New/Open, serial background work, debounce,
stale results, close cancellation, offline rendering, exact bytes, and undo/dirty state.

Native Windows and macOS interaction remains untested on this Linux host.
Hosted CI repeats the shared verification command and native startup on all three
platforms. For native desktop acceptance, repeat the editor procedure above with
Show Preview enabled. Check that links remain inactive, images remain descriptions,
and Save As to plain text hides the preview without interrupting source editing.
[ADR 0008](adr/0008-safe-markdown-preview.md) records renderer limits and deferred behavior.

## Character count verification

Date: 2026-10-18. [Issue #57](https://github.com/daniel-kindl/tangopad/issues/57).

`dotnet msbuild eng/verify.proj` passed on Linux. Locked restore and audit,
formatting, analyzers, and Debug and Release builds passed with zero diagnostics.
Tests passed 221 cases and skipped one Windows-only filesystem case.
All seven TRX reports passed validation. Native startup passed.

`Editor.App/ViewModels/EditorViewModel.cs` owns the cached scalar count in the
`Editor.App.ViewModels` namespace. `Views/EditorWindow.axaml` binds the status
through compiled bindings. `Resources/Text.axaml` owns the label format.
The view reuses the existing status style and spacing token.
No module dependency, namespace, or public API changes are required.

The first headless case failed because the character status control was absent.
All 13 focused cases pass. They cover empty and ASCII text, supplementary Unicode,
combining marks, CRLF, mixed newlines, count notifications, both themes, typing,
caret status, selection, Word Wrap, font size, Find/Replace, undo/redo, identity,
dirty state, exact UTF-8 save bytes, New, and Open.

The count enumerates Unicode scalars once per changed text value without a
collection allocation. Caret and selection changes do not invoke the count.
The Release profile alternates two preallocated strings for 100 text updates
after 20 warmup updates. It measures the ViewModel setter, including document
synchronization and count notifications. It excludes control rendering and string
creation. The input repeats ASCII, a supplementary character, a combining mark,
and CRLF. Timing is evidence from this host, not a CI performance threshold.

| UTF-16 code units | Mean time per text update | Allocated bytes per text update |
| --- | --- | --- |
| 1,000,000 | 2.052 ms | 0 |
| 10,000,000 | 21.781 ms | 0 |

The corresponding 100,000 caret changes took 8.690 ms and 0.473 ms.
JIT warmup and concurrent host activity can affect these measurements.
Repeat the profile from the repository root:

```sh
dotnet test --project tests/Editor.App.Tests --configuration Release -- --filter-method '*DocumentCountsLargeInputs' --show-live-output on
```

`python3 eng/check-native-editor.py` passed all 29 groups on Fedora Linux 44,
x64, with Xvfb, a private D-Bus session, and the GTK portal.
Local evidence is in `artifacts/native-editor-pndqzkwh`.
The inspected Unicode fixture screenshot shows `Characters: 29` beside the caret
status. The fixture includes a supplementary character and mixed line endings.
Native editing, Find/Replace, font size, Word Wrap, Markdown preview, Go To Line,
file operations, exact saved bytes, and close checks pass.

Windows and macOS native interaction remains untested on this Linux host.
Hosted Linux, Windows, and macOS checks must pass before a normal merge.

## Word count, issue #59

`Editor.App/ViewModels/EditorViewModel.cs` owns the cached `WordCount` property in
`Editor.App.ViewModels`. Its text-change handler counts characters and words in one
Unicode-scalar loop. `Views/EditorWindow.axaml` uses a compiled binding and the
shared `Text.Status.WordCountFormat` resource in `Resources/Text.axaml`.
The status label reuses the current style and spacing. No module, dependency,
namespace, public API, or persistence change is required.

A word is a maximal sequence of non-whitespace Unicode scalar values.
Unicode whitespace separates words, including tabs, CRLF, LF, CR, non-breaking
spaces, and Unicode line separators. Empty and whitespace-only text has zero
words. The policy applies to plain text and Markdown source. Markdown punctuation
remains part of the count. The count uses no linguistic or rendered-Markdown
segmentation. The character-count policy remains unchanged. Counting does not
normalize or replace text, including unpaired UTF-16 surrogates.

The first headless test failed because `WordStatus` was absent.
Table-driven ViewModel tests cover the word policy and existing character counts.
Notification tests cover equal-length edits that change word boundaries, edits
that preserve counts, caret movement, New, and Open. Headless tests exercise both
themes, typing, clipboard paste, Find/Replace, undo, redo, New, and Open.
They check selection, caret, document identity, dirty state, undo history, and
exact UTF-8 save bytes. Selection, scrolling, font size, Word Wrap, and a live
theme change send no text or count notifications. Source inspection confirms
that only the text-change handler performs the recount.

The isolated Release profile uses the existing large-input test, now with word
assertions. It alternates preallocated text and text plus `!` for 100 updates
after 20 warmup updates. Each token contains ASCII, a supplementary character,
a combining mark, and CRLF. The measurements include document synchronization
and ViewModel count updates. They exclude string creation, AvaloniaEdit buffer
updates, layout, highlighting, and Markdown preview. No timing threshold applies.

| UTF-16 code units | Mean time per text update | Allocated bytes per text update |
| --- | --- | --- |
| 1,000,000 | 2.014 ms | 0 |
| 10,000,000 | 20.367 ms | 0 |

The corresponding 100,000 caret changes took 0.222 ms and 6.259 ms.
The count performs linear work on the UI thread. At ten million code units,
counting alone exceeds a 16.7 ms frame interval on this host. Large edits can
therefore delay input or rendering. The smaller fixture takes about 2 ms for
counting and ViewModel synchronization. These results do not measure complete
keystroke latency. Keep the single correct pass for this version. Reassess
scheduling or incremental counts if measured document workloads require them.

After a Release build, repeat the isolated profile from the repository root:

```sh
dotnet tests/Editor.App.Tests/bin/Release/net10.0/Editor.App.Tests.dll --filter-method '*DocumentCountsLargeInputs' --output Detailed --show-live-output on
```

`dotnet msbuild eng/verify.proj` passed on Fedora Linux 44, x64, with SDK 10.0.402.
Locked restore, formatting, analyzers, and Debug and Release builds passed with
zero diagnostics. Tests passed 234 cases and skipped one Windows-only filesystem
case. All seven TRX reports passed validation. Native startup passed.
`python3 eng/check-native-editor.py` passed all 29 native workflow groups with
Xvfb, a private D-Bus session, and the GTK portal. Local evidence is in
`artifacts/native-editor-durhz178`. The inspected Unicode fixture screenshot
shows `Characters: 29` and `Words: 6` beside the caret status.

Windows and macOS native interaction remains untested on this Linux host.
Hosted Linux, Windows, and macOS verification must pass before a normal merge.

## Reload from Disk, issue #67

`Editor.App/ViewModels/EditorViewModel.cs` owns the command in `Editor.App.ViewModels`.
`Views/EditorWindow.axaml` binds the toolbar button through the shared label in
`Resources/Text.axaml`. Window code owns the platform shortcut and tooltip.
Tests stay in `Editor.App.Tests/ViewModels` and `Editor.App.Tests/Views`.
No dependency, namespace, or public API changes are required.
[ADR 0007](adr/0007-plain-text-editor.md) records the document identity decision.

The untitled-availability test fails against `dev` commit `4768416` because `ReloadButton` is absent.
The same test passes with the implementation. All 15 focused reload cases pass.
They cover clean and externally modified text,
Save, Discard, Cancel, pending-save edits, failed loads, cancellation, reentry,
untitled availability, exact Unicode and newlines, format, Markdown highlighting
and preview, counts, line-ending status, selection, caret, undo, redo, and persisted history.
The existing Application and filesystem tests verify the reused load and strict UTF-8 contracts.
The headless shortcut cases include unchanged disk text, which must still reset editing state.

Repeat the focused tests from the repository root:

```sh
dotnet test --project tests/Editor.App.Tests -- --filter-method '*Reload*'
```

`eng/check-native-editor.py` includes external Unicode edits with mixed newlines,
Cancel with retained text and undo, and Discard with reset undo.
Each native check verifies saved bytes through the production editor.
Windows and macOS native interaction remains untested on this Linux host.
Hosted Linux, Windows, and macOS verification must pass before a normal merge.

`dotnet msbuild eng/verify.proj` passed on Linux with SDK 10.0.402.
Locked restore, audit, formatting, analyzers, and Debug and Release builds passed
with zero diagnostics. Tests passed 300 cases and skipped one Windows-only filesystem
case. All seven TRX reports passed validation. Native startup passed.

The first native F5 attempt delivered Alt+F5 with xdotool 3.20211022.1.
Temporary key-event diagnostics confirmed the extra modifier. The production shortcut
remains F5. The script sends Xvfb keycode 71 to avoid the
[xdotool function-key modifier defect](https://github.com/jordansissel/xdotool/releases/tag/v4.20251130.1).
Native undo checks use single-character edits because simulated typing creates separate undo entries.

`python3 eng/check-native-editor.py` passed all 44 native workflow groups on Fedora Linux 44, x64,
with Xvfb, a private D-Bus session, and the GTK portal. Local evidence is in
`artifacts/native-editor-xuc9abp3`. The inspected reload screenshot shows the external Unicode
source and mixed line-ending status. The three reload groups verify exact saved bytes,
Cancel with retained undo, and Discard with reset undo. Existing editor workflows also pass.

## Line numbers, issue #69

`Editor.App/ViewModels/EditorViewModel.cs` owns `ShowLineNumbers` in
`Editor.App.ViewModels`. The property starts enabled and stays in the current
window. It has no preference storage. `Views/EditorWindow.axaml` binds the checkbox
and AvaloniaEdit gutter through compiled bindings. `Resources/Text.axaml` owns
the shared label. The toolbar wraps controls to keep the checkbox accessible at
small widths. No dependency, namespace, or public API changes are required.

The initial two headless cases failed because `LineNumbersToggle` was absent.
They pass with the implementation. The focused tests cover keyboard toggling,
both themes, minimum window width, gutter removal and restoration, selection,
caret, dirty state, document identity, undo, redo, exact UTF-8 save bytes,
New, Open, Reload, Save As, Find/Replace, Markdown preview, and source highlighting.
Go To Line tests cover wrapping with numbers both visible and hidden.
The file-drop test opens Markdown with numbers hidden and checks the retained choice.
ViewModel tests check presentation-only notifications and the default in a new window.

`eng/check-native-editor.py` includes keyboard toggles, logical line
navigation with numbers visible and hidden, undo, and exact saved bytes.
Inspect its line-number screenshots to check the gutter and keyboard focus.
For interactive desktop acceptance, repeat those checks in both themes at the
minimum window width. Check that the label, check mark, and focus indicator remain visible.
Windows and macOS native interaction requires a separate desktop session.

`dotnet msbuild eng/verify.proj` passed on Linux with SDK 10.0.402.
Locked restore, audit, formatting, analyzers, and Debug and Release builds passed
with zero diagnostics. Tests passed 306 cases and skipped one Windows-only filesystem
case. All seven TRX reports passed validation. Native startup passed.
The first full run required an update to the existing toolbar layout assertion
because the toolbar now wraps controls.

`python3 eng/check-native-editor.py` passed all 47 workflow groups on Fedora Linux 44,
x64, with Xvfb, the GTK portal, and a private D-Bus session. Local evidence is in
`artifacts/native-editor-oomjbboz`. The inspected screenshots show gutter removal,
keyboard restoration, and insertion at logical line 3 with numbers hidden.
The script compares the rendered gutter before navigation to avoid stale screenshots.
Earlier script attempts required explicit dialog waits and editor focus.
Windows and macOS native interaction remains untested on this Linux host.
Hosted Linux, Windows, and macOS verification must pass before a normal merge.

The first hosted native attempt clicked Word Wrap because Ubuntu uses wider default text.
The script now selects Line numbers with Tab from the existing Word Wrap control.
Its gutter sample excludes the third line for both host font heights.

The second hosted attempt passed the toggle and navigation checks, then failed
while restoring toolbar state after a modal dialog. The script now restores
Word Wrap before that dialog. It leaves line numbers hidden for the remaining
native document workflows and avoids another toolbar traversal after the modal closes.
