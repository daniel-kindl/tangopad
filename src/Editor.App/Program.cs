using Avalonia;
using Editor.Infrastructure;

namespace Editor.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        _ = AppBuilder.Configure(() => new App(new FileTextDocumentStore()))
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
    }
}
