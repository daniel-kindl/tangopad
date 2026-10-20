using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Editor.App.Resources;
using Editor.App.Views.Dialogs;

namespace Editor.App.Services;

internal sealed class DesktopEditorDialogs(Window owner) : IEditorDialogs
{
    public async Task<string?> ChooseOpenAsync()
    {
        IReadOnlyList<IStorageFile> files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = PresentationResources.Text("Text.Picker.Open.Title"),
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
            Title = PresentationResources.Text("Text.Picker.Save.Title"),
            SuggestedFileName = destination is null ? PresentationResources.Text("Text.Picker.Save.DefaultName") : Path.GetFileName(destination),
            ShowOverwritePrompt = true,
        }).ConfigureAwait(true);
        return file is null ? null : LocalPath(file);
    }

    public Task<UnsavedDecision> ConfirmUnsavedAsync()
    {
        return new UnsavedChangesDialog().ShowDialog<UnsavedDecision>(owner);
    }

    public Task<ConflictDecision> ConfirmConflictAsync(string destination)
    {
        return new FileConflictDialog(destination).ShowDialog<ConflictDecision>(owner);
    }

    public async Task ShowErrorAsync(string message)
    {
        _ = await new ErrorDialog(message).ShowDialog<UnsavedDecision>(owner).ConfigureAwait(true);
    }

    private static string LocalPath(IStorageFile file)
    {
        return file.TryGetLocalPath() ?? throw new NotSupportedException(PresentationResources.Text("Text.Error.LocalPathRequired"));
    }
}
