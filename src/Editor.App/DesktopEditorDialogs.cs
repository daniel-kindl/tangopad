using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Editor.App;

internal sealed class DesktopEditorDialogs(Window owner) : IEditorDialogs
{
    public async Task<string?> ChooseOpenAsync()
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open text file",
            AllowMultiple = false,
        }).ConfigureAwait(true);
        try
        {
            return files.Count == 0 ? null : LocalPath(files[0]);
        }
        finally
        {
            foreach (IStorageFile file in files)
            {
                file.Dispose();
            }
        }
    }

    public async Task<string?> ChooseSaveAsync(string? destination)
    {
        using IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save text file",
            SuggestedFileName = destination is null ? "Untitled.txt" : Path.GetFileName(destination),
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);
        return file is null ? null : LocalPath(file);
    }

    public Task<UnsavedDecision> ConfirmUnsavedAsync()
    {
        return new UnsavedChangesDialog().ShowDialog<UnsavedDecision>(owner);
    }

    public async Task ShowErrorAsync(string message)
    {
        _ = await new ErrorDialog(message).ShowDialog<UnsavedDecision>(owner).ConfigureAwait(true);
    }

    private static string LocalPath(IStorageFile file)
    {
        return file.TryGetLocalPath() ?? throw new NotSupportedException("This file has no local path. Select a local file.");
    }
}
