using Avalonia.Controls;

namespace Editor.App;

internal sealed partial class ErrorDialog : Window
{
    internal ErrorDialog(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        OkButton.Click += (_, _) => Close(UnsavedDecision.Cancel);
    }
}
