using Avalonia.Controls;
using Editor.App.ViewModels;

namespace Editor.App.Views.Dialogs;

internal sealed partial class UpdatesDialog : Window
{
    internal UpdatesDialog(UpdateViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        CloseButton.Click += (_, _) => Close();
        Closed += (_, _) => viewModel.CancelCommand.Execute(null);
    }
}
