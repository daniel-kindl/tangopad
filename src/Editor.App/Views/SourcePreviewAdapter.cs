using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Editor.App.Resources;
using Editor.App.ViewModels;
using Editor.Core;
using Editor.Formats.Markdown;
using Editor.Formats.Textile;
using Editor.Preview;

namespace Editor.App.Views;

internal sealed class SourcePreviewAdapter : IDisposable
{
    private readonly EditorWindow window;
    private readonly EditorViewModel viewModel;
    private readonly Func<DocumentFormat, string, PreviewDocument> parse;
    private readonly CancellationTokenSource lifetime = new();
    private long revision;
    private bool pending;
    private bool detached;
    private DocumentFormat previewFormat;

    internal SourcePreviewAdapter(EditorWindow window, Func<DocumentFormat, string, PreviewDocument>? parse = null)
    {
        this.window = window;
        viewModel = window.ViewModel;
        this.parse = parse ?? ((format, source) => format switch
        {
            DocumentFormat.Markdown => MarkdownPreviewParser.Parse(source),
            DocumentFormat.Textile => TextilePreviewParser.Parse(source),
            DocumentFormat.PlainText => throw new InvalidOperationException("Plain text has no preview parser."),
            _ => throw new InvalidOperationException("The document format has no preview parser."),
        });
        window.PreviewToggle.Click += OnToggle;
        viewModel.PropertyChanged += OnStateChanged;
        RefreshFormat();
    }

    internal Task Work { get; private set; } = Task.CompletedTask;

    private void OnToggle(object? sender, RoutedEventArgs args)
    {
        SetVisible(!window.PreviewScroll.IsVisible);
        _ = window.Editor.TextArea.Focus();
    }

    private void OnStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(EditorViewModel.Format))
        {
            RefreshFormat();
        }
        else if (args.PropertyName is nameof(EditorViewModel.Text) or nameof(EditorViewModel.DocumentId))
        {
            Request();
        }
    }

    private void RefreshFormat()
    {
        bool changed = previewFormat != viewModel.Format;
        previewFormat = viewModel.Format;
        window.PreviewToggle.IsVisible = viewModel.Format is DocumentFormat.Markdown or DocumentFormat.Textile;
        AutomationProperties.SetName(window.PreviewScroll, PresentationResources.Text(
            viewModel.Format == DocumentFormat.Textile ? "Text.Preview.TextileName" : "Text.Preview.Name"));
        window.PreviewBlocks.Classes.Set("textile", viewModel.Format == DocumentFormat.Textile);
        if (changed || !window.PreviewToggle.IsVisible)
        {
            SetVisible(false);
        }
        else
        {
            Request();
        }
    }

    private void SetVisible(bool visible)
    {
        window.PreviewScroll.IsVisible = visible;
        window.PreviewSplitter.IsVisible = visible;
        window.DocumentLayout.ColumnDefinitions[2].Width = visible ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        window.DocumentLayout.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        window.PreviewToggle.Content = PresentationResources.Text(visible ? "Text.Preview.Hide" : "Text.Preview.Show");
        Request();
    }

    private void Request()
    {
        revision++;
        window.PreviewBlocks.Children.Clear();
        pending = window.PreviewScroll.IsVisible && window.PreviewToggle.IsVisible && !detached;
        if (pending && Work.IsCompleted)
        {
            Work = UpdateAsync();
        }
    }

    private async Task UpdateAsync()
    {
        try
        {
            while (pending)
            {
                long requested = revision;
                await Task.Delay(TimeSpan.FromMilliseconds(200), lifetime.Token).ConfigureAwait(true);
                if (requested != revision)
                {
                    continue;
                }

                pending = false;
                Guid identity = viewModel.DocumentId;
                string source = viewModel.Text;
                DocumentFormat format = viewModel.Format;
                try
                {
                    PreviewDocument document = await Task.Run(() => parse(format, source), lifetime.Token).ConfigureAwait(true);
                    if (IsCurrent(requested, identity, format))
                    {
                        Render(document);
                    }
                }
                catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)
                    && !(error is OperationCanceledException && lifetime.IsCancellationRequested))
                {
                    // A failed attempt must not stop newer requests in this serial loop.
                    if (IsCurrent(requested, identity, format))
                    {
                        window.PreviewBlocks.Children.Clear();
                        TextBlock message = new() { Text = PresentationResources.Text("Text.Preview.Unavailable") };
                        message.Classes.Add("preview-block");
                        AutomationProperties.SetName(message, message.Text);
                        AutomationProperties.SetLiveSetting(message, AutomationLiveSetting.Polite);
                        window.PreviewBlocks.Children.Add(message);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Closing the window cancels delays and discards any parser result.
        }
    }

    private bool IsCurrent(long requested, Guid identity, DocumentFormat format)
    {
        return !detached && requested == revision && identity == viewModel.DocumentId && format == viewModel.Format && window.PreviewScroll.IsVisible;
    }

    private void Render(PreviewDocument document)
    {
        window.PreviewBlocks.Children.Clear();
        foreach (PreviewBlock block in document.Blocks)
        {
            Control control = block switch
            {
                PreviewTextBlock text => RenderText(text),
                PreviewTableBlock table => RenderTable(table),
                _ => throw new InvalidOperationException("The preview block type is unsupported."),
            };
            window.PreviewBlocks.Children.Add(control);
        }
        if (document.IsLimited)
        {
            TextBlock message = new() { Text = PresentationResources.Text("Text.Preview.Limited") };
            message.Classes.Add("preview-block");
            window.PreviewBlocks.Children.Add(message);
        }
    }

    private TextBlock RenderText(PreviewTextBlock block)
    {
        TextBlock text = CreateText(block.Runs, block.HeadingLevel > 0);
        text.Padding = new Thickness(block.Depth * (double)window.FindResource("Size.Preview.Indent")!, 0, 0, 0);
        if (block.Code)
        {
            text.Classes.Add("preview-code");
        }
        if (block.HeadingLevel > 0)
        {
            text.Classes.Add("preview-heading");
            text.FontSize = (double)window.FindResource($"Size.Preview.Heading.{block.HeadingLevel}")!;
        }

        text.Inlines!.Insert(0, new Run(block.ThematicBreak ? "────────" : block.Prefix));
        return text;
    }

    private Grid RenderTable(PreviewTableBlock table)
    {
        Grid grid = new();
        grid.Classes.Add("preview-table");
        int columns = table.Rows.Max(row => row.Cells.Count);
        for (int column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        }
        for (int row = 0; row < table.Rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int column = 0; column < table.Rows[row].Cells.Count; column++)
            {
                PreviewTableCell cell = table.Rows[row].Cells[column];
                Border border = new() { Child = CreateText(cell.Runs, cell.Heading) };
                border.Classes.Add("preview-cell");
                Grid.SetRow(border, row);
                Grid.SetColumn(border, column);
                grid.Children.Add(border);
            }
        }
        return grid;
    }

    private TextBlock CreateText(IReadOnlyList<PreviewRun> runs, bool strong)
    {
        TextBlock text = new();
        text.Classes.Add("preview-block");
        foreach (PreviewRun run in runs)
        {
            Run inline = new(run.Text)
            {
                FontWeight = run.Strong || strong ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = run.Emphasis ? FontStyle.Italic : FontStyle.Normal,
            };
            if (run.Code)
            {
                inline.FontFamily = (FontFamily)window.FindResource("Font.Editor")!;
            }
            if (run.Link || run.Underline || run.Strike)
            {
                TextDecorationCollection decorations = [];
                if (run.Link || run.Underline)
                {
                    decorations.AddRange(TextDecorations.Underline);
                }
                if (run.Strike)
                {
                    decorations.AddRange(TextDecorations.Strikethrough);
                }
                inline.TextDecorations = decorations;
            }
            text.Inlines!.Add(inline);
        }
        return text;
    }

    public void Dispose()
    {
        if (detached)
        {
            return;
        }
        detached = true;
        revision++;
        pending = false;
        lifetime.Cancel();
        viewModel.PropertyChanged -= OnStateChanged;
        window.PreviewToggle.Click -= OnToggle;
        window.PreviewBlocks.Children.Clear();
        lifetime.Dispose();
    }
}
