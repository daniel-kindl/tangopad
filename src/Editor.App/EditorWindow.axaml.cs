using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Editor.Application;

namespace Editor.App;

internal sealed partial class EditorWindow : Window
{
    private bool closingApproved;

    internal EditorWindow(ITextDocumentStore store, IEditorDialogs? dialogs = null)
    {
        InitializeComponent();
        ViewModel = new EditorViewModel(store, dialogs ?? new DesktopEditorDialogs(this), PresentationResources.EditorLabels);
        DataContext = ViewModel;
        EditorBufferAdapter buffer = new(Editor, ViewModel);
        AddShortcut(NewButton, "N", ViewModel.NewCommand);
        AddShortcut(OpenButton, "O", ViewModel.OpenCommand);
        AddShortcut(SaveButton, "S", ViewModel.SaveCommand);
        AddShortcut(SaveAsButton, "Shift+S", ViewModel.SaveAsCommand);
        Closing += OnClosing;
        Closed += (_, _) => buffer.Detach();
        Opened += (_, _) => Editor.TextArea.Focus();
    }

    internal EditorViewModel ViewModel { get; }

    private void AddShortcut(Button button, string key, ICommand command)
    {
        KeyGesture gesture = KeyGesture.Parse($"{(OperatingSystem.IsMacOS() ? "Meta" : "Control")}+{key}");
        ToolTip.SetTip(button, gesture.ToString());
        KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command });
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (closingApproved)
        {
            return;
        }

        args.Cancel = true;
        if (await ViewModel.CanCloseAsync().ConfigureAwait(true))
        {
            closingApproved = true;
            Editor.IsReadOnly = true;
            Actions.IsEnabled = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(Close);
        }
    }
}
