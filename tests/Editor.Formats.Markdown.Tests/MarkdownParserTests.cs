using Editor.Preview;
using Xunit;

namespace Editor.Formats.Markdown.Tests;

/// <summary>Checks the framework-independent, inert preview representation.</summary>
public sealed class MarkdownParserTests
{
    /// <summary>Basic Markdown becomes structured text without HTML rendering.</summary>
    [Fact]
    public void ParsesBaseline()
    {
        PreviewDocument result = MarkdownPreviewParser.Parse("# Heading\n\nA *small* **strong** [link](https://example.com) and `code`.\n\n> quote\n\n- first\n- second\n\n3. third\n\n```cs\nvar x = 1;\n```\n\n---");
        Assert.False(result.IsLimited);
        Assert.Equal(1, Assert.IsType<PreviewTextBlock>(result.Blocks[0]).HeadingLevel);
        Assert.Equal("Heading", Assert.IsType<PreviewTextBlock>(result.Blocks[0]).Runs[0].Text);
        Assert.Contains(Assert.IsType<PreviewTextBlock>(result.Blocks[1]).Runs, run => run.Text == "small" && run.Emphasis && !run.Strong);
        Assert.Contains(Assert.IsType<PreviewTextBlock>(result.Blocks[1]).Runs, run => run.Text == "strong" && run.Strong);
        Assert.Contains(Assert.IsType<PreviewTextBlock>(result.Blocks[1]).Runs, run => run.Text == "link" && run.Link);
        Assert.Contains(Assert.IsType<PreviewTextBlock>(result.Blocks[1]).Runs, run => run.Text == "code" && run.Code);
        Assert.Equal("› ", Assert.IsType<PreviewTextBlock>(result.Blocks[2]).Prefix);
        Assert.Equal("• ", Assert.IsType<PreviewTextBlock>(result.Blocks[3]).Prefix);
        Assert.Equal("• ", Assert.IsType<PreviewTextBlock>(result.Blocks[4]).Prefix);
        Assert.Equal("3. ", Assert.IsType<PreviewTextBlock>(result.Blocks[5]).Prefix);
        Assert.True(Assert.IsType<PreviewTextBlock>(result.Blocks[6]).Code);
        Assert.Equal("var x = 1;", Assert.IsType<PreviewTextBlock>(result.Blocks[6]).Runs[0].Text);
        Assert.True(Assert.IsType<PreviewTextBlock>(result.Blocks[7]).ThematicBreak);
    }

    /// <summary>Resource references and HTML remain inert text with no resource-loading representation.</summary>
    [Fact]
    public void HostileContentHasOnlyText()
    {
        const string source = "<script>alert(1)</script>\n\n<iframe src='file:///etc/passwd'></iframe>\n\n![remote](https://127.0.0.1:1/tracker) ![local](file:///etc/passwd) [bad](javascript:alert%281%29) [data](data:text/html,evil)";
        PreviewDocument result = MarkdownPreviewParser.Parse(source);
        string text = string.Concat(result.Blocks.SelectMany(block => Assert.IsType<PreviewTextBlock>(block).Runs).Select(run => run.Text));
        Assert.Contains("<script>alert(1)</script>", text, StringComparison.Ordinal);
        Assert.Contains("<iframe", text, StringComparison.Ordinal);
        Assert.Contains("remote", text, StringComparison.Ordinal);
        Assert.Contains("local", text, StringComparison.Ordinal);
        Assert.DoesNotContain("tracker", text, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("data:text", text, StringComparison.Ordinal);
    }

    /// <summary>Input and output budgets prevent unbounded rendering.</summary>
    [Fact]
    public void BoundsLargeAndDeepInput()
    {
        PreviewDocument large = MarkdownPreviewParser.Parse(new string('x', 32_769));
        Assert.True(large.IsLimited);
        Assert.Empty(large.Blocks);
        PreviewDocument many = MarkdownPreviewParser.Parse(string.Concat(Enumerable.Repeat("p\n\n", 1000)));
        Assert.True(many.IsLimited);
        Assert.InRange(many.Blocks.Count, 1, 256);
        PreviewDocument runs = MarkdownPreviewParser.Parse(string.Concat(Enumerable.Repeat("`a` ", 1500)));
        Assert.True(runs.IsLimited);
        Assert.InRange(runs.Blocks.Sum(block => Assert.IsType<PreviewTextBlock>(block).Runs.Count), 1, 2048);
        PreviewDocument deep = MarkdownPreviewParser.Parse(new string('>', 200) + " nested");
        Assert.True(deep.IsLimited);
        Assert.Empty(deep.Blocks);
    }

    /// <summary>Malformed input and Unicode remain readable.</summary>
    [Fact]
    public void AcceptsMalformedText()
    {
        PreviewDocument result = MarkdownPreviewParser.Parse("**unfinished [link](\n\n```\n<raw> 🥔 e\u0301");
        Assert.False(result.IsLimited);
        Assert.Equal("**unfinished [link](", string.Concat(Assert.IsType<PreviewTextBlock>(result.Blocks[0]).Runs.Select(run => run.Text)));
        Assert.Equal("<raw> 🥔 e\u0301", Assert.IsType<PreviewTextBlock>(result.Blocks[1]).Runs[0].Text);
    }
}
