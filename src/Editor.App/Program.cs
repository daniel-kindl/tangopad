using Avalonia;
using Editor.Infrastructure;

namespace Editor.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        RecentFileHistory history = RecentFileHistory.ForCurrentUser();
        EditorPreferences preferences = EditorPreferences.ForCurrentUser();
        _ = AppBuilder.Configure(() => new App(new FileTextDocumentStore(), history, preferences))
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
        _ = Task.WhenAll(history.FlushAsync(), preferences.FlushAsync()).Wait(TimeSpan.FromMilliseconds(250));
    }
}
