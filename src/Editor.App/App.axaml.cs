using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Editor.App.Views;
using Editor.Application;
using Editor.Infrastructure;

namespace Editor.App;

internal sealed partial class App(ITextDocumentStore store, RecentFileHistory? recentFiles = null) : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new EditorWindow(store, recentFiles: recentFiles);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
