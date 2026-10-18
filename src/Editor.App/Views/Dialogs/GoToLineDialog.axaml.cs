using System.Globalization;
using Avalonia.Controls;

namespace Editor.App.Views.Dialogs;

internal sealed partial class GoToLineDialog : Window
{
    internal GoToLineDialog(int currentLine)
    {
        InitializeComponent();
        LineInput.Text = currentLine.ToString(CultureInfo.InvariantCulture);
        Opened += (_, _) =>
        {
            _ = LineInput.Focus();
            LineInput.SelectAll();
        };
        CancelButton.Click += (_, _) => Close();
        GoButton.Click += (_, _) =>
        {
            if (int.TryParse(LineInput.Text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int line) && line > 0)
            {
                Close(line);
                return;
            }

            ValidationMessage.IsVisible = true;
            _ = LineInput.Focus();
            LineInput.SelectAll();
        };
    }
}
