using Editor.Preview;
using Xunit;

namespace Editor.Formats.Textile.Tests;

/// <summary>Checks bounded Textile parsing into inactive preview text.</summary>
public sealed class TextileParserTests
{
    /// <summary>The baseline preserves block order, explicit text, and inline styles.</summary>
    [Fact]
    public void ParsesBaseline()
    {
        const string source = "h1. Heading\n\np. A *bold* _italic_ +underlined+ -removed- @code@.\n\nbq. Quote\n\n* First\n** Nested\n# One\n# Two\n\nbc. a < b\n*literal*\n\npre. sample\n\n----";
        PreviewDocument result = TextilePreviewParser.Parse(source);
        Assert.False(result.IsLimited);
        PreviewTextBlock[] blocks = [.. result.Blocks.Cast<PreviewTextBlock>()];
        Assert.Equal(["Heading", "A bold italic underlined removed code.", "Quote", "First", "Nested", "One", "Two", "a < b\n*literal*", "sample", ""], blocks.Select(Text));
        Assert.Equal(1, blocks[0].HeadingLevel);
        Assert.Contains(blocks[1].Runs, run => run.Text == "bold" && run.Strong);
        Assert.Contains(blocks[1].Runs, run => run.Text == "italic" && run.Emphasis);
        Assert.Contains(blocks[1].Runs, run => run.Text == "underlined" && run.Underline);
        Assert.Contains(blocks[1].Runs, run => run.Text == "removed" && run.Strike);
        Assert.Contains(blocks[1].Runs, run => run.Text == "code" && run.Code);
        Assert.Equal("› ", blocks[2].Prefix);
        Assert.Equal(["• ", "• ", "1. ", "2. "], blocks[3..7].Select(block => block.Prefix));
        Assert.Equal([1, 2, 1, 1], blocks[3..7].Select(block => block.Depth));
        Assert.True(blocks[7].Code);
        Assert.True(blocks[8].Code);
        Assert.True(blocks[9].ThematicBreak);
    }

    /// <summary>All supported heading levels and ordinary line endings remain readable.</summary>
    [Fact]
    public void ParsesHeadingsAndParagraphs()
    {
        PreviewDocument headings = TextilePreviewParser.Parse(string.Join("\n\n", Enumerable.Range(1, 6).Select(level => $"h{level}. Heading {level}")));
        Assert.Equal([1, 2, 3, 4, 5, 6], headings.Blocks.Cast<PreviewTextBlock>().Select(block => block.HeadingLevel));
        Assert.Equal(["one\ntwo\nthree", "four"], TextilePreviewParser.Parse("one\r\ntwo\rthree\n\nfour").Blocks.Cast<PreviewTextBlock>().Select(Text));
    }

    /// <summary>Tables keep cell boundaries, headers, empty cells, and escaped pipes.</summary>
    [Fact]
    public void ParsesTables()
    {
        PreviewDocument result = TextilePreviewParser.Parse("|_. Name|_. Value|\n|*bold*|a\\|b|\n||empty|\n\nAfter");
        PreviewTableBlock table = Assert.IsType<PreviewTableBlock>(result.Blocks[0]);
        Assert.Equal(["Name", "Value", "bold", "a|b", "", "empty"], table.Rows.SelectMany(row => row.Cells).Select(cell => string.Concat(cell.Runs.Select(run => run.Text))));
        Assert.All(table.Rows[0].Cells, cell => Assert.True(cell.Heading));
        Assert.True(Assert.Single(table.Rows[1].Cells[0].Runs).Strong);
        Assert.Equal("After", Text(Assert.IsType<PreviewTextBlock>(result.Blocks[1])));
    }

    /// <summary>An escaped final pipe does not create a table without any cells.</summary>
    [Fact]
    public void PreservesMalformedTableText()
    {
        PreviewTextBlock block = Assert.IsType<PreviewTextBlock>(Assert.Single(TextilePreviewParser.Parse("|unfinished\\|").Blocks));
        Assert.Equal("|unfinished|", Text(block));
        PreviewTableBlock escapedSlash = Assert.IsType<PreviewTableBlock>(Assert.Single(TextilePreviewParser.Parse("|a\\\\|").Blocks));
        Assert.Equal("a\\", string.Concat(Assert.Single(Assert.Single(escapedSlash.Rows).Cells).Runs.Select(run => run.Text)));
    }

    /// <summary>New parent items and changed list kinds restart descendant numbering.</summary>
    [Fact]
    public void ResetsNestedListNumbers()
    {
        PreviewDocument result = TextilePreviewParser.Parse("# Parent\n## Child\n## Child\n# Next\n## Child\n* Unordered\n## Changed\n## Changed\n*# Mixed");
        Assert.Equal(["1. ", "1. ", "2. ", "2. ", "1. ", "• ", "1. ", "2. ", "1. "], result.Blocks.Cast<PreviewTextBlock>().Select(block => block.Prefix));
    }

    /// <summary>Nested styles combine, and code and literal escapes suppress formatting.</summary>
    [Fact]
    public void ParsesNestedAndLiteralText()
    {
        PreviewTextBlock block = Assert.IsType<PreviewTextBlock>(Assert.Single(TextilePreviewParser.Parse("*bold _and italic_* \\*literal\\* ==+plain+== @*code*@ <code>_raw_</code> <notextile>*raw*</notextile>").Blocks));
        Assert.Equal("bold and italic *literal* +plain+ *code* _raw_ *raw*", Text(block));
        Assert.Contains(block.Runs, run => run.Text == "and italic" && run.Strong && run.Emphasis);
        Assert.Contains(block.Runs, run => run.Text == "*code*" && run.Code && !run.Strong);
        Assert.Contains(block.Runs, run => run.Text == "_raw_" && run.Code && !run.Emphasis);
        Assert.DoesNotContain(block.Runs, run => run.Text == "+plain+" && run.Underline);
        Assert.Equal("well-known word_under_score C++ *unfinished", Text(Assert.IsType<PreviewTextBlock>(Assert.Single(TextilePreviewParser.Parse("well-known word_under_score C++ *unfinished").Blocks))));
    }

    /// <summary>Exact pre and code tags preserve multiline code without HTML execution.</summary>
    [Theory]
    [InlineData("<pre>\n*raw*\n<script>x</script>\n</pre>", "*raw*\n<script>x</script>")]
    [InlineData("<pre>one\ntwo</pre>", "one\ntwo")]
    [InlineData("<pre>\n\nfirst\n\nlast\n\n</pre>", "\nfirst\n\nlast\n")]
    [InlineData("<code>sample</code>", "sample")]
    public void ParsesLiteralCodeBlocks(string source, string expected)
    {
        PreviewTextBlock block = Assert.IsType<PreviewTextBlock>(Assert.Single(TextilePreviewParser.Parse(source).Blocks));
        Assert.True(block.Code);
        Assert.Equal(expected, Text(block));
    }

    /// <summary>References become text labels without retaining resource destinations.</summary>
    [Fact]
    public void ReferencesHaveNoDestinations()
    {
        PreviewTextBlock block = Assert.IsType<PreviewTextBlock>(Assert.Single(TextilePreviewParser.Parse("\"remote\":https://127.0.0.1:1/tracker \"local\":file:///secret \"script\":javascript:alert(1) !http://127.0.0.1/a.png(picture)! !file:///secret(local image)! !image.png!").Blocks));
        Assert.Equal("remote local script picture local image image.png", Text(block));
        Assert.Contains(block.Runs, run => run.Text == "remote" && run.Link);
        Assert.DoesNotContain(block.Runs, run => run.Text.Contains("tracker", StringComparison.Ordinal));
    }

    /// <summary>HTML, CSS attributes, and incomplete macros stay inactive literal text.</summary>
    [Theory]
    [InlineData("<script>*alert(1)*</script>")]
    [InlineData("<img src=\"file:///etc/passwd\" onerror=\"alert(1)\">")]
    [InlineData("h1{background:url(https://127.0.0.1/a)}. styled")]
    [InlineData("p(class#id). *text*")]
    [InlineData("p{color:red}. _text_")]
    [InlineData("{{outer({{unfinished}} *raw*")]
    public void PreservesInactiveSource(string source)
    {
        PreviewTextBlock block = Assert.IsType<PreviewTextBlock>(Assert.Single(TextilePreviewParser.Parse(source).Blocks));
        Assert.Equal(source, Text(block));
        Assert.DoesNotContain(block.Runs, run => run.Strong || run.Emphasis || run.Link);
    }

    /// <summary>Malformed UTF-16 and non-ASCII source remain exact text.</summary>
    [Fact]
    public void PreservesMalformedUnicode()
    {
        const string source = "λ 🥔 e\u0301 \ud800 \udfff";
        Assert.Equal(source, Text(Assert.IsType<PreviewTextBlock>(Assert.Single(TextilePreviewParser.Parse(source).Blocks))));
        Assert.Empty(TextilePreviewParser.Parse("").Blocks);
        _ = Assert.Throws<ArgumentNullException>(() => TextilePreviewParser.Parse(null!));
    }

    /// <summary>Input, blocks, runs, cells, and nesting have independent limits.</summary>
    [Fact]
    public void EnforcesResourceLimits()
    {
        PreviewDocument source = TextilePreviewParser.Parse(new string('x', 32_769));
        Assert.True(source.IsLimited);
        Assert.Empty(source.Blocks);
        PreviewDocument blocks = TextilePreviewParser.Parse(string.Concat(Enumerable.Repeat("p. paragraph\n\n", 300)));
        Assert.True(blocks.IsLimited);
        Assert.InRange(blocks.Blocks.Count, 1, 256);
        PreviewDocument runs = TextilePreviewParser.Parse(string.Concat(Enumerable.Repeat("@a@ ", 1500)));
        Assert.True(runs.IsLimited);
        Assert.InRange(runs.Blocks.Cast<PreviewTextBlock>().Sum(block => block.Runs.Count), 1, 2048);
        PreviewDocument rows = TextilePreviewParser.Parse(string.Concat(Enumerable.Repeat("|a|\n", 300)));
        Assert.True(rows.IsLimited);
        Assert.InRange(Assert.IsType<PreviewTableBlock>(Assert.Single(rows.Blocks)).Rows.Count, 1, 256);
        PreviewDocument cells = TextilePreviewParser.Parse("|" + string.Concat(Enumerable.Repeat("|", 1500)));
        Assert.True(cells.IsLimited);
        Assert.InRange(Assert.IsType<PreviewTableBlock>(Assert.Single(cells.Blocks)).Rows.Sum(row => row.Cells.Count), 1, 1024);
        PreviewDocument deep = TextilePreviewParser.Parse(new string('*', 33) + " nested");
        Assert.True(deep.IsLimited);
        Assert.Empty(deep.Blocks);
    }

    /// <summary>A table beyond the block budget does not create an empty table record.</summary>
    [Fact]
    public void DoesNotEmitEmptyLimitedTable()
    {
        PreviewDocument result = TextilePreviewParser.Parse(string.Concat(Enumerable.Repeat("p. x\n\n", 255)) + "|cell|");
        Assert.True(result.IsLimited);
        Assert.Equal(255, result.Blocks.Count);
        Assert.All(result.Blocks, block => Assert.IsType<PreviewTextBlock>(block));
        PreviewTableBlock emptyCell = Assert.IsType<PreviewTableBlock>(Assert.Single(TextilePreviewParser.Parse("||").Blocks));
        Assert.Equal("", string.Concat(Assert.Single(Assert.Single(emptyCell.Rows).Cells).Runs.Select(run => run.Text)));
    }

    /// <summary>Delimiter-heavy malformed input terminates within the scan budget.</summary>
    [Fact]
    public void BoundsMalformedDelimiterSearches()
    {
        string source = string.Concat(Enumerable.Repeat("*a*b ", 6000));
        PreviewDocument result = TextilePreviewParser.Parse(source);
        Assert.True(result.IsLimited);
        Assert.InRange(result.Blocks.Count, 0, 256);
    }

    private static string Text(PreviewTextBlock block)
    {
        return string.Concat(block.Runs.Select(run => run.Text));
    }
}
