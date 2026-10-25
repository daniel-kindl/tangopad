using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Editor.App.Views;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks the shell information exposed to assistive technology.</summary>
public sealed class AccessibilityAcceptanceTests
{
    /// <summary>Icon commands expose their names, roles, and enabled states.</summary>
    [Fact]
    public async Task IconCommandsExposeAccessibleNamesAndRoles()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore());
            window.Show();
            foreach ((Button button, string name) in new (Button, string)[]
            {
                (window.NewButton, "New"), (window.OpenButton, "Open"), (window.SaveButton, "Save"),
                (window.SaveAsButton, "Save As"), (window.ReloadButton, "Reload from Disk"),
                (window.RecentFilesButton, "Recent Files"), (window.UndoButton, "Undo"),
                (window.RedoButton, "Redo"), (window.FindButton, "Find"), (window.ReplaceButton, "Replace"),
            })
            {
                AutomationPeer peer = Assert.IsType<AutomationPeer>(ControlAutomationPeer.CreatePeerForElement(button), exactMatch: false);
                Assert.Equal(name, peer.GetName());
                Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
                Assert.Equal(button.IsEffectivelyEnabled, peer.IsEnabled());
            }

            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>The line-number toggle exposes the state changed with the keyboard.</summary>
    [Fact]
    public async Task ToggleStateFollowsKeyboardInput()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore());
            window.Show();
            AutomationPeer peer = Assert.IsType<AutomationPeer>(ControlAutomationPeer.CreatePeerForElement(window.LineNumbersToggle), exactMatch: false);
            Assert.Equal("Line numbers", peer.GetName());
            Assert.Equal(AutomationControlType.CheckBox, peer.GetAutomationControlType());
            IToggleProvider toggle = Assert.IsType<IToggleProvider>(peer.GetProvider<IToggleProvider>(), exactMatch: false);
            Assert.Equal(ToggleState.On, toggle.ToggleState);
            _ = window.LineNumbersToggle.Focus(NavigationMethod.Tab);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ToggleState.Off, toggle.ToggleState);
            Assert.False(window.Editor.ShowLineNumbers);
            Assert.True(window.LineNumbersToggle.IsFocused);
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>A checked icon has a visible outline without relying on its background tint.</summary>
    /// <param name="dark">Whether the window uses the dark theme.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckedToggleHasContrastingOutline(bool dark)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow window = new(new FileTextDocumentStore()) { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Border border = Assert.Single(window.LineNumbersToggle.GetVisualDescendants().OfType<Border>(), item => item.Name == "IconBackground");
            Assert.True(border.BorderThickness.Left >= 1, "The checked state needs an outline.");
            Color foreground = Assert.IsType<ISolidColorBrush>(border.BorderBrush, exactMatch: false).Color;
            Color background = Assert.IsType<ISolidColorBrush>(border.Background, exactMatch: false).Color;
            Assert.True(Contrast(foreground, background) >= 3, "The checked outline needs 3:1 contrast.");
            _ = window.LineNumbersToggle.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();
            Assert.True(border.BorderThickness.Left >= 2, "Keyboard focus needs a distinct outline.");
            Color focus = Assert.IsType<ISolidColorBrush>(border.BorderBrush, exactMatch: false).Color;
            Assert.True(Contrast(focus, background) >= 3, "The focus outline needs 3:1 contrast.");
            window.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    /// <summary>Shared text and icon colors meet their minimum contrast in both themes.</summary>
    /// <param name="foreground">The foreground brush role.</param>
    /// <param name="background">The background brush role.</param>
    /// <param name="minimum">The required contrast ratio.</param>
    [Theory]
    [InlineData("Text.Primary", "Canvas", 4.5)]
    [InlineData("Text.Muted", "Surface", 4.5)]
    [InlineData("Icon", "Surface", 3.0)]
    [InlineData("Accent.Text", "Accent", 4.5)]
    public async Task ThemeColorsMeetContrast(string foreground, string background, double minimum)
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Assert.True(Avalonia.Application.Current!.TryGetResource($"Brush.{foreground}", theme, out object? foregroundValue));
                Assert.True(Avalonia.Application.Current.TryGetResource($"Brush.{background}", theme, out object? backgroundValue));
                Color text = Assert.IsType<ISolidColorBrush>(foregroundValue, exactMatch: false).Color;
                Color surface = Assert.IsType<ISolidColorBrush>(backgroundValue, exactMatch: false).Color;
                Assert.Equal(255, text.A);
                Assert.Equal(255, surface.A);
                Assert.True(Contrast(text, surface) >= minimum, $"{foreground} on {background} needs {minimum}:1 contrast in {theme}.");
            }

            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private static double Contrast(Color foreground, Color background)
    {
        double light = Luminance(foreground);
        double dark = Luminance(background);
        return (Math.Max(light, dark) + 0.05) / (Math.Min(light, dark) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
    }
}
