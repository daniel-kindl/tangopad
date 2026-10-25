using Avalonia.Controls;
using Editor.App.Services;

namespace Editor.App.Views.Dialogs;

internal sealed partial class ErrorDialog : Window
{
    internal ErrorDialog(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        OkButton.Click += (_, _) => Close(UnsavedDecision.Cancel);
    }
}
