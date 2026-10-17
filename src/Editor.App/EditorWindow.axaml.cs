using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
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
        ViewModel.Search.Attach(buffer);
        ViewModel.Search.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SearchViewModel.IsOpen) && !ViewModel.Search.IsOpen)
            {
                _ = Editor.TextArea.Focus();
            }
        };
        AddShortcut(NewButton, "N", ViewModel.NewCommand);
        AddShortcut(OpenButton, "O", ViewModel.OpenCommand);
        AddShortcut(SaveButton, "S", ViewModel.SaveCommand);
        AddShortcut(SaveAsButton, "Shift+S", ViewModel.SaveAsCommand);
        RelayCommand find = new(() => OpenSearch(false));
        RelayCommand replace = new(() => OpenSearch(true));
        FindButton.Command = find;
        ReplaceButton.Command = replace;
        AddShortcut(FindButton, "F", find);
        AddShortcut(ReplaceButton, "H", replace);
        AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Closed += (_, _) => { ViewModel.Search.Attach(null); buffer.Detach(); };
        Opened += (_, _) => Editor.TextArea.Focus();
    }

    internal EditorViewModel ViewModel { get; }

    private void OpenSearch(bool replace)
    {
        if (replace)
        {
            ViewModel.Search.ShowReplaceCommand.Execute(null);
        }
        else
        {
            ViewModel.Search.ShowFindCommand.Execute(null);
        }

        _ = QueryInput.Focus();
        QueryInput.SelectAll();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs args)
    {
        if (!ViewModel.Search.IsOpen)
        {
            return;
        }

        if (args.Key == Key.Escape && args.KeyModifiers == KeyModifiers.None)
        {
            ViewModel.Search.CloseCommand.Execute(null);
            args.Handled = true;
        }
        else if ((QueryInput.IsKeyboardFocusWithin || ReplacementInput.IsKeyboardFocusWithin) && args.Key == Key.Enter && (args.KeyModifiers == KeyModifiers.None || args.KeyModifiers == KeyModifiers.Shift))
        {
            ICommand command = args.KeyModifiers == KeyModifiers.Shift ? ViewModel.Search.PreviousCommand : ViewModel.Search.NextCommand;
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }

            args.Handled = true;
        }
    }

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
