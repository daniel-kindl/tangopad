# Review the interface revision

Generate disposable test documents from the repository root:

```sh
python3 eng/prepare-ux-samples.py
```

The command writes to `artifacts/ux-samples/`.
Running it again replaces the sample documents with their original bytes.
The sample directory is ignored by Git.

| File | Check |
| --- | --- |
| `01-welcome.md` | Markdown highlighting, preview, tables, code, Unicode, and long lines. |
| `02-search-and-unicode.txt` | Case sensitivity, whole words, regex captures, Unicode, and horizontal scrolling. |
| `03-windows-crlf.txt` | The status shows CRLF before and after a save. |
| `04-mixed-endings.txt` | The status shows Mixed. Saving preserves the original endings. |
| `05-preview.textile` | Textile highlighting and supported preview blocks. |
| `06-settings.json` | JSON highlighting. No preview action appears. |
| `07-empty.txt` | Zero counts, None endings, and immediate editing. |
| `08-many-lines.txt` | Scrolling, line numbers, and Go To Line. |
| `09-no-final-newline.txt` | None endings. Saving adds no newline. |

## Check the shell

1. Open `01-welcome.md` with Open.
2. Inspect the file actions, editing controls, canvas, and status bar.
3. Resize the window to 400 pixels wide.
4. Check that every file action remains visible.
5. Enable Word Wrap.
6. Disable line numbers.
7. Enable Show Preview.
8. Drag the divider.
9. Restore line numbers.
10. Compare Light and Dark under View > Appearance.

Check the shell at the minimum height with search and preview closed.
Search and other expanded panels need additional height.
At 400 × 250, both toolbars must remain reachable above at least one editor line.

## Check menus and dialogs

1. Open File.
2. Inspect New, Open, Recent Files, Save, Save As, Reload from Disk, and Exit.
3. Inspect Edit and Search for commands and shortcut labels.
4. Open View.
5. Change Word Wrap and line numbers through the menu.
6. Use Appearance to compare Light, Dark, and Follow System.
7. Open Help.
8. Select About Tangopad.
9. Check the version, description, project link, and icon credits.
10. Close About with Escape.
11. Check that the editor has focus.
12. Select Check for Updates under Help.
13. Check that update status appears in a dialog.
14. Close the dialog.
15. Check that the status bar contains no update button.

## Check editing and recovery

1. Find `draft` in `02-search-and-unicode.txt`.
2. Enable Match case.
3. Compare the matches with Match whole word enabled.
4. Enable regular expressions.
5. Find `item-(\d+)`.
6. Replace the matches with `entry-$1`.
7. Check for `entry-001, entry-002, entry-003`.
8. Undo the replacement.
9. Save a copy with Save As.
10. Edit the copy.
11. Select New.
12. Select Cancel in the unsaved-changes dialog.
13. Check that the text and selection remain available.

## Check keyboard access

Use Ctrl on Linux and Windows. Use Command on macOS.

1. Open Find with Ctrl+F or Command+F.
2. Check that the query input has focus.
3. Close search with Escape.
4. Check that the editor has focus.
5. Open Go To Line with Ctrl+G or Command+G.
6. Navigate to line 1500 in `08-many-lines.txt`.
7. Change font zoom with the existing editor shortcuts.
8. Use Tab and Shift+Tab to inspect command focus indicators.

## Automated verification and evidence

Run the repository gate:

```sh
dotnet msbuild eng/verify.proj
```

`ToolbarLayoutTests` checks command bounds, target heights, and editor space in both themes.
It checks 400 × 250, 640 × 480, and 1040 × 760 windows.
Linux native screenshots check the actual font rendering at 400 × 250.
The headless suite checks menu actions, About, update entry, search, exact source, dirty state, undo, preview, and dialog decisions.

Windows, macOS, screen readers, high-contrast settings, and multiple DPI scales need separate native acceptance.
Record each platform result before treating issue #47 as complete.

## Recorded local result

On Linux, `dotnet msbuild eng/verify.proj` passed with zero build warnings and errors.
The reports contain 709 passed tests and one Windows-only skip.
`python3 eng/check-native-editor.py` also passed, including the Help dialogs and Recent Files submenu.
The native run used Xvfb, a private D-Bus session, and the GTK portal.
Light and dark screenshots are in [the design draft](47-design.md).
The local review profile includes all nine sample paths in Recent Files.
Windows, macOS, screen readers, high-contrast settings, and multiple DPI scales remain untested.
