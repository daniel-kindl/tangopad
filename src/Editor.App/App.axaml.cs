using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Editor.Application;

namespace Editor.App;

internal sealed partial class App(ITextDocumentStore store) : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new EditorWindow(store);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
