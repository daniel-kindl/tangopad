using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editor.App.Resources;
using Editor.App.Services;
using Editor.Application;
using Editor.Core;
using Editor.Infrastructure;

namespace Editor.App.ViewModels;

// All calls and continuations use the UI thread, as required by DocumentSession.
internal sealed partial class EditorViewModel(ITextDocumentStore store, IEditorDialogs dialogs, EditorLabels labels, RecentFileHistory? recentFiles = null) : ObservableObject
{
    private const double DefaultFontSize = 15;
    private const double MinimumFontSize = 8;
    private const double MaximumFontSize = 48;
    private const double FontSizeStep = 1;

    private CancellationTokenSource? operation;
    private readonly RecentFileHistory history = recentFiles ?? new();

    public IReadOnlyList<string> RecentPaths => history.Paths;
    public IReadOnlyList<RecentFileEntry> RecentFiles => [.. history.Paths.Select(path => new RecentFileEntry(path))];

    internal async Task InitializeHistoryAsync()
    {
        await history.Ready.ConfigureAwait(true);
        OnPropertyChanged(nameof(RecentPaths));
        OnPropertyChanged(nameof(RecentFiles));
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private Task<bool> OpenRecentAsync(string path)
    {
        return OpenPathAsync(path);
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    private async Task ClearRecentFilesAsync()
    {
        history.Clear();
        OnPropertyChanged(nameof(RecentPaths));
        OnPropertyChanged(nameof(RecentFiles));
        await UpdateHistoryStatusAsync().ConfigureAwait(true);
    }

    private void RecordRecent(string path)
    {
        history.Record(path);
        OnPropertyChanged(nameof(RecentPaths));
        OnPropertyChanged(nameof(RecentFiles));
        _ = UpdateHistoryStatusAsync();
    }

    [ObservableProperty]
    public partial bool HistoryStorageFailed { get; private set; }

    private async Task UpdateHistoryStatusAsync()
    {
        await history.FlushAsync().ConfigureAwait(true);
        HistoryStorageFailed = history.StorageFailed;
    }

    [ObservableProperty]
    public partial double FontSize { get; private set; } = DefaultFontSize;

    [RelayCommand]
    private void IncreaseFontSize()
    {
        FontSize = Math.Min(MaximumFontSize, FontSize + FontSizeStep);
    }

    [RelayCommand]
    private void DecreaseFontSize()
    {
        FontSize = Math.Max(MinimumFontSize, FontSize - FontSizeStep);
    }

    [RelayCommand]
    private void ResetFontSize()
    {
        FontSize = DefaultFontSize;
    }

    [ObservableProperty]
    public partial bool WordWrap { get; set; }

    [ObservableProperty]
    public partial bool ShowLineNumbers { get; set; } = true;

    public SearchViewModel Search { get; } = new();

    partial void OnCanEditChanged(bool value) { Search.CanEdit = value; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewCommand), nameof(OpenCommand), nameof(SaveCommand), nameof(SaveAsCommand), nameof(CancelCommand), nameof(OpenRecentCommand), nameof(ClearRecentFilesCommand), nameof(ReloadCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool CanEdit { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(IsDirty))]
    public partial string Text { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int CharacterCount { get; private set; }

    [ObservableProperty]
    public partial int WordCount { get; private set; }

    [ObservableProperty]
    public partial LineEndingStyle LineEndings { get; private set; }

    [ObservableProperty]
    public partial int CaretLine { get; internal set; } = 1;

    [ObservableProperty]
    public partial int CaretColumn { get; internal set; } = 1;

    internal DocumentSession Session { get; private set; } = DocumentSession.CreateUntitled(store);
    public DocumentFormat Format => Session.Format;
    public Guid DocumentId => Session.Document.Id;
    public bool IsDirty => Session.Document.IsDirty;
    public string Title => string.Format(CultureInfo.CurrentCulture, labels.WindowTitleFormat,
        Session.Destination ?? labels.UntitledName, IsDirty ? labels.DirtyMarker : string.Empty);

    partial void OnTextChanged(string value)
    {
        Session.Document.ReplaceText(value);
        int count = 0;
        int words = 0;
        bool inWord = false;
        bool previousCr = false;
        bool hasLf = false;
        bool hasCrLf = false;
        bool hasCr = false;
        foreach (Rune rune in value.EnumerateRunes())
        {
            count++;
            if (previousCr)
            {
                if (rune.Value == '\n')
                {
                    hasCrLf = true;
                }
                else
                {
                    hasCr = true;
                }
            }

            if (rune.Value == '\n' && !previousCr)
            {
                hasLf = true;
            }

            previousCr = rune.Value == '\r';
            if (Rune.IsWhiteSpace(rune))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                words++;
                inWord = true;
            }
        }

        CharacterCount = count;
        WordCount = words;
        LineEndings = (hasLf, hasCrLf, hasCr || previousCr) switch
        {
            (false, false, false) => LineEndingStyle.None,
            (true, false, false) => LineEndingStyle.Lf,
            (false, true, false) => LineEndingStyle.CrLf,
            (false, false, true) => LineEndingStyle.Cr,
            _ => LineEndingStyle.Mixed,
        };
    }

    private bool CanStartOperation => !IsBusy;

    [RelayCommand(CanExecute = nameof(IsBusy))]
    internal void Cancel()
    {
        operation?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    internal Task<bool> NewAsync()
    {
        return RunAsync(async token =>
        {
            if (!await ConfirmReplacementAsync(token).ConfigureAwait(true))
            {
                return false;
            }

            Session = DocumentSession.CreateUntitled(store);
            Text = Session.Document.Text;
            OnPropertyChanged(nameof(DocumentId));
            OnPropertyChanged(nameof(Format));
            return true;
        }, allowEditing: false);
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    internal Task<bool> OpenAsync()
    {
        return RunAsync(async token =>
        {
            string? path = await dialogs.ChooseOpenAsync().ConfigureAwait(true);
            return path is not null && await OpenPathCoreAsync(path, token).ConfigureAwait(true);
        }, allowEditing: false);
    }

    internal Task<bool> OpenPathAsync(string path)
    {
        return RunAsync(token => OpenPathCoreAsync(path, token), allowEditing: false);
    }

    private async Task<bool> OpenPathCoreAsync(string path, CancellationToken token)
    {
        if (!await ConfirmReplacementAsync(token).ConfigureAwait(true))
        {
            return false;
        }

        CanEdit = false;
        DocumentSession loaded = await DocumentSession.LoadAsync(store, path, token).ConfigureAwait(true);
        RecordRecent(path);
        Session = loaded;
        Text = Session.Document.Text;
        OnPropertyChanged(nameof(DocumentId));
        OnPropertyChanged(nameof(Format));
        return true;
    }

    private bool CanReload => !IsBusy && Session.Destination is not null;

    [RelayCommand(CanExecute = nameof(CanReload))]
    internal Task<bool> ReloadAsync()
    {
        return Session.Destination is { } path
            ? RunAsync(token => OpenPathCoreAsync(path, token), allowEditing: false)
            : Task.FromResult(false);
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    internal Task<bool> SaveAsync()
    {
        return RunAsync(token => SaveDocumentAsync(false, token), allowEditing: true);
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    internal Task<bool> SaveAsAsync()
    {
        return RunAsync(token => SaveDocumentAsync(true, token), allowEditing: true);
    }

    internal Task<bool> CanCloseAsync()
    {
        return RunAsync(ConfirmReplacementAsync, allowEditing: false);
    }

    private async Task<bool> ConfirmReplacementAsync(CancellationToken token)
    {
        if (!Session.Document.IsDirty)
        {
            token.ThrowIfCancellationRequested();
            return true;
        }

        UnsavedDecision decision = await dialogs.ConfirmUnsavedAsync().ConfigureAwait(true);
        token.ThrowIfCancellationRequested();
        return decision switch
        {
            UnsavedDecision.Discard => true,
            UnsavedDecision.Save => await SaveDocumentAsync(false, token).ConfigureAwait(true) && !Session.Document.IsDirty,
            UnsavedDecision.Cancel => false,
            _ => false,
        };
    }

    private async Task<bool> SaveDocumentAsync(bool saveAs, CancellationToken token)
    {
        CanEdit = true;
        string? path = Session.Destination;
        if (saveAs || path is null)
        {
            path = await dialogs.ChooseSaveAsync(path).ConfigureAwait(true);
        }

        if (path is null)
        {
            return false;
        }

        token.ThrowIfCancellationRequested();
        await Session.SaveAsAsync(path, token).ConfigureAwait(true);
        RecordRecent(path);
        OnPropertyChanged(nameof(Format));
        return true;
    }

    private async Task<bool> RunAsync(Func<CancellationToken, Task<bool>> action, bool allowEditing)
    {
        if (IsBusy)
        {
            return false;
        }

        using CancellationTokenSource source = new();
        operation = source;
        IsBusy = true;
        CanEdit = allowEditing;
        try
        {
            return await action(source.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException or EncoderFallbackException or NotSupportedException or ArgumentException or AggregateException)
        {
            await dialogs.ShowErrorAsync(error.Message).ConfigureAwait(true);
            return false;
        }
        finally
        {
            operation = null;
            IsBusy = false;
            CanEdit = true;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(IsDirty));
        }
    }

}
