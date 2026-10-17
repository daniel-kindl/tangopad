using Avalonia.Styling;

namespace Editor.App;

internal static class PresentationResources
{
    internal static string Text(string key)
    {
        return Avalonia.Application.Current!.TryGetResource(key, ThemeVariant.Default, out object? value) && value is string text
            ? text
            : throw new InvalidOperationException($"The text resource is missing: {key}");
    }

    internal static EditorLabels EditorLabels => new(
        Text("Text.Document.Untitled"), Text("Text.Window.TitleFormat"), Text("Text.Document.DirtyMarker"));
}
