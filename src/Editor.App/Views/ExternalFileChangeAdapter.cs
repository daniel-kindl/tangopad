using System.ComponentModel;
using System.Threading.Channels;
using Avalonia.Threading;
using Editor.App.ViewModels;
using Editor.Application;

namespace Editor.App.Views;

// The worker only reads disk state. The dispatcher owns all presentation state.
internal sealed class ExternalFileChangeAdapter : IDisposable
{
    private readonly EditorViewModel viewModel;
    private readonly IDocumentFileMonitor monitor;
    private readonly Dispatcher dispatcher = Dispatcher.UIThread;
    private CancellationTokenSource? cancellation;
    private IDisposable? subscription;
    private string? baseline;
    private Guid documentId;
    private string? observed;
    private string? dismissed;
    private bool hasDismissed;
    private bool disposed;

    internal ExternalFileChangeAdapter(EditorViewModel viewModel, IDocumentFileMonitor monitor)
    {
        this.viewModel = viewModel;
        this.monitor = monitor;
        viewModel.PropertyChanged += OnPropertyChanged;
        viewModel.ExternalChangeDismissed = Dismiss;
    }

    internal Task Work { get; private set; } = Task.CompletedTask;

    /// <summary>Gets how many verification results the dispatcher has applied.</summary>
    internal int DeliveredResults { get; private set; }

    private void Dismiss()
    {
        dismissed = observed;
        hasDismissed = true;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(EditorViewModel.IsBusy))
        {
            Stop();
            if (!viewModel.IsBusy)
            {
                Start();
            }
        }
    }

    private void Start()
    {
        if (disposed)
        {
            return;
        }

        if (documentId != viewModel.DocumentId || baseline != viewModel.Session.Revision)
        {
            documentId = viewModel.DocumentId;
            baseline = viewModel.Session.Revision;
            hasDismissed = false;
            viewModel.HasExternalChange = false;
        }

        if (viewModel.Session.Destination is not { } path)
        {
            return;
        }

        CancellationTokenSource source = new();
        cancellation = source;
        Channel<bool> events = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        void Signal() { _ = events.Writer.TryWrite(true); }
        try
        {
            subscription = monitor.Watch(path, Signal);
        }
        catch (Exception error) when (IsMonitoringFailure(error))
        {
            // Verification still runs once when the backend cannot monitor this destination.
        }
        Signal();
        CancellationToken token = source.Token;
        Work = Task.Run(() => ObserveAsync(path, source, events.Reader, token));
    }

    private async Task ObserveAsync(string path, CancellationTokenSource source, ChannelReader<bool> events, CancellationToken token)
    {
        try
        {
            while (await events.WaitToReadAsync(token).ConfigureAwait(false))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150), token).ConfigureAwait(false);
                _ = events.TryRead(out _);
                try
                {
                    string? revision = await monitor.ReadRevisionAsync(path, token).ConfigureAwait(false);
                    await dispatcher.InvokeAsync(() =>
                    {
                        if (disposed || !ReferenceEquals(cancellation, source) || token.IsCancellationRequested)
                        {
                            return;
                        }
                        observed = revision;
                        viewModel.HasExternalChange = revision != baseline && !(hasDismissed && revision == dismissed);
                        DeliveredResults++;
                    }, DispatcherPriority.Default, token);
                }
                catch (Exception error) when (IsMonitoringFailure(error))
                {
                    // A failed check cannot establish a new disk revision.
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static bool IsMonitoringFailure(Exception error)
    {
        return error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Security.SecurityException;
    }

    private void Stop()
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = null;
        subscription?.Dispose();
        subscription = null;
    }

    public void Dispose()
    {
        disposed = true;
        viewModel.PropertyChanged -= OnPropertyChanged;
        viewModel.ExternalChangeDismissed = null;
        Stop();
    }
}
