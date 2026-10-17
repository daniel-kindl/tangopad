using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editor.App.Resources;
using Editor.App.Services;
using Editor.Application;

namespace Editor.App.ViewModels;

// All calls and continuations use the UI thread, as required by DocumentSession.
internal sealed partial class EditorViewModel(ITextDocumentStore store, IEditorDialogs dialogs, EditorLabels labels) : ObservableObject
{
    private CancellationTokenSource? operation;

    public SearchViewModel Search { get; } = new();

    partial void OnCanEditChanged(bool value) { Search.CanEdit = value; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NewCommand), nameof(OpenCommand), nameof(SaveCommand), nameof(SaveAsCommand), nameof(CancelCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial bool CanEdit { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(IsDirty))]
    public partial string Text { get; set; } = string.Empty;

    internal DocumentSession Session { get; private set; } = DocumentSession.CreateUntitled(store);
    public Guid DocumentId => Session.Document.Id;
    public bool IsDirty => Session.Document.IsDirty;
    public string Title => string.Format(CultureInfo.CurrentCulture, labels.WindowTitleFormat,
        Session.Destination ?? labels.UntitledName, IsDirty ? labels.DirtyMarker : string.Empty);

    partial void OnTextChanged(string value)
    {
        Session.Document.ReplaceText(value);
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
            return true;
        }, allowEditing: false);
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    internal Task<bool> OpenAsync()
    {
        return RunAsync(async token =>
        {
            string? path = await dialogs.ChooseOpenAsync().ConfigureAwait(true);
            if (path is null || !await ConfirmReplacementAsync(token).ConfigureAwait(true))
            {
                return false;
            }

            CanEdit = false;
            DocumentSession loaded = await DocumentSession.LoadAsync(store, path, token).ConfigureAwait(true);
            Session = loaded;
            Text = Session.Document.Text;
            OnPropertyChanged(nameof(DocumentId));
            return true;
        }, allowEditing: false);
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
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.DecoderFallbackException or System.Text.EncoderFallbackException or NotSupportedException or ArgumentException or AggregateException)
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
