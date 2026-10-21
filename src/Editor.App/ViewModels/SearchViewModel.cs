using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Editor.App.ViewModels;

internal sealed partial class SearchViewModel : ObservableObject
{
    private ISearchBuffer? buffer;
    private List<SearchMatch> matches = [];
    private string? scannedText;
    private string? scannedQuery;
    private bool scannedMatchCase;
    private bool scannedMatchWholeWord;

    internal SearchViewModel(ISearchBuffer? buffer = null)
    {
        Attach(buffer);
    }

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Replacement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool MatchCase { get; set; }

    [ObservableProperty]
    public partial bool MatchWholeWord { get; set; }

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial bool IsReplaceVisible { get; private set; }

    [ObservableProperty]
    public partial bool CanEdit { get; set; } = true;

    [ObservableProperty]
    public partial int MatchCount { get; private set; }

    [ObservableProperty]
    public partial int ReplacementCount { get; private set; }

    public bool HasQuery => Query.Length != 0;
    public bool NoMatches => HasQuery && MatchCount == 0;

    internal void Attach(ISearchBuffer? value)
    {
        buffer?.Changed -= OnBufferChanged;

        buffer = value;
        buffer?.Changed += OnBufferChanged;

        Refresh();
    }

    partial void OnQueryChanged(string value) { Refresh(); }
    partial void OnMatchCaseChanged(bool value) { Refresh(); }
    partial void OnMatchWholeWordChanged(bool value) { Refresh(); }
    partial void OnCanEditChanged(bool value) { NotifyCommands(); }
    partial void OnIsOpenChanged(bool value) { NotifyCommands(); }
    partial void OnIsReplaceVisibleChanged(bool value) { NotifyCommands(); }

    private void OnBufferChanged(object? sender, EventArgs args) { Refresh(); }

    private void Refresh()
    {
        string text = buffer?.Text ?? string.Empty;
        if (text != scannedText || Query != scannedQuery || MatchCase != scannedMatchCase || MatchWholeWord != scannedMatchWholeWord)
        {
            ReplacementCount = 0;
            scannedText = text;
            scannedQuery = Query;
            scannedMatchCase = MatchCase;
            scannedMatchWholeWord = MatchWholeWord;
            matches = [];
            if (buffer is not null && Query.Length != 0)
            {
                StringComparison comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                int offset = 0;
                while (offset <= text.Length - Query.Length)
                {
                    int start = text.IndexOf(Query, offset, comparison);
                    if (start < 0)
                    {
                        break;
                    }

                    if (!MatchWholeWord || IsWholeWordMatch(text, start, Query.Length))
                    {
                        matches.Add(new SearchMatch(start, Query.Length));
                    }

                    offset = start + 1;
                }
            }
        }

        MatchCount = matches.Count;
        OnPropertyChanged(nameof(HasQuery));
        OnPropertyChanged(nameof(NoMatches));
        NotifyCommands();
    }

    private static bool IsWholeWordMatch(string text, int start, int length)
    {
        int end = start + length;
        if (SplitsScalar(text, start) || SplitsScalar(text, end))
        {
            return false;
        }

        _ = Rune.DecodeLastFromUtf16(text.AsSpan(0, start), out Rune before, out _);
        _ = Rune.DecodeFromUtf16(text.AsSpan(end), out Rune after, out _);
        return (start == 0 || !IsWordCharacter(before)) && (end == text.Length || !IsWordCharacter(after));
    }

    private static bool SplitsScalar(string text, int offset)
    {
        return offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);
    }

    private static bool IsWordCharacter(Rune value)
    {
        return Rune.IsLetterOrDigit(value) || Rune.GetUnicodeCategory(value) is
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.EnclosingMark or UnicodeCategory.ConnectorPunctuation;
    }

    private bool CanNavigate => IsOpen && buffer is not null && matches.Count != 0;
    private bool CanReplaceAll => CanNavigate && IsReplaceVisible && CanEdit;
    private bool CanReplaceCurrent => CanReplaceAll && CurrentMatch is not null;
    private SearchMatch? CurrentMatch => buffer is null ? null : matches
        .Where(match => match.Start == buffer.SelectionStart && match.Length == buffer.SelectionLength)
        .Select(match => (SearchMatch?)match).FirstOrDefault();

    private void NotifyCommands()
    {
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        ReplaceCurrentCommand.NotifyCanExecuteChanged();
        ReplaceAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ShowFind() { IsReplaceVisible = false; IsOpen = true; }

    [RelayCommand]
    private void ShowReplace() { IsReplaceVisible = true; IsOpen = true; }

    [RelayCommand]
    private void Close() { IsOpen = false; }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void Next()
    {
        if (buffer is null || !CanNavigate)
        {
            return;
        }

        int start = buffer.SelectionStart;
        bool selected = CurrentMatch is not null;
        SearchMatch match = matches.FirstOrDefault(match => selected ? match.Start > start : match.Start >= start, matches[0]);
        buffer.Select(match.Start, match.Length);
    }

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void Previous()
    {
        if (buffer is null || !CanNavigate)
        {
            return;
        }

        SearchMatch match = matches.LastOrDefault(match => match.Start < buffer.SelectionStart, matches[^1]);
        buffer.Select(match.Start, match.Length);
    }

    [RelayCommand(CanExecute = nameof(CanReplaceCurrent))]
    private void ReplaceCurrent()
    {
        if (!CanReplaceCurrent || buffer is null || CurrentMatch is not { } match)
        {
            return;
        }

        buffer.Replace([match], Replacement);
        ReplacementCount = 1;
    }

    [RelayCommand(CanExecute = nameof(CanReplaceAll))]
    private void ReplaceAll()
    {
        if (!CanReplaceAll || buffer is null)
        {
            return;
        }

        List<SearchMatch> replacements = [];
        int end = 0;
        foreach (SearchMatch match in matches)
        {
            if (match.Start >= end)
            {
                replacements.Add(match);
                end = match.Start + match.Length;
            }
        }

        buffer.Replace(replacements, Replacement);
        ReplacementCount = replacements.Count;
    }
}
