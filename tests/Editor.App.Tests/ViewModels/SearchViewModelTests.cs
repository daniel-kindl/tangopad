using Editor.App.ViewModels;
using Xunit;

namespace Editor.App.Tests.ViewModels;

/// <summary>Checks literal search commands without visual controls.</summary>
public sealed class SearchViewModelTests
{
    /// <summary>Both directions wrap through Unicode and overlapping matches.</summary>
    [Fact]
    public void NavigationWrapsAndTracksSelection()
    {
        Buffer buffer = new("😀 banana λ\r\nλ");
        SearchViewModel search = new(buffer) { Query = "ana" };
        search.ShowFindCommand.Execute(null);
        Assert.Equal(2, search.MatchCount);
        search.NextCommand.Execute(null);
        Assert.Equal(4, buffer.SelectionStart);
        search.NextCommand.Execute(null);
        Assert.Equal(6, buffer.SelectionStart);
        search.NextCommand.Execute(null);
        Assert.Equal(4, buffer.SelectionStart);
        search.PreviousCommand.Execute(null);
        Assert.Equal(6, buffer.SelectionStart);
        search.Query = "λ";
        search.NextCommand.Execute(null);
        Assert.Equal(10, buffer.SelectionStart);
        search.NextCommand.Execute(null);
        Assert.Equal(13, buffer.SelectionStart);
    }

    /// <summary>Replacement is literal, non-overlapping, and reports the actual count.</summary>
    [Theory]
    [InlineData("banana", "ana", "$1\\", "b$1\\na", 1)]
    [InlineData("λ\r\nλ\nλ\rλ", "λ", "😀\r\n$&", "😀\r\n$&\r\n😀\r\n$&\n😀\r\n$&\r😀\r\n$&", 4)]
    [InlineData("aAa", "a", "aa", "aaaaaa", 3)]
    [InlineData("one", "", "x", "one", 0)]
    [InlineData("one", "absent", "x", "one", 0)]
    public void ReplaceAllUsesLiteralText(string text, string query, string replacement, string expected, int count)
    {
        Buffer buffer = new(text);
        SearchViewModel search = new(buffer) { Query = query, Replacement = replacement };
        search.ShowReplaceCommand.Execute(null);
        search.ReplaceAllCommand.Execute(null);
        Assert.Equal(expected, buffer.Text);
        Assert.Equal(count, search.ReplacementCount);
    }

    /// <summary>Current replacement requires a valid selection and editing permission.</summary>
    [Fact]
    public void CommandsFollowQuerySelectionAndEditingState()
    {
        Buffer buffer = new("One one");
        SearchViewModel search = new(buffer);
        search.ShowReplaceCommand.Execute(null);
        Assert.False(search.NextCommand.CanExecute(null));
        search.Query = "one";
        search.MatchCase = true;
        Assert.Equal(1, search.MatchCount);
        Assert.False(search.ReplaceCurrentCommand.CanExecute(null));
        search.NextCommand.Execute(null);
        Assert.True(search.ReplaceCurrentCommand.CanExecute(null));
        search.CanEdit = false;
        Assert.False(search.ReplaceAllCommand.CanExecute(null));
        Assert.False(search.ReplaceCurrentCommand.CanExecute(null));
        search.CanEdit = true;
        search.Replacement = "two";
        search.ReplaceCurrentCommand.Execute(null);
        Assert.Equal("One two", buffer.Text);
        Assert.Equal(1, search.ReplacementCount);
        Assert.True(search.NoMatches);
        search.Query = "two";
        buffer.Select(0, 0);
        Assert.False(search.ReplaceCurrentCommand.CanExecute(null));
        search.CloseCommand.Execute(null);
        Assert.False(search.IsOpen);
        Assert.False(search.NextCommand.CanExecute(null));
        Assert.Equal("One two", buffer.Text);
    }

    /// <summary>Multiline search preserves each line ending and case comparison is ordinal.</summary>
    [Theory]
    [InlineData("A\r\nβ", "a\r\nβ", false, 1)]
    [InlineData("A\nβ", "a\nβ", true, 0)]
    [InlineData("A\rβ", "a\rβ", false, 1)]
    [InlineData("I ı İ i", "i", false, 2)]
    public void SearchUsesExactLineEndings(string text, string query, bool matchCase, int expected)
    {
        SearchViewModel search = new(new Buffer(text)) { Query = query, MatchCase = matchCase };
        Assert.Equal(expected, search.MatchCount);
    }

    private sealed class Buffer(string text) : ISearchBuffer
    {
        public event EventHandler? Changed;
        public string Text { get; private set; } = text;
        public int SelectionStart { get; private set; }
        public int SelectionLength { get; private set; }
        public void Select(int start, int length)
        {
            SelectionStart = start;
            SelectionLength = length;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Replace(IReadOnlyList<SearchMatch> matches, string replacement)
        {
            foreach (SearchMatch match in matches.Reverse())
            {
                Text = Text.Remove(match.Start, match.Length).Insert(match.Start, replacement);
            }

            Select(0, 0);
        }
    }
}
