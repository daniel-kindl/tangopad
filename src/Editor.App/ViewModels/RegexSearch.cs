using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Editor.App.ViewModels;

internal enum SearchIssue
{
    None,
    InvalidPattern,
    TimedOut,
    EmptyMatch,
    LimitReached,
    InvalidReplacement
}

internal sealed record RegexSearchResult(List<SearchMatch> Matches, List<SearchReplacement> Replacements, SearchIssue Issue);

internal static class RegexSearch
{
    private const int MaximumInputLength = 1_000_000;
    private const int MaximumPatternLength = 4096;
    private const int MaximumMatches = 20_000;
    private const int MaximumOutputLength = 8_000_000;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromMilliseconds(250);

    internal static RegexSearchResult Scan(string text, string query, string? replacement, bool matchCase, bool wholeWord, CancellationToken cancellationToken)
    {
        if (text.Length > MaximumInputLength || query.Length > MaximumPatternLength || replacement is { Length: > MaximumPatternLength })
        {
            return new([], [], SearchIssue.LimitReached);
        }

        Stopwatch elapsed = Stopwatch.StartNew();
        try
        {
            RegexOptions options = RegexOptions.CultureInvariant;
            if (!matchCase)
            {
                options |= RegexOptions.IgnoreCase;
            }

            Regex regex = new(query, options, MatchTimeout);
            List<Match> captures = [];
            List<SearchMatch> matches = [];
            for (Match match = regex.Match(text); match.Success; match = match.NextMatch())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (elapsed.Elapsed > ScanTimeout)
                {
                    return new([], [], SearchIssue.TimedOut);
                }

                if (match.Length == 0)
                {
                    return new([], [], SearchIssue.EmptyMatch);
                }

                if (!wholeWord || SearchViewModel.IsWholeWordMatch(text, match.Index, match.Length))
                {
                    if (matches.Count == MaximumMatches)
                    {
                        return new([], [], SearchIssue.LimitReached);
                    }

                    matches.Add(new SearchMatch(match.Index, match.Length));
                    if (replacement is not null)
                    {
                        captures.Add(match);
                    }
                }
            }

            if (elapsed.Elapsed > ScanTimeout)
            {
                return new([], [], SearchIssue.TimedOut);
            }

            if (replacement is null)
            {
                return new(matches, [], SearchIssue.None);
            }

            List<ReplacementPart>? parts = ParseReplacement(regex, replacement);
            if (parts is null)
            {
                return new(matches, [], SearchIssue.InvalidReplacement);
            }

            List<SearchReplacement> replacements = [];
            long outputLength = text.Length;
            long expandedLength = 0;
            foreach (Match match in captures)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (elapsed.Elapsed > ScanTimeout)
                {
                    return new([], [], SearchIssue.TimedOut);
                }

                StringBuilder expanded = new();
                foreach (ReplacementPart part in parts)
                {
                    string value = part.IsGroup ? match.Groups[part.Text].Value : part.Text;
                    expandedLength += value.Length;
                    if (expandedLength > MaximumOutputLength)
                    {
                        return new(matches, [], SearchIssue.LimitReached);
                    }

                    _ = expanded.Append(value);
                }

                outputLength += expanded.Length - match.Length;
                replacements.Add(new SearchReplacement(new SearchMatch(match.Index, match.Length), expanded.ToString()));
            }

            return outputLength > MaximumOutputLength
                ? new(matches, [], SearchIssue.LimitReached)
                : elapsed.Elapsed > ScanTimeout
                ? new([], [], SearchIssue.TimedOut)
                : new(matches, replacements, SearchIssue.None);
        }
        catch (RegexMatchTimeoutException)
        {
            return new([], [], SearchIssue.TimedOut);
        }
        catch (ArgumentException)
        {
            return new([], [], SearchIssue.InvalidPattern);
        }
    }

    private static List<ReplacementPart>? ParseReplacement(Regex regex, string replacement)
    {
        HashSet<string> groups = new(regex.GetGroupNames(), StringComparer.Ordinal);
        foreach (int number in regex.GetGroupNumbers())
        {
            _ = groups.Add(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        List<ReplacementPart> parts = [];
        int offset = 0;
        while (offset < replacement.Length)
        {
            int dollar = replacement.IndexOf('$', offset);
            if (dollar < 0)
            {
                parts.Add(new(replacement[offset..], false));
                break;
            }

            if (dollar > offset)
            {
                parts.Add(new(replacement[offset..dollar], false));
            }

            offset = dollar + 1;
            if (offset == replacement.Length)
            {
                return null;
            }

            char token = replacement[offset++];
            if (token == '$')
            {
                parts.Add(new("$", false));
                continue;
            }

            string group;
            if (token == '&')
            {
                group = "0";
            }
            else if (token == '{')
            {
                int end = replacement.IndexOf('}', offset);
                if (end < 0)
                {
                    return null;
                }

                group = replacement[offset..end];
                offset = end + 1;
            }
            else if (char.IsAsciiDigit(token))
            {
                int start = offset - 1;
                while (offset < replacement.Length && char.IsAsciiDigit(replacement[offset]))
                {
                    offset++;
                }

                group = replacement[start..offset];
            }
            else
            {
                return null;
            }

            if (!groups.Contains(group))
            {
                return null;
            }

            parts.Add(new(group, true));
        }

        return parts;
    }

    private readonly record struct ReplacementPart(string Text, bool IsGroup);
}
