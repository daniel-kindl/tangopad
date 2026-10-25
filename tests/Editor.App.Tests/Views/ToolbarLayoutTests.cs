using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Editor.App.Views;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks command access when the editor window is narrow.</summary>
public sealed class ToolbarLayoutTests
{
    /// <summary>File and editing controls fit the window and leave space for the document.</summary>
    /// <param name="width">The window width in logical pixels.</param>
    /// <param name="height">The window height in logical pixels.</param>
    [Theory]
    [InlineData(400, 250)]
    [InlineData(640, 480)]
    [InlineData(1040, 760)]
    public async Task CommandsFitWindow(double width, double height)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore()) { Width = width, Height = height };
            window.Show();
            foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                foreach (Control control in new Control[]
                {
                    window.NewButton, window.OpenButton, window.RecentFilesButton, window.SaveButton,
                    window.SaveAsButton, window.ReloadButton, window.FindButton, window.ReplaceButton,
                    window.WordWrapToggle, window.LineNumbersToggle,
                })
                {
                    Point origin = Assert.NotNull(control.TranslatePoint(default, window));
                    Point edge = Assert.NotNull(control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window));
                    Assert.InRange(origin.X, 0, window.ClientSize.Width);
                    Assert.InRange(origin.Y, 0, window.ClientSize.Height);
                    Assert.InRange(edge.X, 0, window.ClientSize.Width);
                    Assert.True(edge.Y <= window.ClientSize.Height, $"{control.Name}: {origin} to {edge}, window {window.ClientSize}.");
                    Assert.True(control.Bounds.Height >= 32, $"{control.Name} has no usable target.");
                }

                Assert.True(window.Editor.Bounds.Height >= 32, "The document needs at least one visible line.");
                Assert.True(window.Editor.IsKeyboardFocusWithin);
            }

            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }
}
