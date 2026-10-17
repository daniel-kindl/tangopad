using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
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
        return ShowAsync(
        "Unsaved changes",
        "Save changes before continuing?",
        [("Save", UnsavedDecision.Save), ("Discard", UnsavedDecision.Discard), ("Cancel", UnsavedDecision.Cancel)]);
    }

    public async Task ShowErrorAsync(string message)
    {
        _ = await ShowAsync(
        "File operation failed", message, [("OK", UnsavedDecision.Cancel)]).ConfigureAwait(true);
    }

    private static string LocalPath(IStorageFile file)
    {
        return file.TryGetLocalPath() ?? throw new NotSupportedException("This file has no local path. Select a local file.");
    }

    private async Task<UnsavedDecision> ShowAsync(string title, string message, (string Label, UnsavedDecision Decision)[] choices)
    {
        Window dialog = new()
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        StackPanel panel = new() { Margin = new Thickness(20), Spacing = 20 };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach ((string label, UnsavedDecision decision) in choices)
        {
            Button button = new() { Content = label, IsCancel = decision == UnsavedDecision.Cancel };
            button.Click += (_, _) => dialog.Close(decision);
            buttons.Children.Add(button);
        }

        panel.Children.Add(buttons);
        dialog.Content = panel;
        return await dialog.ShowDialog<UnsavedDecision>(owner).ConfigureAwait(true);
    }
}
