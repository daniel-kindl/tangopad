using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Editor.App.Resources;
using Editor.App.Services;
using Editor.App.ViewModels;
using Editor.App.Views.Dialogs;
using Editor.Application;
using Editor.Formats.Markdown;

namespace Editor.App.Views;

internal sealed partial class EditorWindow : Window
{
    private bool closingApproved;
    private bool goToLineOpen;

    internal EditorWindow(ITextDocumentStore store, IEditorDialogs? dialogs = null, Func<string, PreviewDocument>? previewParser = null)
    {
        InitializeComponent();
        ViewModel = new EditorViewModel(store, dialogs ?? new DesktopEditorDialogs(this), PresentationResources.EditorLabels);
        DataContext = ViewModel;
        Preview = new MarkdownPreviewAdapter(this, previewParser);
        EditorBufferAdapter buffer = new(Editor, ViewModel);
        MarkdownHighlightingAdapter highlighting = new(Editor, ViewModel);
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
        AddHandler(KeyDownEvent, OnFontSizeKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnGoToLineKeyDown, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Closed += (_, _) => { ViewModel.Search.Attach(null); buffer.Detach(); highlighting.Detach(); Preview.Dispose(); };
        Opened += (_, _) => Editor.TextArea.Focus();
    }

    internal EditorViewModel ViewModel { get; }
    internal MarkdownPreviewAdapter Preview { get; }

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

    private void OnFontSizeKeyDown(object? sender, KeyEventArgs args)
    {
        if (!Editor.IsKeyboardFocusWithin)
        {
            return;
        }

        KeyModifiers modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        ICommand? command = (args.Key, args.KeyModifiers) switch
        {
            (Key.OemPlus or Key.Add, var modifiers) when modifiers == modifier || modifiers == (modifier | KeyModifiers.Shift)
                => ViewModel.IncreaseFontSizeCommand,
            (Key.OemMinus or Key.Subtract, var modifiers) when modifiers == modifier => ViewModel.DecreaseFontSizeCommand,
            (Key.D0 or Key.NumPad0, var modifiers) when modifiers == modifier => ViewModel.ResetFontSizeCommand,
            _ => null,
        };
        if (command is not null)
        {
            command.Execute(null);
            args.Handled = true;
        }
    }

    private async void OnGoToLineKeyDown(object? sender, KeyEventArgs args)
    {
        KeyModifiers modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (args.Key != Key.G || args.KeyModifiers != modifier || !Editor.IsKeyboardFocusWithin || goToLineOpen)
        {
            return;
        }

        args.Handled = true;
        goToLineOpen = true;
        try
        {
            int? line = await new GoToLineDialog(Editor.TextArea.Caret.Line).ShowDialog<int?>(this).ConfigureAwait(true);
            if (line is { } requestedLine)
            {
                int offset = Editor.Document.GetLineByNumber(Math.Min(requestedLine, Editor.Document.LineCount)).Offset;
                Editor.Select(offset, 0);
                Editor.TextArea.Caret.BringCaretToView();
            }

            _ = Editor.TextArea.Focus();
        }
        finally
        {
            goToLineOpen = false;
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
