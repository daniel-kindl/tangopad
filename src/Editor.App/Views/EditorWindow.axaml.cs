using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.Input;
using Editor.App.Resources;
using Editor.App.Services;
using Editor.App.ViewModels;
using Editor.App.Views.Dialogs;
using Editor.Application;
using Editor.Core;
using Editor.Infrastructure;
using Editor.Preview;

namespace Editor.App.Views;

internal sealed partial class EditorWindow : Window
{
    private static readonly DataFormat<EditorWindow> EditorDragFormat = DataFormat.CreateInProcessFormat<EditorWindow>("Tangopad.EditorSelection");

    private bool closingApproved;
    private bool goToLineOpen;

    internal EditorWindow(ITextDocumentStore store, IEditorDialogs? dialogs = null, Func<DocumentFormat, string, PreviewDocument>? previewParser = null, RecentFileHistory? recentFiles = null, EditorPreferences? editorPreferences = null, IDocumentFileMonitor? fileMonitor = null)
    {
        InitializeComponent();
        ViewModel = new EditorViewModel(store, dialogs ?? new DesktopEditorDialogs(this), PresentationResources.EditorLabels, recentFiles, editorPreferences);
        DataContext = ViewModel;
        RecentFilesItems.AddHandler(Button.ClickEvent, (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            RecentFilesButton.Flyout?.Hide();
            _ = Editor.TextArea.Focus();
        }));
        ExternalChanges = new ExternalFileChangeAdapter(ViewModel, fileMonitor ?? new LocalDocumentFileMonitor());
        Preview = new SourcePreviewAdapter(this, previewParser);
        EditorBufferAdapter buffer = new(Editor, ViewModel);
        SourceHighlightingAdapter highlighting = new(Editor, ViewModel);
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
        KeyGesture reload = KeyGesture.Parse(OperatingSystem.IsMacOS() ? "Meta+R" : "F5");
        ToolTip.SetTip(ReloadButton, reload.ToString());
        KeyBindings.Add(new KeyBinding { Gesture = reload, Command = ViewModel.ReloadCommand });
        RelayCommand find = new(() => OpenSearch(false));
        RelayCommand replace = new(() => OpenSearch(true));
        FindButton.Command = find;
        ReplaceButton.Command = replace;
        AddShortcut(FindButton, "F", find);
        AddShortcut(ReplaceButton, "H", replace);
        AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnFontSizeKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnGoToLineKeyDown, RoutingStrategies.Tunnel);
        // Validate external drops before AvaloniaEdit's text-drop handlers run.
        Editor.Options.EnableTextDragDrop = false;
        RegisterDropHandlers(Editor.TextArea);
        Editor.Options.EnableTextDragDrop = true;
        Editor.TextArea.AddHandler(DataObjectEx.DataObjectCopyingEvent, OnCopying);
        DragDrop.SetAllowDrop(this, true);
        RegisterDropHandlers(this);
        Closing += OnClosing;
        Closed += (_, _) => { ViewModel.Search.Dispose(); buffer.Detach(); highlighting.Detach(); Preview.Dispose(); ExternalChanges.Dispose(); };
        Opened += async (_, _) =>
        {
            _ = Editor.TextArea.Focus();
            await Task.WhenAll(ViewModel.InitializeHistoryAsync(), ViewModel.InitializePreferencesAsync()).ConfigureAwait(true);
        };
    }

    internal EditorViewModel ViewModel { get; }
    internal SourcePreviewAdapter Preview { get; }
    internal ExternalFileChangeAdapter ExternalChanges { get; }

    private void RegisterDropHandlers(Interactive target)
    {
        target.AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        target.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        target.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        target.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void OnCopying(object? sender, DataObjectCopyingEventArgs args)
    {
        if (args.IsDragDrop && args.DataObject is DataTransfer data)
        {
            data.Add(DataTransferItem.Create(EditorDragFormat, this));
        }
    }

    private bool IsEditorDrag(DragEventArgs args)
    {
        return ReferenceEquals(args.DataTransfer.TryGetValue(EditorDragFormat), this);
    }

    private string? DroppedPath(DragEventArgs args)
    {
        if (ViewModel.IsBusy || closingApproved || (args.DragEffects & DragDropEffects.Copy) == 0)
        {
            return null;
        }

        try
        {
            if (args.DataTransfer.Items.Count != 1 || args.DataTransfer.Items[0].TryGetFile() is not IStorageFile file)
            {
                return null;
            }

            Uri uri = file.Path;
            if (!uri.IsAbsoluteUri || !uri.IsFile || uri.IsUnc || !uri.IsLoopback)
            {
                return null;
            }

            string? path = file.TryGetLocalPath();
            return path is not null && Path.IsPathFullyQualified(path) && !Directory.Exists(path) ? path : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs args)
    {
        if (IsEditorDrag(args))
        {
            return;
        }

        bool accepted = DroppedPath(args) is not null;
        args.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        DropHint.IsVisible = accepted;
        args.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs args)
    {
        if (IsEditorDrag(args))
        {
            return;
        }

        DropHint.IsVisible = false;
        args.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs args)
    {
        if (IsEditorDrag(args))
        {
            return;
        }

        string? path = DroppedPath(args);
        args.Handled = true;
        args.DragEffects = path is null ? DragDropEffects.None : DragDropEffects.Copy;
        DropHint.IsVisible = false;
        if (path is not null && await ViewModel.OpenPathAsync(path).ConfigureAwait(true))
        {
            _ = Editor.TextArea.Focus();
        }
    }

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
