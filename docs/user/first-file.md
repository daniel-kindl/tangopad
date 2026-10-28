# Create and save your first file

Start a [source build or an available package](install.md).
The window opens with an Untitled document.

1. Type `A note for tomorrow` into the editor.
2. Open **File > Save**.
3. Choose a writable local directory in the file picker.
4. Name the file `notes.txt`.
5. Confirm the save in the picker.

The document name changes to `notes.txt`. The unsaved marker disappears when the save succeeds.
Errors appear in a dialog. An error does not establish that your edits reached disk.
Use **Save As** to try another destination if necessary.

## Open and change the file

1. Select **File > New**.
2. Select **File > Open**.
3. Choose `notes.txt` from the directory used above.
4. Add a second line.
5. Select **File > Save**.

An asterisk marks unsaved changes. Save writes a snapshot.
You can continue typing during a save, but later edits remain unsaved.
Save again when the operation finishes if the marker remains.

Open accepts one local UTF-8 file. You can also drop one local file onto the window.
Folders, multiple files, remote locations, and dropped URL text are unsupported.
**File > Recent Files** reopens up to ten recent destinations.
Select **Clear Recent Files** to clear the active history.

## Save a copy

1. Select **File > Save As**.
2. Choose another filename or directory.
3. Confirm the destination.

After a successful save, subsequent saves use the new destination.
The original file remains at its previous location.
Existing destinations can require an additional overwrite confirmation.

## Keep or discard unsaved edits

New, Open, Reload from Disk, and closing the window can ask whether to save changes.

| Choice | Result |
| --- | --- |
| Save | Saves before continuing. A failed or cancelled save prevents the pending action. |
| Discard | Continues without saving the current edits. |
| Cancel | Keeps the document open and stops the pending action. |

Save regularly. There is no autosave or session recovery of unsaved text after a crash.
If another program changes the file, follow the [conflict procedure](troubleshooting.md#the-file-changed-on-disk).

## Try Markdown preview

1. Save the [sample Markdown file](samples/field-notes.md) locally as `field-notes.md`.
2. Open that file in Tangopad.
3. Select **Show Preview** above the editor.
4. Edit a heading or paragraph in the source pane.
5. Check the read-only preview after the short update delay.
6. Select **Hide Preview** to restore the full-width source editor.

![Markdown headings, emphasis, a list, and a quote beside their source in the Linux editor.](media/markdown.png)

The Linux development capture shows the sample in source and preview.
Links stay inactive and image descriptions appear as text.
See the [editor reference](editor.md#markup-and-source-highlighting) for formats and preview limits.
