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

internal sealed class MarkdownHighlightingAdapter
{
    private readonly TextEditor editor;
    private readonly EditorViewModel viewModel;
    private readonly IBrush plainTextLinkBrush;

    internal MarkdownHighlightingAdapter(TextEditor editor, EditorViewModel viewModel)
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
        if (viewModel.Format != DocumentFormat.Markdown)
        {
            editor.SyntaxHighlighting = null;
            editor.TextArea.TextView.LinkTextForegroundBrush = plainTextLinkBrush;
            return;
        }

        using Stream stream = typeof(MarkdownHighlightingAdapter).Assembly.GetManifestResourceStream("Editor.App.Views.Markdown.xshd")!;
        using XmlReader reader = XmlReader.Create(stream);
        IHighlightingDefinition definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        foreach (HighlightingColor color in definition.NamedHighlightingColors)
        {
            ISolidColorBrush brush = (ISolidColorBrush)editor.FindResource(editor.ActualThemeVariant, $"Brush.Markdown.{color.Name}")!;
            color.Foreground = new SimpleHighlightingBrush(brush.Color);
        }

        editor.SyntaxHighlighting = definition;
        editor.TextArea.TextView.LinkTextForegroundBrush = definition.GetNamedColor("Link").Foreground.GetBrush(null);
    }

    internal void Detach()
    {
        viewModel.PropertyChanged -= OnStateChanged;
        editor.ActualThemeVariantChanged -= OnThemeChanged;
    }
}
