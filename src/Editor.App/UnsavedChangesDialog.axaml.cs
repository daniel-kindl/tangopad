using Avalonia.Controls;

namespace Editor.App;

internal sealed partial class UnsavedChangesDialog : Window
{
    internal UnsavedChangesDialog()
    {
        InitializeComponent();
        SaveButton.Click += (_, _) => Close(UnsavedDecision.Save);
        DiscardButton.Click += (_, _) => Close(UnsavedDecision.Discard);
        CancelButton.Click += (_, _) => Close(UnsavedDecision.Cancel);
    }
}
