using Avalonia.Data.Converters;
using Avalonia.Styling;
using Editor.App.ViewModels;

namespace Editor.App.Resources;

internal static class PresentationResources
{
    public static FuncValueConverter<LineEndingStyle, string> LineEndingLabel { get; } = new(style => Text(style switch
    {
        LineEndingStyle.None => "Text.Status.LineEnding.None",
        LineEndingStyle.Lf => "Text.Status.LineEnding.Lf",
        LineEndingStyle.CrLf => "Text.Status.LineEnding.CrLf",
        LineEndingStyle.Cr => "Text.Status.LineEnding.Cr",
        LineEndingStyle.Mixed => "Text.Status.LineEnding.Mixed",
        _ => throw new ArgumentOutOfRangeException(nameof(style)),
    }));

    internal static string Text(string key)
    {
        return Avalonia.Application.Current!.TryGetResource(key, ThemeVariant.Default, out object? value) && value is string text
            ? text
            : throw new InvalidOperationException($"The text resource is missing: {key}");
    }

    internal static EditorLabels EditorLabels => new(
        Text("Text.Document.Untitled"), Text("Text.Window.TitleFormat"), Text("Text.Document.DirtyMarker"));
}
