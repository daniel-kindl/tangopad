using Avalonia.Controls;
using Editor.App.Services;

namespace Editor.App.Views.Dialogs;

internal sealed partial class FileConflictDialog : Window
{
    internal FileConflictDialog(string destination)
    {
        InitializeComponent();
        DestinationText.Text = destination;
        ReloadButton.Click += (_, _) => Close(ConflictDecision.Reload);
        OverwriteButton.Click += (_, _) => Close(ConflictDecision.Overwrite);
        CancelButton.Click += (_, _) => Close(ConflictDecision.Cancel);
    }
}
