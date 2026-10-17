using Avalonia.Controls;
using Editor.App.Services;

namespace Editor.App.Views.Dialogs;

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
