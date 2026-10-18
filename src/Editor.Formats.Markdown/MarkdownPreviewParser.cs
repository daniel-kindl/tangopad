using System.Globalization;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Editor.Formats.Markdown;

/// <summary>Converts Markdown to bounded text without HTML or resource loading.</summary>
public static class MarkdownPreviewParser
{
    /// <summary>The maximum input length in UTF-16 code units.</summary>
    public const int MaximumSourceLength = 32_768;
    private const int MaximumBlocks = 256;
    private const int MaximumRuns = 2048;

    /// <summary>Parses source text. Oversized or excessively nested input produces a limited, empty preview.</summary>
    /// <param name="source">The exact document text.</param>
    /// <returns>An inert preview representation.</returns>
    public static PreviewDocument Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length > MaximumSourceLength)
        {
            return new PreviewDocument([], true);
        }

        MarkdownPipeline pipeline = new MarkdownPipelineBuilder { MaximumNestingDepth = 32 }.DisableHtml().Build();
        MarkdownDocument document;
        try
        {
            document = Markdig.Markdown.Parse(source, pipeline);
        }
        catch (ArgumentException)
        {
            // Markdig rejects syntax trees that exceed the configured nesting depth.
            return new PreviewDocument([], true);
        }

        List<PreviewBlock> blocks = [];
        int remainingRuns = MaximumRuns;
        bool limited = false;
        Visit(document, "", 0);
        return new PreviewDocument(blocks.AsReadOnly(), limited);

        void Visit(ContainerBlock container, string prefix, int depth)
        {
            foreach (Block block in container)
            {
                if (blocks.Count >= MaximumBlocks || remainingRuns <= 0)
                {
                    limited = true;
                    return;
                }

                if (block is ListBlock list)
                {
                    int number = int.TryParse(list.OrderedStart, CultureInfo.InvariantCulture, out int start) ? start : 1;
                    foreach (ListItemBlock item in list.Cast<ListItemBlock>())
                    {
                        Visit(item, prefix + (list.IsOrdered ? $"{number.ToString(CultureInfo.InvariantCulture)}. " : "• "), depth + 1);
                        number++;
                    }
                }
                else if (block is ContainerBlock nested)
                {
                    Visit(nested, prefix + (block is QuoteBlock ? "› " : ""), depth + 1);
                }
                else if (block is LeafBlock leaf)
                {
                    List<PreviewRun> runs = [];
                    if (leaf is CodeBlock code)
                    {
                        runs.Add(new PreviewRun(code.Lines.ToString(), Code: true));
                        remainingRuns--;
                    }
                    else if (leaf.Inline is { } inline)
                    {
                        AddInline(inline, new PreviewRun(""), runs);
                    }

                    blocks.Add(new PreviewBlock(runs.AsReadOnly(), prefix, depth,
                        leaf is HeadingBlock heading ? heading.Level : 0, leaf is CodeBlock, leaf is ThematicBreakBlock));
                    prefix = "";
                }
            }
        }

        void AddInline(ContainerInline container, PreviewRun style, List<PreviewRun> runs)
        {
            foreach (Inline inline in container)
            {
                if (remainingRuns <= 0)
                {
                    limited = true;
                    return;
                }

                if (inline is ContainerInline nested)
                {
                    PreviewRun childStyle = inline switch
                    {
                        EmphasisInline emphasis => style with
                        {
                            Strong = style.Strong || emphasis.DelimiterCount >= 2,
                            Emphasis = style.Emphasis || emphasis.DelimiterCount % 2 == 1,
                        },
                        LinkInline link => style with { Link = !link.IsImage },
                        _ => style,
                    };
                    AddInline(nested, childStyle, runs);
                }
                else
                {
                    string text = inline switch
                    {
                        LiteralInline literal => literal.Content.ToString(),
                        CodeInline code => code.Content,
                        LineBreakInline => "\n",
                        AutolinkInline link => link.Url,
                        _ => "",
                    };
                    runs.Add(style with { Text = text, Code = style.Code || inline is CodeInline, Link = style.Link || inline is AutolinkInline });
                    remainingRuns--;
                }
            }
        }
    }
}
