using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Editor.App.Resources;
using Editor.App.ViewModels;
using Editor.Core;
using Editor.Formats.Markdown;

namespace Editor.App.Views;

internal sealed class MarkdownPreviewAdapter : IDisposable
{
    private readonly EditorWindow window;
    private readonly EditorViewModel viewModel;
    private readonly Func<string, PreviewDocument> parse;
    private readonly CancellationTokenSource lifetime = new();
    private long revision;
    private bool pending;
    private bool detached;

    internal MarkdownPreviewAdapter(EditorWindow window, Func<string, PreviewDocument>? parse = null)
    {
        this.window = window;
        viewModel = window.ViewModel;
        this.parse = parse ?? MarkdownPreviewParser.Parse;
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
        window.PreviewToggle.IsVisible = viewModel.Format == DocumentFormat.Markdown;
        if (!window.PreviewToggle.IsVisible)
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
        pending = window.PreviewScroll.IsVisible && viewModel.Format == DocumentFormat.Markdown && !detached;
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
                PreviewDocument document = await Task.Run(() => parse(source), lifetime.Token).ConfigureAwait(true);
                if (!detached && requested == revision && identity == viewModel.DocumentId && window.PreviewScroll.IsVisible)
                {
                    Render(document);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Closing the window cancels delays and discards any parser result.
        }
    }

    private void Render(PreviewDocument document)
    {
        window.PreviewBlocks.Children.Clear();
        foreach (PreviewBlock block in document.Blocks)
        {
            TextBlock text = new();
            text.Classes.Add("preview-block");
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

            text.Inlines!.Add(new Run(block.ThematicBreak ? "────────" : block.Prefix));
            foreach (PreviewRun run in block.Runs)
            {
                Run inline = new(run.Text)
                {
                    FontWeight = run.Strong || block.HeadingLevel > 0 ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = run.Emphasis ? FontStyle.Italic : FontStyle.Normal,
                };
                if (run.Code)
                {
                    inline.FontFamily = (FontFamily)window.FindResource("Font.Editor")!;
                }
                if (run.Link)
                {
                    inline.TextDecorations = TextDecorations.Underline;
                }
                text.Inlines.Add(inline);
            }
            window.PreviewBlocks.Children.Add(text);
        }
        if (document.IsLimited)
        {
            TextBlock message = new() { Text = PresentationResources.Text("Text.Preview.Limited") };
            message.Classes.Add("preview-block");
            window.PreviewBlocks.Children.Add(message);
        }
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
