using Avalonia.Controls;
using Avalonia.Input;
using Editor.Application;

namespace Editor.App;

internal sealed partial class EditorWindow : Window
{
    private Guid documentId;
    private bool closingApproved;

    internal EditorWindow(ITextDocumentStore store, IEditorDialogs? dialogs = null)
    {
        InitializeComponent();
        Workspace = new EditorWorkspace(store, dialogs ?? new DesktopEditorDialogs(this));
        WireAction(NewButton, "N", Workspace.NewAsync);
        WireAction(OpenButton, "O", Workspace.OpenAsync);
        WireAction(SaveButton, "S", () => Workspace.SaveAsync());
        WireAction(SaveAsButton, "Shift+S", () => Workspace.SaveAsync(saveAs: true));
        CancelOperation.Click += (_, _) => Workspace.Cancel();
        Editor.TextChanged += (_, _) => Workspace.Edit(Editor.Text);
        Workspace.Changed += (_, _) => Refresh();
        Closing += OnClosing;
        Opened += (_, _) => Editor.TextArea.Focus();
        Refresh();
    }

    internal EditorWorkspace Workspace { get; }

    private void WireAction(Button button, string key, Func<Task<bool>> action)
    {
        KeyGesture gesture = KeyGesture.Parse($"{(OperatingSystem.IsMacOS() ? "Meta" : "Control")}+{key}");
        ToolTip.SetTip(button, gesture.ToString());
        button.Click += async (_, _) => _ = await action().ConfigureAwait(true);
        KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = new EditorCommand(action) });
    }

    private void Refresh()
    {
        Title = Workspace.Title;
        Actions.IsEnabled = !Workspace.IsBusy;
        CancelOperation.IsVisible = Workspace.IsBusy;
        Editor.IsReadOnly = !Workspace.CanEdit;
        if (documentId != Workspace.Session.Document.Id)
        {
            documentId = Workspace.Session.Document.Id;
            Editor.Text = Workspace.Session.Document.Text;
            Editor.Document.UndoStack.ClearAll();
            Editor.CaretOffset = 0;
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (closingApproved)
        {
            return;
        }

        args.Cancel = true;
        if (await Workspace.CanCloseAsync().ConfigureAwait(true))
        {
            closingApproved = true;
            Editor.IsReadOnly = true;
            Actions.IsEnabled = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(Close);
        }
    }

    private sealed class EditorCommand(Func<Task<bool>> action) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter)
        {
            return true;
        }

        public async void Execute(object? parameter)
        {
            _ = await action().ConfigureAwait(true);
        }
    }
}
