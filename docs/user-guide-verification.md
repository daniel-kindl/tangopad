# Native user-guide verification

[Issue #105](https://github.com/daniel-kindl/tangopad/issues/105) requires guide validation against native desktop packages.
This record covers scripted Linux development checks. It does not establish final release-candidate or first-time-user acceptance.
The test follows the [first-file tutorial](user/first-file.md) and selected reference and troubleshooting procedures.
It uses real keyboard input, menus, GTK file pickers, application dialogs, and saved files.
It does not inject ViewModel state or replace the file store.

The implementation stays in `eng/check-native-editor.py` and the existing native CI steps.
No application namespace, behavior, or project dependency changes.

## Repeat the source-build check

Install the [Linux native workflow prerequisites](native-startup.md) and the packages listed in
[the Verify workflow](../.github/workflows/verify.yml).
Use a non-root account. The permission-failure case requires ordinary filesystem permissions.
Run from the repository root:

```sh
dotnet msbuild eng/verify.proj
python3 eng/check-native-editor.py --user-guide-only
```

This mode reuses the existing isolated Xvfb display, private D-Bus session, GTK portal,
and disposable application profile. It does not send input to the active desktop.
The default command without `--user-guide-only` retains the existing editor workflow suite.
Hosted Linux verification runs both modes and uploads their evidence directories.

## Repeat the packaged check

Build a Linux test-feed candidate on Linux:

```sh
python3 eng/package-release.py --rid linux-x64
```

Select the actual AppImage from `artifacts/release/linux-x64/<version>/`.
The packaging command prints that directory.
Run the guide check with the selected path:

```sh
python3 eng/check-native-editor.py --user-guide-only --executable <path-to-candidate.AppImage>
```

The placeholder must be replaced with an existing file path.
The supplied executable option requires `--user-guide-only`.
For an AppImage, the runner sets `APPIMAGE_EXTRACT_AND_RUN=1`.
This executes the packaged application without requiring a FUSE mount.
It does not prove ordinary file-manager launch, FUSE support, desktop integration, or installation.
It does not test production signing, updates, or removal.

The candidate workflow runs this check on its built Linux AppImage.
Its Linux host installs the existing native input, portal, and screenshot tools.
The workflow uploads guide evidence with the other native package evidence.
Windows and macOS package-guide checks remain separate acceptance work.

## Inspect evidence

Each run prints a fresh `artifacts/native-editor-<suffix>/` directory.
The directory contains screenshots, process logs, and `result.json`.
The report records these values:

- Overall result, host, architecture, session, and completed steps.
- Source commit and whether the checkout contains uncommitted work.
- Executable SHA-256 and, for an ordinary source build, the managed assembly hash.
- Launch kind, including explicit AppImage extraction mode.
- SHA-256 values for the six guide pages and their Markdown sample.
- A statement that production installation, update, and uninstall acceptance remains untested.

A failed check retains a failure screenshot and a FAIL report.
A successful guide run requires the editor to close with exit code 0.
The script compares saved files with literal expected bytes.
Keep diagnostic screenshots in ignored artifacts. They can contain synthetic temporary paths and are not product illustrations.

## Recorded Linux candidate result

Date: 2026-10-26. Host: Fedora Linux 44 container, x86_64, Xvfb, GTK portal, and private D-Bus.
Source base: `12ec809ef5c33c73902f1195a81155bc819028ae` with the uncommitted documentation and verification changes.
Candidate version: 0.1.0. The package inventory records `production: false`.
This is not a published stable release.

The candidate AppImage was `TangopadDesktop-linux-x64.AppImage`.
Its SHA-256 was `2b55dca781009aa3710e1130e094ac5d4cbdef11cf1538bfed0b8d467bab7070`.
OpenSSL verified the inventory signature against the candidate's temporary test key.
Every artifact hash matched the signed inventory. The guide report recorded the same AppImage hash.
The temporary key does not authenticate production update feeds.

The final candidate run passed all 17 workflow checkpoints and exited with code 0.
Evidence was recorded in `artifacts/native-editor-skj0fh3q/result.json`.

| Guide behavior | Observed result |
| --- | --- |
| File > Save, New, Open, edit, and Save | Native menu actions produced the expected files and exact saved text. |
| Save As | Subsequent Save used the copy. The original retained its bytes. |
| Unsaved Cancel, Save, and Discard | Cancel retained edits. Save finished before New. Discard left the previous file unchanged. |
| External conflict | Cancel preserved the external version. Save As preserved local edits. Opening the original exposed the external contents. |
| Permission error | A non-writable parent caused a real save error. Save As to a writable destination retained the unsaved text. |
| Encoding | Malformed UTF-8 was rejected without losing the current file. BOM-bearing Unicode input saved without the signature and retained exact mixed endings. |
| Presentation | Word Wrap, line numbers, font zoom, and Reset Zoom changed presentation and stored choices without changing file bytes. |
| Markdown sample | The real sample opened, preview appeared, the edited heading rendered, and Hide Preview restored source view. |
| Replace all | Exact output matched the replacement. One Undo restored the previous source. |
| Go To Line | A character inserted at line 2 appeared at the expected byte offset. Undo restored the exact source. |

The preview check waits for visible heading ink and then for the longer edited heading.
A temporary blank pane cannot satisfy the update condition.
The final edited-heading screenshot was inspected and showed `Field notes edited` in the preview.
The permission-error screenshot was also inspected.
The source-build run also passed all 17 checkpoints. Its evidence is in `artifacts/native-editor-k583up6m/result.json`.
These are scripted checks derived from the guide, not a novice participant's usability test.

## Remaining acceptance

| Area | State |
| --- | --- |
| Linux candidate UI through AppImage extraction mode | Passed the 17 scripted checkpoints above. |
| Direct AppImage launch, supported distributions, FUSE, and desktop integration | Not tested by this run. |
| Production installation, update, uninstall, and state preservation | Pending issue #107 and release-package acceptance. |
| Native Windows and macOS guide interaction | Not tested on this host. |
| Full shortcut coverage, regex limits, preferences after restart, and every format limit | Separate existing tests and final native guide review are still required. |
| Screen readers, OS high contrast, and physical monitor transitions | Pending the recorded accessibility work. |
| A first-time user following only the final guide | Pending human review on the selected release candidate. |

Keep [the product acceptance matrix](product-documentation.md#release-candidate-acceptance-record) pending until final evidence exists.
This development result does not close issues #105 or #48.
