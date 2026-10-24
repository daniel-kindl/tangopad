using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Editor.App.Views;
using Editor.Application.Updates;
using Editor.Infrastructure;
using Xunit;

namespace Editor.App.Tests.Views;

/// <summary>Checks editor control placement when update controls are available.</summary>
public sealed class UpdateLayoutTests
{
    /// <summary>The update entry point does not move existing toolbar controls or the editor's first line.</summary>
    [Fact]
    public async Task UpdatesPreserveEditorAndToolbarPositions()
    {
        HeadlessUnitTestSession ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorBindingTests).Assembly);
        _ = await ui.Dispatch(() =>
        {
            EditorWindow baseline = new(new FileTextDocumentStore());
            baseline.Show();
            baseline.UpdateLayout();
            Point? wrap = baseline.FindControl<CheckBox>("WordWrapToggle")!.TranslatePoint(default, baseline);
            Point? numbers = baseline.FindControl<CheckBox>("LineNumbersToggle")!.TranslatePoint(default, baseline);
            Point? editor = baseline.Editor.TranslatePoint(default, baseline);
            baseline.Close();
            Dispatcher.UIThread.RunJobs();

            EditorWindow updated = new(new FileTextDocumentStore(), updater: new Updater());
            updated.Show();
            updated.UpdateLayout();
            Assert.Equal(wrap, updated.FindControl<CheckBox>("WordWrapToggle")!.TranslatePoint(default, updated));
            Assert.Equal(numbers, updated.FindControl<CheckBox>("LineNumbersToggle")!.TranslatePoint(default, updated));
            Assert.Equal(editor, updated.Editor.TranslatePoint(default, updated));
            updated.Close();
            return true;
        }, TestContext.Current.CancellationToken).ConfigureAwait(true);
    }

    private sealed class Updater : IApplicationUpdater
    {
        public string InstalledVersion => "0.1.0";
        public bool IsSupported => false;
        public Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken) { throw new NotSupportedException(); }
        public Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken) { throw new NotSupportedException(); }
        public void ApplyAndRestart() { throw new NotSupportedException(); }
    }
}
