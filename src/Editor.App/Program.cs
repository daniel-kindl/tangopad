using Avalonia;
using Editor.Infrastructure;

namespace Editor.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string path = UserDataLocation.StatePath(args);
        AppStateStore state = new(path, Path.GetDirectoryName(path));
        state.Ready.GetAwaiter().GetResult();
        if (state.StorageError is not null) { Console.Error.WriteLine(state.StorageError); }
        RecentFileHistory history = state.History;
        EditorPreferences preferences = state.Preferences;
        _ = AppBuilder.Configure(() => new App(new FileTextDocumentStore(), history, preferences, new LocalDocumentFileMonitor(), state.StorageError))
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
        _ = Task.WhenAll(history.FlushAsync(), preferences.FlushAsync()).Wait(TimeSpan.FromMilliseconds(250));
    }
}
