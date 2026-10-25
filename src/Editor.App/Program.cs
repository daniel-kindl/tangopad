using Avalonia;
using Editor.Infrastructure;
using Editor.Infrastructure.Updates;
using Velopack;

namespace Editor.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        string? updateError = DesktopApplicationUpdater.WaitForPendingUpdate();
        string path = UserDataLocation.StatePath(args, DesktopApplicationUpdater.GetDistributionDirectory());
        DesktopApplicationUpdater updater = new(args);
        AppStateStore state = new(path, Path.GetDirectoryName(path));
        state.Ready.GetAwaiter().GetResult();
        string? startupError = string.Join(Environment.NewLine, new[] { updateError, state.StorageError }.Where(error => error is not null));
        if (startupError.Length == 0) { startupError = null; }
        if (state.StorageError is not null) { Console.Error.WriteLine(state.StorageError); }
        RecentFileHistory history = state.History;
        EditorPreferences preferences = state.Preferences;
        _ = AppBuilder.Configure(() => new App(new FileTextDocumentStore(), history, preferences, new LocalDocumentFileMonitor(), startupError, updater))
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
        _ = Task.WhenAll(history.FlushAsync(), preferences.FlushAsync()).Wait(TimeSpan.FromMilliseconds(250));
    }
}
