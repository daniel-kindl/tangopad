using System.ComponentModel;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using Editor.App.ViewModels;
using Editor.Core;

namespace Editor.App.Views;

internal sealed class SourceHighlightingAdapter
{
    private readonly TextEditor editor;
    private readonly EditorViewModel viewModel;
    private readonly IBrush plainTextLinkBrush;

    internal SourceHighlightingAdapter(TextEditor editor, EditorViewModel viewModel)
    {
        this.editor = editor;
        this.viewModel = viewModel;
        plainTextLinkBrush = editor.TextArea.TextView.LinkTextForegroundBrush;
        viewModel.PropertyChanged += OnStateChanged;
        editor.ActualThemeVariantChanged += OnThemeChanged;
        Refresh();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(EditorViewModel.Format))
        {
            Refresh();
        }
    }

    private void OnThemeChanged(object? sender, EventArgs args)
    {
        Refresh();
    }

    private void Refresh()
    {
        string? format = viewModel.Format switch
        {
            DocumentFormat.Markdown => "Markdown",
            DocumentFormat.Textile => "Textile",
            DocumentFormat.Json => "Json",
            DocumentFormat.Yaml => "Yaml",
            DocumentFormat.Csv => "Csv",
            DocumentFormat.Tsv => "Tsv",
            DocumentFormat.Xml or DocumentFormat.Xaml => "XML",
            DocumentFormat.PlainText => null,
            _ => null,
        };
        if (format is null)
        {
            editor.SyntaxHighlighting = null;
            editor.TextArea.TextView.LinkTextForegroundBrush = plainTextLinkBrush;
            return;
        }

        using Stream stream = format == "XML"
            ? typeof(HighlightingManager).Assembly.GetManifestResourceStream(
                "AvaloniaEdit.Highlighting.Resources.XML-Mode.xshd")!
            : typeof(SourceHighlightingAdapter).Assembly.GetManifestResourceStream($"Editor.App.Views.{format}.xshd")!;
        using XmlReader reader = XmlReader.Create(stream);
        IHighlightingDefinition definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        foreach (HighlightingColor color in definition.NamedHighlightingColors)
        {
            ISolidColorBrush brush = (ISolidColorBrush)editor.FindResource(editor.ActualThemeVariant, $"Brush.{format}.{color.Name}")!;
            color.Foreground = new SimpleHighlightingBrush(brush.Color);
        }

        editor.SyntaxHighlighting = definition;
        editor.TextArea.TextView.LinkTextForegroundBrush = definition.GetNamedColor("Link")?.Foreground.GetBrush(null) ?? plainTextLinkBrush;
    }

    internal void Detach()
    {
        viewModel.PropertyChanged -= OnStateChanged;
        editor.ActualThemeVariantChanged -= OnThemeChanged;
    }
}
