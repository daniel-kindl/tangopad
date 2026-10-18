using Avalonia;
using Editor.Infrastructure;

namespace Editor.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        RecentFileHistory history = RecentFileHistory.ForCurrentUser();
        _ = AppBuilder.Configure(() => new App(new FileTextDocumentStore(), history))
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
        _ = history.FlushAsync().Wait(TimeSpan.FromMilliseconds(250));
    }
}
