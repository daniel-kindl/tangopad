using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using Editor.Application;

namespace Editor.App;

internal sealed class EditorWindow : Window
{
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(8) };
    private readonly Button cancel = new() { Content = "Cancel operation", IsVisible = false };
    private Guid documentId;
    private bool closingApproved;

    internal EditorWindow(ITextDocumentStore store, IEditorDialogs? dialogs = null)
    {
        Width = 960;
        Height = 680;
        MinWidth = 400;
        MinHeight = 250;
        Workspace = new EditorWorkspace(store, dialogs ?? new DesktopEditorDialogs(this));
        AddAction("New", "N", Workspace.NewAsync);
        AddAction("Open", "O", Workspace.OpenAsync);
        AddAction("Save", "S", () => Workspace.SaveAsync());
        AddAction("Save As", "Shift+S", () => Workspace.SaveAsync(saveAs: true));
        cancel.Click += (_, _) => Workspace.Cancel();
        StackPanel toolbar = new() { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(actions);
        toolbar.Children.Add(cancel);
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel layout = new();
        layout.Children.Add(toolbar);
        layout.Children.Add(Editor);
        Content = layout;
        Editor.TextChanged += (_, _) => Workspace.Edit(Editor.Text);
        Workspace.Changed += (_, _) => Refresh();
        Closing += OnClosing;
        Opened += (_, _) => Editor.TextArea.Focus();
        Refresh();
    }

    internal TextEditor Editor { get; } = new()
    {
        FontFamily = FontFamily.Parse("monospace"),
        FontSize = 15,
        ShowLineNumbers = true,
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
    };
    internal EditorWorkspace Workspace { get; }

    private void AddAction(string label, string key, Func<Task<bool>> action)
    {
        KeyGesture gesture = KeyGesture.Parse($"{(OperatingSystem.IsMacOS() ? "Meta" : "Control")}+{key}");
        Button button = new() { Content = label };
        ToolTip.SetTip(button, gesture.ToString());
        button.Click += async (_, _) => _ = await action().ConfigureAwait(true);
        actions.Children.Add(button);
        KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = new EditorCommand(action) });
    }

    private void Refresh()
    {
        Title = Workspace.Title;
        actions.IsEnabled = !Workspace.IsBusy;
        cancel.IsVisible = Workspace.IsBusy;
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
            actions.IsEnabled = false;
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
