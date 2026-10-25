using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Editor.App.ViewModels;
using Editor.App.Views;
using Editor.App.Views.Dialogs;
using Editor.Infrastructure;
using Editor.Infrastructure.Updates;
using Velopack;
using Velopack.Locators;

namespace Editor.UpdateProbe;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        string root = DesktopApplicationUpdater.GetDistributionDirectory() ?? throw new IOException("The probe must run from a native package.");
        string configurationPath = Path.Combine(root, ".tangopad-update-probe.json");
        ProbeConfiguration configuration = ReadConfiguration(configurationPath);
        Trace(configuration, $"Started {Environment.ProcessPath}. Action: {configuration.Action}. Arguments: {string.Join(' ', args)}");
        Trace(configuration, "Waiting for the native update handoff.");
        AppDomain.CurrentDomain.UnhandledException += (_, error) => Trace(configuration, error.ExceptionObject.ToString() ?? "An unhandled exception occurred.");
        string? updateError = DesktopApplicationUpdater.WaitForPendingUpdate();
        Trace(configuration, "The native update handoff completed. Recovery: " + (updateError ?? "none"));
        if (args.Contains("--probe-secondary", StringComparer.Ordinal))
        {
            configuration = configuration with { Action = "hold", ResultPath = configuration.ResultPath + ".secondary" };
        }
        string statePath = UserDataLocation.StatePath(args, root);
        if (args.Contains("--installed", StringComparer.Ordinal))
        {
            statePath = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(statePath))!, "TangopadUpdateProbe", "state.bin");
        }
        if (configuration.Action == "initialize" && (File.Exists(statePath)
            || File.Exists(Path.Combine(Path.GetDirectoryName(statePath)!, "editor-preferences.json"))
            || File.Exists(Path.Combine(Path.GetDirectoryName(statePath)!, "recent-files.json"))))
        {
            throw new IOException("The probe refuses to replace existing application state. Use a fresh test account.");
        }
        AppStateStore state = new(statePath);
        state.Ready.GetAwaiter().GetResult();
        Trace(configuration, "Application state is ready at " + statePath);
        if (state.StorageError is not null) { throw new IOException(state.StorageError); }
        if (configuration.Action == "initialize") { state.Preferences.Update(wordWrap: true, showLineNumbers: false, fontSize: 17); }
        using ProbeTransport handler = new(configurationPath);
        using HttpClient transport = new(handler, disposeHandler: false);
        DesktopApplicationUpdater updater = new(args, transport, "TangopadUpdateProbe");
        _ = AppBuilder.Configure(() => new App.App(new FileTextDocumentStore(), state.History, state.Preferences,
                new LocalDocumentFileMonitor(), updateError, updater))
            .UsePlatformDetect()
            .AfterSetup(_ => Dispatcher.UIThread.Post(async () => await ExecuteAsync(configuration, configurationPath, statePath, state, updater, updateError).ConfigureAwait(true)))
            .StartWithClassicDesktopLifetime(args);
    }

    private static ProbeConfiguration ReadConfiguration(string path)
    {
        return ProbeConfiguration.Parse(File.ReadAllText(path));
    }

    private static async Task ExecuteAsync(ProbeConfiguration configuration, string configurationPath, string statePath,
        AppStateStore state, DesktopApplicationUpdater updater, string? updateError)
    {
        try
        {
            IClassicDesktopStyleApplicationLifetime desktop = (IClassicDesktopStyleApplicationLifetime)Avalonia.Application.Current!.ApplicationLifetime!;
            EditorWindow window = (EditorWindow)desktop.MainWindow!;
            while (!window.IsVisible) { await Task.Delay(20).ConfigureAwait(true); }
            Trace(configuration, "The editor window is visible.");
            if (updateError is not null)
            {
                Trace(configuration, "Waiting for the recovery dialog.");
                while (window.OwnedWindows.OfType<ErrorDialog>().SingleOrDefault() is not { IsVisible: true })
                {
                    await Task.Delay(20).ConfigureAwait(true);
                }
                ErrorDialog dialog = window.OwnedWindows.OfType<ErrorDialog>().Single();
                dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Trace(configuration, "The recovery dialog was dismissed.");
            }
            await Task.WhenAll(window.ViewModel.InitializePreferencesAsync(), window.ViewModel.InitializeHistoryAsync()).ConfigureAwait(true);
            if (!await window.ViewModel.OpenPathAsync(configuration.DocumentPath).ConfigureAwait(true)) { throw new IOException("The probe could not open its document."); }
            if (configuration.Action == "hold") { window.ViewModel.Text += "second window unsaved edit\r\n"; }
            UpdateViewModel updates = window.ViewModel.Updates ?? throw new InvalidOperationException("The native update commands are missing.");
            if (configuration.Action is "cancel" or "apply" or "recover")
            {
                if (configuration.Action != "recover") { window.ViewModel.Text += "pending update edit\r\n"; }
                await updates.CheckCommand.ExecuteAsync(null).ConfigureAwait(true);
                if (updates.Release is null) { throw new IOException("The signed local feed did not expose a newer release. " + updates.Error); }
                if (configuration.Action is "apply" or "recover")
                {
                    WriteConfiguration(configurationPath, configuration with { Action = "report", Fault = "none" });
                }
                Task answer = configuration.Action == "recover" ? Task.CompletedTask : AnswerUnsavedAsync(window, configuration.Action == "cancel" ? "CancelButton" : "SaveButton");
                await updates.InstallCommand.ExecuteAsync(null).ConfigureAwait(true);
                await answer.ConfigureAwait(true);
            }
            else if (configuration.Action is "fault" or "protected" or "other-instance")
            {
                await updates.CheckCommand.ExecuteAsync(null).ConfigureAwait(true);
                if (updates.Release is not null) { await updates.InstallCommand.ExecuteAsync(null).ConfigureAwait(true); }
            }
            await Task.WhenAll(state.History.FlushAsync(), state.Preferences.FlushAsync()).ConfigureAwait(true);
            ProbeReport report = new(Environment.ProcessId, updater.InstalledVersion, statePath, window.ViewModel.Text, window.ViewModel.IsDirty,
                window.ViewModel.CanEdit, window.IsVisible, updates.Status, updates.Error, updates.Progress, VelopackLocator.Current.PackagesDir, updateError);
            _ = Directory.CreateDirectory(configuration.EvidenceDirectory);
            using (RenderTargetBitmap bitmap = new(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height)))
            {
                bitmap.Render(window);
                bitmap.Save(Path.Combine(configuration.EvidenceDirectory, "window.png"), new PngBitmapEncoderOptions());
            }
            WriteReport(configuration.ResultPath, report);
            Trace(configuration, "The probe report was written to " + configuration.ResultPath);
            if (configuration.Action == "hold")
            {
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(75));
                while (!File.Exists(configuration.ResultPath + ".release")) { await Task.Delay(20, timeout.Token).ConfigureAwait(true); }
                WriteReport(configuration.ResultPath, report with { Text = window.ViewModel.Text, Dirty = window.ViewModel.IsDirty });
            }
            if (window.ViewModel.IsDirty)
            {
                Task answer = AnswerUnsavedAsync(window, "DiscardButton");
                _ = await window.ViewModel.NewAsync().ConfigureAwait(true);
                await answer.ConfigureAwait(true);
            }
            window.Close();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException or JsonException or OperationCanceledException)
        {
            await Console.Error.WriteLineAsync(error.ToString()).ConfigureAwait(true);
            WriteReport(configuration.ResultPath, new { failure = error.ToString() });
            Environment.Exit(1);
        }
    }

    private static async Task AnswerUnsavedAsync(Window owner, string buttonName)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        while (true)
        {
            UnsavedChangesDialog? dialog = owner.OwnedWindows.OfType<UnsavedChangesDialog>().SingleOrDefault();
            if (dialog is not null && dialog.IsVisible)
            {
                await Task.Delay(100, timeout.Token).ConfigureAwait(true);
                Button button = dialog.FindControl<Button>(buttonName) ?? throw new InvalidOperationException("The native decision button is missing.");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return;
            }
            await Task.Delay(20, timeout.Token).ConfigureAwait(true);
        }
    }

    private static void WriteConfiguration(string path, ProbeConfiguration configuration)
    {
        WriteReport(path, configuration);
    }

    private static void Trace(ProbeConfiguration configuration, string message)
    {
        _ = Directory.CreateDirectory(configuration.EvidenceDirectory);
        File.AppendAllText(Path.Combine(configuration.EvidenceDirectory, "startup.log"),
            $"{DateTimeOffset.UtcNow:O} PID {Environment.ProcessId}: {message}{Environment.NewLine}");
    }

    private static void WriteReport<T>(string path, T value)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value));
        File.Move(temporary, path, overwrite: true);
    }
}
