using Editor.Application;

namespace Editor.App;

// All calls and continuations use the UI thread, as required by DocumentSession.
internal sealed class EditorWorkspace(ITextDocumentStore store, IEditorDialogs dialogs)
{
    private CancellationTokenSource? operation;

    internal event EventHandler? Changed;

    internal DocumentSession Session { get; private set; } = DocumentSession.CreateUntitled(store);
    internal bool IsBusy => operation is not null;
    internal bool CanEdit { get; private set; } = true;
    internal string Title => $"{Session.Destination ?? "Untitled"}{(Session.Document.IsDirty ? " *" : string.Empty)} - Tangopad";

    internal void Edit(string text)
    {
        Session.Document.ReplaceText(text);
        Notify();
    }

    internal void Cancel()
    {
        operation?.Cancel();
    }

    internal Task<bool> NewAsync()
    {
        return RunAsync(async token =>
        {
            if (!await ConfirmReplacementAsync(token).ConfigureAwait(true))
            {
                return false;
            }

            Session = DocumentSession.CreateUntitled(store);
            return true;
        }, allowEditing: false);
    }

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
            Notify();
            DocumentSession loaded = await DocumentSession.LoadAsync(store, path, token).ConfigureAwait(true);
            Session = loaded;
            return true;
        }, allowEditing: false);
    }

    internal Task<bool> SaveAsync(bool saveAs = false)
    {
        return RunAsync(token => SaveDocumentAsync(saveAs, token), allowEditing: true);
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
        Notify();
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
        CanEdit = allowEditing;
        Notify();
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
            CanEdit = true;
            Notify();
        }
    }

    private void Notify()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
