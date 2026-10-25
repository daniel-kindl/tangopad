using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Editor.App.Resources;

namespace Editor.App.Views.Dialogs;

internal sealed partial class AboutDialog : Window
{
    private static readonly CompositeFormat VersionFormat = CompositeFormat.Parse(PresentationResources.Text("Text.About.VersionFormat"));

    internal AboutDialog()
    {
        InitializeComponent();
        string version = typeof(App).Assembly.GetName().Version!.ToString(3);
        VersionText.Text = string.Format(CultureInfo.CurrentCulture, VersionFormat, version);
        CloseButton.Click += (_, _) => Close();
        ProjectButton.Click += async (_, _) => _ = await Launcher.LaunchUriAsync(new Uri("https://github.com/daniel-kindl/tangopad")).ConfigureAwait(true);
    }
}
