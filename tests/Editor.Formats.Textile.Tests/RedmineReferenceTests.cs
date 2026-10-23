using Editor.Preview;
using Xunit;

namespace Editor.Formats.Textile.Tests;

/// <summary>Checks inactive Redmine references and local-only macro output.</summary>
public sealed class RedmineReferenceTests
{
    /// <summary>Wiki links display their caption or local page title without a destination.</summary>
    /// <param name="source">The source reference.</param>
    /// <param name="expected">The literal display label.</param>
    [Theory]
    [InlineData("[[WikiPage]]", "WikiPage")]
    [InlineData("[[Page|Caption]]", "Caption")]
    [InlineData("[[Page|*Caption*]]", "*Caption*")]
    [InlineData("[[sandbox:Some page#Section]]", "Some page")]
    [InlineData("[[sandbox:Page|Project caption]]", "Project caption")]
    [InlineData("[[sandbox:]]", "sandbox:")]
    [InlineData("[[#section]]", "#section")]
    [InlineData("[[Page|Caption\\|more]]", "Caption|more")]
    [InlineData("[[Outer|[[Inner|Caption]]]]", "[[Inner|Caption]]")]
    public void DisplaysWikiLabels(string source, string expected)
    {
        Assert.Equal(expected, Text(TextilePreviewParser.Parse(source)));
    }

    /// <summary>Issue references stay exact because no server metadata is available.</summary>
    [Fact]
    public void PreservesIssueReferences()
    {
        const string source = "#123 ##123 #123-6 #123#note-6 #note-6";
        Assert.Equal(source, Text(TextilePreviewParser.Parse(source)));
    }

    /// <summary>Images and attachments expose only inactive names or labels.</summary>
    [Fact]
    public void DisplaysAttachmentNames()
    {
        PreviewDocument result = TextilePreviewParser.Parse("attachment:file.zip attachment:\"file name.zip\" !image.png! !remote.png(Label)!");
        Assert.Equal("file.zip file name.zip image.png Label", Text(result));
    }

    /// <summary>Server and plugin macros become named placeholders without evaluating their arguments.</summary>
    /// <param name="source">The macro source.</param>
    /// <param name="expected">The expected placeholder.</param>
    [Theory]
    [InlineData("{{include(Project:Page)}}", "[macro: include]")]
    [InlineData("{{collapse(Details)\nh1. Hidden\n{{include(Remote)}}\n}}", "[macro: collapse]")]
    [InlineData("{{child_pages}}", "[macro: child_pages]")]
    [InlineData("{{thumbnail(file:///secret)}}", "[macro: thumbnail]")]
    [InlineData("{{custom_plugin(<script>evil</script>)}}", "[macro: custom_plugin]")]
    public void NamesInactiveMacros(string source, string expected)
    {
        Assert.Equal(expected, Text(TextilePreviewParser.Parse(source)));
    }

    /// <summary>The contents list uses only parsed headings, including headings after the macro.</summary>
    [Fact]
    public void BuildsLocalContents()
    {
        const string source = "{{toc}}\n\nh1. *Overview*\n\nh3. [[Project:Details|Details]]\n\nbc. h2. Code\n\n{{collapse\nh2. Hidden\n}}\n\nh2. Finish";
        PreviewDocument result = TextilePreviewParser.Parse(source);
        Assert.False(result.IsLimited);
        Assert.Equal("• Overview\n    • Details\n  • Finish", string.Concat(Assert.IsType<PreviewTextBlock>(result.Blocks[0]).Runs.Select(run => run.Text)));
        Assert.Equal("[toc: no local headings]", Text(TextilePreviewParser.Parse("{{toc}}")));
    }

    /// <summary>A macro after paragraph text cannot expose its body as local headings.</summary>
    [Fact]
    public void ParagraphMacrosKeepTheirBodiesOpaque()
    {
        const string source = "{{toc}}\n\nh1. Visible\n\nordinary text\n{{child_pages}} {{collapse(Details)\nh2. Hidden\n{{include(Remote)}}\n}}\n\nh2. Finish";
        PreviewDocument result = TextilePreviewParser.Parse(source);
        Assert.False(result.IsLimited);
        Assert.Equal("• Visible\n  • Finish", string.Concat(Assert.IsType<PreviewTextBlock>(result.Blocks[0]).Runs.Select(run => run.Text)));
        Assert.DoesNotContain("Hidden", Text(result), StringComparison.Ordinal);
        Assert.Contains("ordinary text\n[macro: child_pages] [macro: collapse]", Text(result), StringComparison.Ordinal);
    }

    /// <summary>Escaped references and code remain literal instead of creating labels or placeholders.</summary>
    /// <param name="source">The escaped source.</param>
    /// <param name="expected">The expected literal text.</param>
    [Theory]
    [InlineData("![[Page|Caption]]", "[[Page|Caption]]")]
    [InlineData("\\[[Page|Caption]]", "[[Page|Caption]]")]
    [InlineData("!{{include(Page)}}", "{{include(Page)}}")]
    [InlineData("!{{collapse\nh1. Literal\n}}", "{{collapse\nh1. Literal\n}}")]
    [InlineData("!attachment:file.zip", "attachment:file.zip")]
    [InlineData("!#123", "#123")]
    [InlineData("@[[Page|Caption]] {{toc}}@", "[[Page|Caption]] {{toc}}")]
    [InlineData("==[[Page|Caption]] {{include(Page)}}==", "[[Page|Caption]] {{include(Page)}}")]
    [InlineData("[[]]", "[[]]")]
    [InlineData("[[|Caption]]", "[[|Caption]]")]
    [InlineData("[[Page|]]", "[[Page|]]")]
    [InlineData("!attachment:\"*file*\"", "attachment:\"*file*\"")]
    [InlineData("[[unfinished", "[[unfinished")]
    [InlineData("{{my-plugin(x)}}", "{{my-plugin(x)}}")]
    [InlineData("{{unfinished", "{{unfinished")]
    public void PreservesEscapedAndMalformedForms(string source, string expected)
    {
        Assert.Equal(expected, Text(TextilePreviewParser.Parse(source)));
    }

    /// <summary>Table delimiters inside references, macros, and inline code cannot split cells.</summary>
    [Fact]
    public void ParsesDifficultTable()
    {
        const string source = "|_. Reference|_. Content|\n|[[p:Page|A\\|B]]|{{include(Page|{{plugin(x)}})}}|\n|@x|y@|attachment:\"file|name.zip\"|\n|![[Page|Caption]]|==literal|pipe==|";
        PreviewTableBlock table = Assert.IsType<PreviewTableBlock>(Assert.Single(TextilePreviewParser.Parse(source).Blocks));
        Assert.All(table.Rows, row => Assert.Equal(2, row.Cells.Count));
        Assert.Equal(["Reference", "Content", "A|B", "[macro: include]", "x|y", "file|name.zip", "[[Page|Caption]]", "literal|pipe"], table.Rows.SelectMany(row => row.Cells).Select(cell => string.Concat(cell.Runs.Select(run => run.Text))));
    }

    /// <summary>Repeated contents lists cannot amplify output beyond the document text budget.</summary>
    [Fact]
    public void BoundsGeneratedContents()
    {
        PreviewDocument result = TextilePreviewParser.Parse("h1. " + new string('x', 1000) + "\n\n" + string.Join("\n\n", Enumerable.Repeat("{{toc}}", 100)));
        Assert.True(result.IsLimited);
        Assert.InRange(result.Blocks.Cast<PreviewTextBlock>().Sum(block => block.Runs.Sum(run => run.Text.Length)), 1000, 32_768);
    }

    private static string Text(PreviewDocument document)
    {
        return string.Join("\n", document.Blocks.Cast<PreviewTextBlock>().Select(block => string.Concat(block.Runs.Select(run => run.Text))));
    }
}
