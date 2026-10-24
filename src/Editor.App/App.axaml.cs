using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Editor.App.Services;
using Editor.App.Views;
using Editor.Application;
using Editor.Infrastructure;

namespace Editor.App;

internal sealed partial class App(ITextDocumentStore store, RecentFileHistory? recentFiles = null, EditorPreferences? editorPreferences = null, IDocumentFileMonitor? fileMonitor = null, string? storageError = null) : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            EditorWindow window = new(store, recentFiles: recentFiles, editorPreferences: editorPreferences, fileMonitor: fileMonitor);
            desktop.MainWindow = window;
            if (storageError is not null)
            {
                window.Opened += async (_, _) => await new DesktopEditorDialogs(window).ShowErrorAsync(storageError).ConfigureAwait(true);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
