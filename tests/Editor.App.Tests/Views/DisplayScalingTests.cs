using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Editor.App.Views;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks document fidelity and command access during display-scale changes.</summary>
public sealed class DisplayScalingTests
{
    private static readonly double[] ScaleChanges = [1.0, 1.25, 1.5, 2.0, 1.0];

    /// <summary>Display changes preserve exact text, selection, undo, preview, and command bounds.</summary>
    /// <param name="width">The logical window width.</param>
    /// <param name="height">The logical window height.</param>
    /// <param name="preview">Whether the preview is open.</param>
    [Theory]
    [InlineData(400, 250, false)]
    [InlineData(400, 600, true)]
    [InlineData(640, 480, true)]
    public async Task DisplayChangesPreserveDocument(double width, double height, bool preview)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(async () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            _ = Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "source.md");
                await File.WriteAllTextAsync(path, "# Heading\r\nCafé λ😀\nlast\r").ConfigureAwait(true);
                EditorWindow window = new(new FileTextDocumentStore()) { Width = width, Height = height };
                window.Show();
                Assert.True(await window.ViewModel.OpenPathAsync(path).ConfigureAwait(true));
                window.Editor.Document.Insert(0, "!");
                window.Editor.Select(2, 3);
                int caret = window.Editor.CaretOffset;
                if (preview)
                {
                    window.PreviewToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await window.Preview.Work.ConfigureAwait(true);
                }

                foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                {
                    window.RequestedThemeVariant = theme;
                    foreach (double scale in ScaleChanges)
                    {
                        window.SetRenderScaling(scale);
                        Dispatcher.UIThread.RunJobs();
                        window.UpdateLayout();
                        Assert.Equal(scale, window.RenderScaling);
                        Assert.Equal("!# Heading\r\nCafé λ😀\nlast\r", window.Editor.Text);
                        Assert.Equal(2, window.Editor.SelectionStart);
                        Assert.Equal(3, window.Editor.SelectionLength);
                        Assert.Equal(caret, window.Editor.CaretOffset);
                        Assert.True(window.Editor.Document.UndoStack.CanUndo);
                        Assert.Equal(preview, window.PreviewScroll.IsVisible);
                        foreach (Control control in new Control[]
                        {
                            window.NewButton, window.OpenButton, window.SaveButton, window.SaveAsButton,
                            window.FindButton, window.ReplaceButton, window.LineNumbersToggle, window.WordWrapToggle,
                            window.PreviewToggle, window.CaretStatus, window.EncodingStatus, window.LineEndingStatus,
                        })
                        {
                            Point origin = Assert.NotNull(control.TranslatePoint(default, window));
                            Point edge = Assert.NotNull(control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window));
                            Assert.True(origin.X >= 0 && origin.Y >= 0 && edge.X <= window.ClientSize.Width + 1 && edge.Y <= window.ClientSize.Height + 1,
                                $"{control.Name} is outside the window at {scale} scaling.");
                        }
                    }
                }

                window.Editor.Document.UndoStack.Undo();
                Assert.Equal("# Heading\r\nCafé λ😀\nlast\r", window.Editor.Text);
                Assert.False(window.ViewModel.IsDirty);
                Assert.Equal("# Heading\r\nCafé λ😀\nlast\r", await File.ReadAllTextAsync(path).ConfigureAwait(true));
                window.Close();
                Dispatcher.UIThread.RunJobs();
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }
}
