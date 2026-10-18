# Open a file by drag and drop

Drop one local text file onto the editor window to open it. Tangopad shows
"Drop to open this file" for an accepted item. The desktop supplies the drag cursor.
A drop uses the same Save, Discard, and Cancel decisions as Open.
Cancel preserves the document, destination, selection, caret, and undo history.
Loading uses the existing strict UTF-8 store. A failed load keeps the active session.
A successful Save before a failed load can still update the current clean baseline.

Folders, multiple items, remote locations, and text-only URI payloads are rejected.
Tangopad does not fetch a dropped URL or insert it into the document.
The view validates external drops before AvaloniaEdit's text-drop handlers run.
An in-process marker identifies drags from this editor. Those drags retain
AvaloniaEdit's text-drop and undo behavior. The marker never enters native drag
data. External drops cannot insert URI text or move the caret before validation.
Normal text selection, clipboard operations, Find/Replace, and undo retain their
existing behavior.

## Platform boundary

`EditorWindow` owns drag integration in `Editor.App/Views`.
It accepts Avalonia `DataFormat.File` items only. The item must be one
`IStorageFile` with an absolute local `file:` URI and a fully qualified path.
UNC paths and non-loopback URI hosts are rejected. The view obtains the path
through `TryGetLocalPath`. It does not parse a text payload as a path.
`EditorViewModel.OpenPathAsync` shares confirmation and session loading with
picker Open. Core, Application, Infrastructure, and dependency policy do not change.

Avalonia converts native drag formats into storage items. X11 file managers
commonly supply a `text/uri-list` with percent-encoded file URIs. The native check
uses a GTK source with that format, including spaces and Unicode in a filename.
Windows and macOS use their Avalonia desktop storage adapters. The application
requires a local storage item instead of assuming a native path string format.
A backend that cannot supply such an item receives rejection. Virtual files and
remote resources are unsupported. Files can disappear or lose read access after
drag validation. The normal Open error dialog handles subsequent load failures.

The Linux native check uses Xvfb and the X11 backend. It does not prove native
Wayland or file-manager portal behavior. A Wayland session that runs Tangopad
through XWayland still requires a separate file-manager acceptance check.
Windows Explorer, macOS Finder, and native Wayland drops remain manual acceptance
checks on their respective hosts.

## Verification

The first headless tests failed before implementation because the dropped file
stayed unopened and the dirty-document prompt did not appear.
Headless tests route drag events through the editor control and window. They check
local file loading, Unicode paths, Save/Discard/Cancel, selection and undo
preservation, rejected payloads, strict UTF-8 errors, and disappearing files.
ViewModel tests check operation reentry, load cancellation, and cancelled Save As.

After a Release build, run the native X11 workflow check:

```sh
python3 eng/check-native-editor.py
```

The script uses an isolated display, D-Bus session, and GTK portal.
`eng/native-drag-source.py` supplies a real GTK drag source. The check sends mouse
input through X11 and records screenshots and workflow results under `artifacts`.

The final native run passed 39 workflow groups on Fedora Linux 44, x64.
This includes the drop affordance, nine drop scenarios, and the existing editor
workflows. Local screenshots and the result report are in
`artifacts/native-editor-pj9558mm`. The inspected affordance screenshot shows the
hint above the document without a layout shift.
