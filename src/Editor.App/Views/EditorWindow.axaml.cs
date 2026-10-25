using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using AvaloniaEdit.Document;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.Input;
using Editor.App.Resources;
using Editor.App.Services;
using Editor.App.ViewModels;
using Editor.App.Views.Dialogs;
using Editor.Application;
using Editor.Application.Updates;
using Editor.Core;
using Editor.Infrastructure;
using Editor.Preview;

namespace Editor.App.Views;

internal sealed partial class EditorWindow : Window
{
    private static readonly DataFormat<EditorWindow> EditorDragFormat = DataFormat.CreateInProcessFormat<EditorWindow>("Tangopad.EditorSelection");

    private bool closingApproved;
    private bool goToLineOpen;

    internal EditorWindow(ITextDocumentStore store, IEditorDialogs? dialogs = null, Func<DocumentFormat, string, PreviewDocument>? previewParser = null, RecentFileHistory? recentFiles = null, EditorPreferences? editorPreferences = null, IDocumentFileMonitor? fileMonitor = null, IApplicationUpdater? updater = null)
    {
        InitializeComponent();
        ViewModel = new EditorViewModel(store, dialogs ?? new DesktopEditorDialogs(this), PresentationResources.EditorLabels, recentFiles, editorPreferences);
        if (updater is not null) { ViewModel.Updates = new UpdateViewModel(updater, PresentationResources.UpdateLabels, ViewModel.RestartForUpdateAsync); }
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
        AddShortcut(NewButton, "N", ViewModel.NewCommand, NewMenu);
        AddShortcut(OpenButton, "O", ViewModel.OpenCommand, OpenMenu);
        AddShortcut(SaveButton, "S", ViewModel.SaveCommand, SaveMenu);
        AddShortcut(SaveAsButton, "Shift+S", ViewModel.SaveAsCommand, SaveAsMenu);
        KeyGesture reload = KeyGesture.Parse(OperatingSystem.IsMacOS() ? "Meta+R" : "F5");
        ToolTip.SetTip(ReloadButton, $"{ReloadButton.Content} ({reload})");
        ReloadMenu.InputGesture = reload;
        KeyBindings.Add(new KeyBinding { Gesture = reload, Command = ViewModel.ReloadCommand });
        RelayCommand find = new(() => OpenSearch(false));
        RelayCommand replace = new(() => OpenSearch(true));
        FindButton.Command = find;
        ReplaceButton.Command = replace;
        AddShortcut(FindButton, "F", find, FindMenu);
        AddShortcut(ReplaceButton, "H", replace, ReplaceMenu);
        string modifier = OperatingSystem.IsMacOS() ? "Meta" : "Control";
        foreach ((MenuItem menu, string key) in new (MenuItem, string)[]
        {
            (UndoMenu, "Z"), (RedoMenu, OperatingSystem.IsMacOS() ? "Shift+Z" : "Y"),
            (CutMenu, "X"), (CopyMenu, "C"), (PasteMenu, "V"), (SelectAllMenu, "A"),
            (ZoomInMenu, "OemPlus"), (ZoomOutMenu, "OemMinus"), (ZoomResetMenu, "D0"),
        })
        {
            menu.InputGesture = KeyGesture.Parse($"{modifier}+{key}");
        }
        FindMenu.Command = find;
        ReplaceMenu.Command = replace;
        GoToLineMenu.Command = new AsyncRelayCommand(ShowGoToLineAsync);
        GoToLineMenu.InputGesture = KeyGesture.Parse($"{(OperatingSystem.IsMacOS() ? "Meta" : "Control")}+G");
        _ = RecentMenu.Items.Add(new MenuItem { Header = PresentationResources.Text("Text.Recent.Empty"), IsEnabled = false });
        RecentMenu.SubmenuOpened += (_, _) =>
        {
            RecentMenu.Items.Clear();
            foreach (RecentFileEntry entry in ViewModel.RecentFiles)
            {
                MenuItem item = new() { Header = entry.Label, Command = ViewModel.OpenRecentCommand, CommandParameter = entry.Path };
                ToolTip.SetTip(item, entry.Path);
                _ = RecentMenu.Items.Add(item);
            }
            if (ViewModel.RecentFiles.Count == 0)
            {
                _ = RecentMenu.Items.Add(new MenuItem { Header = PresentationResources.Text("Text.Recent.Empty"), IsEnabled = false });
            }
            _ = RecentMenu.Items.Add(new Separator());
            _ = RecentMenu.Items.Add(new MenuItem { Header = PresentationResources.Text("Text.Recent.Clear"), Command = ViewModel.ClearRecentFilesCommand });
        };
        UpdatesMenu.Command = new AsyncRelayCommand(async () =>
        {
            if (ViewModel.Updates is not { } updates) { return; }
            UpdatesDialog dialog = new(updates) { RequestedThemeVariant = RequestedThemeVariant };
            Task showing = dialog.ShowDialog(this);
            if (updates.CheckCommand.CanExecute(null)) { await updates.CheckCommand.ExecuteAsync(null).ConfigureAwait(true); }
            await showing.ConfigureAwait(true);
            _ = Editor.TextArea.Focus();
        });
        CloseMenu.Command = new RelayCommand(Close);
        AboutMenu.Command = new AsyncRelayCommand(async () =>
        {
            await new AboutDialog { RequestedThemeVariant = RequestedThemeVariant }.ShowDialog(this).ConfigureAwait(true);
            _ = Editor.TextArea.Focus();
        });
        SystemThemeMenu.Command = new RelayCommand(() => RequestedThemeVariant = ThemeVariant.Default);
        LightThemeMenu.Command = new RelayCommand(() => RequestedThemeVariant = ThemeVariant.Light);
        DarkThemeMenu.Command = new RelayCommand(() => RequestedThemeVariant = ThemeVariant.Dark);
        ZoomInMenu.Command = ViewModel.IncreaseFontSizeCommand;
        ZoomOutMenu.Command = ViewModel.DecreaseFontSizeCommand;
        ZoomResetMenu.Command = ViewModel.ResetFontSizeCommand;
        RelayCommand undo = new(() => { _ = Editor.Undo(); _ = Editor.TextArea.Focus(); }, () => ViewModel.CanEdit && Editor.Document.UndoStack.CanUndo);
        RelayCommand redo = new(() => { _ = Editor.Redo(); _ = Editor.TextArea.Focus(); }, () => ViewModel.CanEdit && Editor.Document.UndoStack.CanRedo);
        UndoButton.Command = UndoMenu.Command = undo;
        RedoButton.Command = RedoMenu.Command = redo;
        Editor.Document.UndoStack.PropertyChanged += (_, _) => { undo.NotifyCanExecuteChanged(); redo.NotifyCanExecuteChanged(); };
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EditorViewModel.CanEdit)) { undo.NotifyCanExecuteChanged(); redo.NotifyCanExecuteChanged(); }
        };
        CutMenu.Command = new RelayCommand(() => { Editor.Cut(); _ = Editor.TextArea.Focus(); }, () => ViewModel.CanEdit);
        CopyMenu.Command = new RelayCommand(() => { Editor.Copy(); _ = Editor.TextArea.Focus(); });
        PasteMenu.Command = new RelayCommand(() => { Editor.Paste(); _ = Editor.TextArea.Focus(); }, () => ViewModel.CanEdit);
        SelectAllMenu.Command = new RelayCommand(() => { Editor.SelectAll(); _ = Editor.TextArea.Focus(); });
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
        if (!ViewModel.CanEdit || !Editor.IsKeyboardFocusWithin)
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
        await ShowGoToLineAsync().ConfigureAwait(true);
    }

    private async Task ShowGoToLineAsync()
    {
        if (goToLineOpen || !ViewModel.CanEdit) { return; }
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

    private void AddShortcut(Button button, string key, ICommand command, MenuItem? menu = null)
    {
        KeyGesture gesture = KeyGesture.Parse($"{(OperatingSystem.IsMacOS() ? "Meta" : "Control")}+{key}");
        ToolTip.SetTip(button, $"{button.Content} ({gesture})");
        menu?.InputGesture = gesture;
        KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = command });
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        if (closingApproved)
        {
            return;
        }

        args.Cancel = true;
        ViewModel.Updates?.CancelCommand.Execute(null);
        if (await ViewModel.CanCloseAsync().ConfigureAwait(true))
        {
            closingApproved = true;
            Editor.IsReadOnly = true;
            Actions.IsEnabled = false;
            Avalonia.Threading.Dispatcher.UIThread.Post(Close);
        }
    }
}
