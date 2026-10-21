using System.Diagnostics;
using System.Globalization;
using Editor.App.ViewModels;
using Xunit;

namespace Editor.App.Tests.ViewModels;

/// <summary>Checks bounded regex matching and capture replacements through search commands.</summary>
public sealed class RegexSearchViewModelTests
{
    /// <summary>Regex syntax matches exact UTF-16 source without normalization.</summary>
    [Theory]
    [InlineData("猫 café e\u0301 😀", @"\p{L}+", false, 3)]
    [InlineData("cat CAT Cat", "cat", false, 3)]
    [InlineData("cat CAT Cat", "cat", true, 1)]
    [InlineData("cat CAT Cat", "(?i)cat", true, 3)]
    [InlineData("cat CAT Cat", "(?-i)cat", false, 1)]
    [InlineData("a.b axb", @"a\.b", false, 1)]
    [InlineData("foo1 foo2 bar1", @"(?<=foo)\d(?!x)", false, 2)]
    [InlineData("cat\r\ncat\ncat\rcat", @"(?m)^cat\r?$", false, 2)]
    [InlineData("a\r\nb a\nb a\rb", @"a\r\nb", false, 1)]
    [InlineData("a\nb", "a.b", false, 0)]
    [InlineData("a\nb", "(?s)a.b", false, 1)]
    [InlineData("banana", "ana", false, 1)]
    [InlineData("", "cat", false, 0)]
    public async Task RegexCounts(string text, string query, bool matchCase, int expected)
    {
        using SearchViewModel search = new(new Buffer(text)) { Query = query, MatchCase = matchCase, UseRegex = true };
        await search.Work.ConfigureAwait(true);
        Assert.Equal(expected, search.MatchCount);
        Assert.False(search.HasSearchError);
    }

    /// <summary>Whole word applies the literal scalar filter after regex matching.</summary>
    [Fact]
    public async Task WholeWordAndCaseRemainIndependentOfRegexSyntax()
    {
        using SearchViewModel search = new(new Buffer("cat_cat Cat cat’s 😀cat😀 cat𐐀 e\u0301 e"))
        {
            Query = "(cat)",
            UseRegex = true,
            MatchWholeWord = true
        };
        await search.Work.ConfigureAwait(true);
        Assert.Equal(3, search.MatchCount);
        search.MatchCase = true;
        await search.Work.ConfigureAwait(true);
        Assert.Equal(2, search.MatchCount);
        search.Query = "e";
        await search.Work.ConfigureAwait(true);
        Assert.Equal(1, search.MatchCount);
    }

    /// <summary>Invariant regex case folding does not depend on the user's culture.</summary>
    [Fact]
    public async Task CaseFoldingIsCultureInvariant()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            using SearchViewModel search = new(new Buffer("I ı İ i")) { Query = "i", UseRegex = true };
            await search.Work.ConfigureAwait(true);
            Assert.Equal(2, search.MatchCount);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>Capture substitutions preserve Unicode and exact mixed line endings.</summary>
    [Theory]
    [InlineData("cat12 dog3", @"(?<word>\w+?)(\d+)", "${word}:$1:$$:$&", "cat:12:$:cat12 dog:3:$:dog3")]
    [InlineData("λ\r\nλ\nλ\rλ", "(λ)", "$1😀\\\r\n", "λ😀\\\r\n\r\nλ😀\\\r\n\nλ😀\\\r\n\rλ😀\\\r\n")]
    [InlineData("banana", "ana", "x", "bxna")]
    [InlineData("aaa", "(a)", "$1$1", "aaaaaa")]
    [InlineData("ab b", "(a)?b", "[$1]", "[a] []")]
    [InlineData("a", "(a)", "", "")]
    [InlineData("aaa", "(a)+", "$1", "a")]
    [InlineData("😀 😀", "(😀)", "$1$1", "😀😀 😀😀")]
    [InlineData("a", "(a)", "$0:${0}:${1}:$$", "a:a:a:$")]
    [InlineData("a", "(a)", @"\n", @"\n")]
    public async Task ReplaceAllExpandsCapturesOnce(string text, string query, string replacement, string expected)
    {
        Buffer buffer = new(text);
        using SearchViewModel search = new(buffer) { Query = query, Replacement = replacement, UseRegex = true };
        search.ShowReplaceCommand.Execute(null);
        await search.Work.ConfigureAwait(true);
        search.ReplaceAllCommand.Execute(null);
        Assert.Equal(expected, buffer.Text);
        Assert.Equal(1, buffer.EditCount);
        Assert.True(search.ReplacementCount > 0);
        await search.Work.ConfigureAwait(true);
    }

    /// <summary>Invalid or empty matches disable commands and preserve the source.</summary>
    [Theory]
    [InlineData("(", true, false)]
    [InlineData("[", true, false)]
    [InlineData("(?=ana)", false, true)]
    [InlineData("^", false, true)]
    [InlineData("$", false, true)]
    [InlineData("a*", false, true)]
    [InlineData("ana|$", false, true)]
    public async Task UnsafePatternsDiscardPartialResults(string query, bool invalid, bool empty)
    {
        Buffer buffer = new("banana");
        using SearchViewModel search = new(buffer) { Query = query, UseRegex = true, Replacement = "x" };
        search.ShowReplaceCommand.Execute(null);
        await search.Work.ConfigureAwait(true);
        Assert.Equal(invalid, search.InvalidPattern);
        Assert.Equal(empty, search.EmptyRegexMatch);
        Assert.Equal(0, search.MatchCount);
        Assert.False(search.NoMatches);
        Assert.False(search.NextCommand.CanExecute(null));
        Assert.False(search.ReplaceAllCommand.CanExecute(null));
        search.ReplaceAllCommand.Execute(null);
        Assert.Equal("banana", buffer.Text);
    }

    /// <summary>Invalid capture references disable replacement but preserve navigation.</summary>
    [Theory]
    [InlineData("$2")]
    [InlineData("${missing}")]
    [InlineData("${")]
    [InlineData("$")]
    [InlineData("$`")]
    public async Task ReplacementFailureIsAtomicAndRecoverable(string replacement)
    {
        Buffer buffer = new("cat cat");
        using SearchViewModel search = new(buffer) { Query = "(cat)", UseRegex = true, Replacement = replacement };
        search.ShowReplaceCommand.Execute(null);
        await search.Work.ConfigureAwait(true);
        Assert.True(search.InvalidReplacement);
        Assert.Equal(2, search.MatchCount);
        Assert.True(search.NextCommand.CanExecute(null));
        search.NextCommand.Execute(null);
        Assert.False(search.ReplaceCurrentCommand.CanExecute(null));
        search.ReplaceAllCommand.Execute(null);
        Assert.Equal("cat cat", buffer.Text);
        search.Replacement = "$1!";
        await search.Work.ConfigureAwait(true);
        Assert.False(search.InvalidReplacement);
        search.ReplaceCurrentCommand.Execute(null);
        Assert.Equal("cat! cat", buffer.Text);
        await search.Work.ConfigureAwait(true);
    }

    /// <summary>A backtracking timeout never publishes partial results.</summary>
    [Fact]
    public async Task HostilePatternTimesOutAndAllowsNewSearch()
    {
        Buffer buffer = new(new string('a', 30000) + "!");
        using SearchViewModel search = new(buffer) { UseRegex = true, Query = "(a+)+$" };
        search.ShowReplaceCommand.Execute(null);
        Assert.True(search.IsSearching);
        Assert.False(search.ReplaceAllCommand.CanExecute(null));
        await search.Work.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ConfigureAwait(true);
        Assert.True(search.SearchTimedOut);
        Assert.False(search.IsSearching);
        Assert.Equal(0, search.MatchCount);
        search.Query = "!";
        await search.Work.ConfigureAwait(true);
        Assert.Equal(1, search.MatchCount);
        Assert.False(search.HasSearchError);
    }

    /// <summary>New queries, modes, buffers, and close cancel obsolete worker results.</summary>
    [Fact]
    public async Task ReentryAndDetachDiscardOldResults()
    {
        Buffer buffer = new(new string('a', 30000) + "!");
        using SearchViewModel search = new(buffer) { UseRegex = true, Query = "(a+)+$" };
        Task old = search.Work;
        search.Query = "!";
        Task newer = search.Work;
        search.UseRegex = false;
        await Task.WhenAll(old, newer).ConfigureAwait(true);
        Assert.Equal(1, search.MatchCount);
        Assert.False(search.HasSearchError);
        search.UseRegex = true;
        old = search.Work;
        search.Attach(new Buffer("! !"));
        await search.Work.ConfigureAwait(true);
        await old.ConfigureAwait(true);
        Assert.Equal(2, search.MatchCount);
        search.Query = "(";
        old = search.Work;
        search.CloseCommand.Execute(null);
        await old.ConfigureAwait(true);
        Assert.False(search.IsSearching);
        search.ShowFindCommand.Execute(null);
        await search.Work.ConfigureAwait(true);
        Assert.True(search.InvalidPattern);
        search.Attach(null);
        await search.Work.ConfigureAwait(true);
        Assert.Equal(0, search.MatchCount);
    }

    /// <summary>Input, result, and output limits reject a request without editing text.</summary>
    [Theory]
    [InlineData(1000001, "a", "x")]
    [InlineData(20001, "a", "x")]
    [InlineData(100000, "(a+)", "$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1$1")]
    public async Task LimitsPreserveSource(int length, string query, string replacement)
    {
        string original = new('a', length);
        Buffer buffer = new(original);
        using SearchViewModel search = new(buffer) { Query = query, Replacement = replacement, UseRegex = true };
        search.ShowReplaceCommand.Execute(null);
        await search.Work.ConfigureAwait(true);
        Assert.True(search.SearchLimitReached);
        Assert.False(search.ReplaceAllCommand.CanExecute(null));
        search.ReplaceAllCommand.Execute(null);
        Assert.Equal(original, buffer.Text);
    }

    /// <summary>Oversized templates and patterns fail before matching.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TemplateAndPatternLengthsAreBounded(bool longPattern)
    {
        Buffer buffer = new("a");
        using SearchViewModel search = new(buffer)
        {
            Query = longPattern ? new string('a', 4097) : "a",
            Replacement = longPattern ? "x" : new string('x', 4097),
            UseRegex = true
        };
        search.ShowReplaceCommand.Execute(null);
        await search.Work.ConfigureAwait(true);
        Assert.True(search.SearchLimitReached);
        Assert.False(search.ReplaceAllCommand.CanExecute(null));
        Assert.Equal("a", buffer.Text);
    }

    /// <summary>Returning to literal mode restores literal queries and replacement strings.</summary>
    [Fact]
    public async Task ReturningToLiteralModeRestoresLiteralSemantics()
    {
        Buffer buffer = new("(a) a");
        using SearchViewModel search = new(buffer) { Query = "(a)", Replacement = "$1", UseRegex = true };
        search.ShowReplaceCommand.Execute(null);
        await search.Work.ConfigureAwait(true);
        Assert.Equal(2, search.MatchCount);
        search.UseRegex = false;
        Assert.Equal(1, search.MatchCount);
        search.ReplaceAllCommand.Execute(null);
        Assert.Equal("$1 a", buffer.Text);
        Assert.Equal(1, search.ReplacementCount);
    }

    /// <summary>Find ignores invalid or oversized replacement text until Replace is opened.</summary>
    [Fact]
    public async Task FindIgnoresHiddenReplacementFailures()
    {
        using SearchViewModel search = new(new Buffer("cat cat")) { Query = "(cat)", Replacement = "$2", UseRegex = true };
        search.ShowFindCommand.Execute(null);
        await search.Work.ConfigureAwait(true);
        Assert.Equal(2, search.MatchCount);
        Assert.False(search.HasSearchError);
        search.ShowReplaceCommand.Execute(null);
        await search.Work.ConfigureAwait(true);
        Assert.True(search.InvalidReplacement);
        Assert.False(search.ReplaceAllCommand.CanExecute(null));
        search.ShowFindCommand.Execute(null);
        search.Replacement = new string('x', 4097);
        await search.Work.ConfigureAwait(true);
        Assert.Equal(2, search.MatchCount);
        Assert.False(search.HasSearchError);
        Assert.True(search.NextCommand.CanExecute(null));
        search.ShowReplaceCommand.Execute(null);
        await search.Work.ConfigureAwait(true);
        Assert.True(search.SearchLimitReached);
        Assert.False(search.ReplaceAllCommand.CanExecute(null));
    }

    /// <summary>The worker handles representative large source within its safety budget.</summary>
    [Fact]
    public async Task LargeInputBenchmark()
    {
        Buffer buffer = new(string.Concat(Enumerable.Repeat("entry λ123\r\n", 15000)));
        using SearchViewModel search = new(buffer) { Query = @"λ(\d+)", Replacement = "value=$1", UseRegex = true };
        search.ShowReplaceCommand.Execute(null);
        Stopwatch elapsed = Stopwatch.StartNew();
        await search.Work.ConfigureAwait(true);
        Assert.Equal(15000, search.MatchCount);
        Assert.False(search.HasSearchError);
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(5));
        TestContext.Current.TestOutputHelper!.WriteLine($"Scan and replacement preparation: {buffer.Text.Length} UTF-16 units, {search.MatchCount} matches, {elapsed.Elapsed.TotalMilliseconds:F1} ms.");
    }

    private sealed class Buffer(string text) : ISearchBuffer
    {
        public event EventHandler? Changed;
        public string Text { get; private set; } = text;
        public int SelectionStart { get; private set; }
        public int SelectionLength { get; private set; }
        internal int EditCount { get; private set; }
        public void Select(int start, int length)
        {
            SelectionStart = start;
            SelectionLength = length;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Replace(IReadOnlyList<SearchReplacement> replacements)
        {
            EditCount++;
            foreach (SearchReplacement replacement in replacements.Reverse())
            {
                Text = Text.Remove(replacement.Match.Start, replacement.Match.Length).Insert(replacement.Match.Start, replacement.Text);
            }

            Select(replacements[0].Match.Start, replacements[0].Text.Length);
        }
    }
}
