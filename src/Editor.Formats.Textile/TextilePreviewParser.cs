using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Editor.Preview;

namespace Editor.Formats.Textile;

/// <summary>Converts a bounded Textile subset to native, inactive preview records.</summary>
public static class TextilePreviewParser
{
    /// <summary>The maximum source length in UTF-16 code units.</summary>
    public const int MaximumSourceLength = 32_768;
    private const int MaximumDepth = 32;
    private const int MaximumBlocks = 256;
    private const int MaximumRuns = 2048;
    private const int MaximumCells = 1024;

    /// <summary>Parses Textile without HTML rendering, resource access, or document changes.</summary>
    /// <param name="source">The exact document source.</param>
    /// <returns>Bounded inactive text and a flag for incomplete output.</returns>
    public static PreviewDocument Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Length > MaximumSourceLength ? new PreviewDocument([], true) : new Parser(source).Parse();
    }

    private sealed class Parser(string source)
    {
        private readonly string[] lines = source.ReplaceLineEndings("\n").Split('\n');
        private readonly List<PreviewBlock> blocks = [];
        private readonly Dictionary<string, int> listNumbers = new(StringComparer.Ordinal);
        private int position;
        private int blockCount;
        private int runCount;
        private int cellCount;
        private int remainingWork = MaximumSourceLength * 64;
        private bool limited;

        internal PreviewDocument Parse()
        {
            while (position < lines.Length && !limited)
            {
                string line = lines[position];
                if (string.IsNullOrWhiteSpace(line))
                {
                    position++;
                    listNumbers.Clear();
                    continue;
                }
                if (!ReserveBlock()) { break; }
                if (IsTable(line))
                {
                    ParseTable();
                }
                else
                {
                    ParseTextBlock(line);
                }
            }
            return new PreviewDocument(blocks.AsReadOnly(), limited);
        }

        private void ParseTextBlock(string line)
        {
            int listDepth = 0;
            while (listDepth < line.Length && line[listDepth] is '*' or '#') { listDepth++; }
            if (listDepth > 0 && listDepth < line.Length && char.IsWhiteSpace(line[listDepth]))
            {
                if (listDepth > MaximumDepth) { limited = true; return; }
                string marker = line[..listDepth];
                foreach (string key in listNumbers.Keys.Where(key => key.Length > marker.Length || (key.Length == marker.Length && key != marker) || (key.Length < marker.Length && !marker.StartsWith(key, StringComparison.Ordinal))).ToArray())
                {
                    _ = listNumbers.Remove(key);
                }
                string prefix = "• ";
                if (marker[^1] == '#')
                {
                    int number = listNumbers.GetValueOrDefault(marker) + 1;
                    listNumbers[marker] = number;
                    prefix = number.ToString(CultureInfo.InvariantCulture) + ". ";
                }
                blocks.Add(new PreviewTextBlock(Inline(line[(listDepth + 1)..]), prefix, listDepth));
                position++;
                return;
            }
            listNumbers.Clear();
            if (line.Length >= 4 && line.All(character => character == '-'))
            {
                blocks.Add(new PreviewTextBlock([], ThematicBreak: true));
                position++;
                return;
            }
            if (line.StartsWith("<pre>", StringComparison.Ordinal) || line.StartsWith("<code>", StringComparison.Ordinal))
            {
                string tag = line.StartsWith("<pre>", StringComparison.Ordinal) ? "pre" : "code";
                ParseTaggedCode(tag);
                return;
            }
            string prefixText = "";
            int heading = 0;
            bool code = false;
            if (line.Length > 3 && line[0] == 'h' && line[1] is >= '1' and <= '6' && line[2] == '.' && line[3] == ' ')
            {
                heading = line[1] - '0';
                line = line[4..];
            }
            else if (line.StartsWith("p. ", StringComparison.Ordinal)) { line = line[3..]; }
            else if (line.StartsWith("bq. ", StringComparison.Ordinal)) { line = line[4..]; prefixText = "› "; }
            else if (line.StartsWith("bc. ", StringComparison.Ordinal)) { line = line[4..]; code = true; }
            else if (line.StartsWith("pre. ", StringComparison.Ordinal)) { line = line[5..]; code = true; }
            position++;
            StringBuilder text = new(line);
            while (position < lines.Length && !string.IsNullOrWhiteSpace(lines[position]) && (code || !StartsBlock(lines[position])))
            {
                _ = text.Append('\n').Append(lines[position++]);
            }
            string content = text.ToString();
            bool literal = IsAttributedBlock(content) || (content.StartsWith('<') && !content.StartsWith("<notextile>", StringComparison.Ordinal));
            blocks.Add(new PreviewTextBlock(code || literal ? Literal(content, code) : Inline(content), prefixText,
                prefixText.Length == 0 ? 0 : 1, heading, code));
        }

        private void ParseTaggedCode(string tag)
        {
            string open = "<" + tag + ">";
            string close = "</" + tag + ">";
            StringBuilder content = new();
            string line = lines[position++][open.Length..];
            while (true)
            {
                int end = line.IndexOf(close, StringComparison.Ordinal);
                if (end >= 0)
                {
                    _ = content.Append(line.AsSpan(0, end));
                    if (end + close.Length < line.Length)
                    {
                        position--;
                        lines[position] = line[(end + close.Length)..];
                    }
                    break;
                }
                _ = content.Append(line);
                if (position >= lines.Length) { break; }
                _ = content.Append('\n');
                line = lines[position++];
            }
            string text = content.ToString();
            if (text.StartsWith('\n')) { text = text[1..]; }
            if (text.EndsWith('\n')) { text = text[..^1]; }
            blocks.Add(new PreviewTextBlock(Literal(text, true), Code: true));
        }

        private void ParseTable()
        {
            List<PreviewTableRow> rows = [];
            while (position < lines.Length && IsTable(lines[position]) && !limited)
            {
                if (!ReserveBlock()) { break; }
                string line = lines[position++];
                List<PreviewTableCell> cells = [];
                int start = 1;
                bool escaped = false;
                for (int index = 1; index < line.Length && !limited; index++)
                {
                    char character = line[index];
                    if (character == '|' && !escaped)
                    {
                        if (cellCount >= MaximumCells) { limited = true; break; }
                        cellCount++;
                        string value = line[start..index].Trim();
                        bool heading = value.StartsWith("_.", StringComparison.Ordinal);
                        cells.Add(new PreviewTableCell(Inline(heading ? value[2..].TrimStart() : value), heading));
                        start = index + 1;
                    }
                    escaped = character == '\\' && !escaped;
                }
                rows.Add(new PreviewTableRow(cells.AsReadOnly()));
            }
            if (rows.Count > 0) { blocks.Add(new PreviewTableBlock(rows.AsReadOnly())); }
        }

        private ReadOnlyCollection<PreviewRun> Inline(string text)
        {
            List<PreviewRun> runs = [];
            AddInline(text, new PreviewRun(""), runs, 0);
            return runs.AsReadOnly();
        }

        private void AddInline(string text, PreviewRun style, List<PreviewRun> runs, int depth)
        {
            if (depth > MaximumDepth) { limited = true; return; }
            StringBuilder plain = new();
            int index = 0;
            while (index < text.Length && !limited)
            {
                if (!SpendWork()) { break; }
                char character = text[index];
                if (character == '\\' && index + 1 < text.Length && "\\*_+-@=!|\"".Contains(text[index + 1], StringComparison.Ordinal))
                {
                    _ = plain.Append(text[index + 1]);
                    index += 2;
                    continue;
                }
                string? literalEnd = text.AsSpan(index).StartsWith("[[", StringComparison.Ordinal) ? "]]"
                    : text.AsSpan(index).StartsWith("{{", StringComparison.Ordinal) ? "}}" : null;
                if (literalEnd is not null)
                {
                    int end = FindOpaqueEnd(text, index, text.Substring(index, 2), literalEnd);
                    if (end < 0) { _ = plain.Append(text.AsSpan(index)); break; }
                    _ = plain.Append(text.AsSpan(index, end - index));
                    index = end;
                    continue;
                }
                if (text.AsSpan(index).StartsWith("<code>", StringComparison.Ordinal) || text.AsSpan(index).StartsWith("<notextile>", StringComparison.Ordinal))
                {
                    bool code = text.AsSpan(index).StartsWith("<code>", StringComparison.Ordinal);
                    string open = code ? "<code>" : "<notextile>";
                    string close = code ? "</code>" : "</notextile>";
                    int end = FindLiteral(text, close, index + open.Length);
                    if (end >= 0)
                    {
                        Flush();
                        AddRun(runs, style with { Text = text[(index + open.Length)..end], Code = code || style.Code });
                        index = end + close.Length;
                        continue;
                    }
                }
                if (character == '"' && TryLink(text, index, out int linkEnd, out string label))
                {
                    Flush();
                    AddRun(runs, style with { Text = label, Link = true });
                    index = linkEnd;
                    continue;
                }
                if (character == '!' && TryImage(text, index, out int imageEnd, out string image))
                {
                    Flush();
                    AddRun(runs, style with { Text = image });
                    index = imageEnd;
                    continue;
                }
                if (character is '*' or '_' or '+' or '-' or '@' or '=' && CanOpen(text, index))
                {
                    int length = character is '*' or '_' && index + 1 < text.Length && text[index + 1] == character ? 2 : 1;
                    if (character == '=') { length = index + 1 < text.Length && text[index + 1] == '=' ? 2 : 0; }
                    if (length > 0)
                    {
                        string delimiter = text.Substring(index, length);
                        int end = FindClose(text, delimiter, index + length);
                        if (end >= 0)
                        {
                            Flush();
                            string inner = text[(index + length)..end];
                            if (character is '@' or '=')
                            {
                                AddRun(runs, style with { Text = inner, Code = style.Code || character == '@' });
                            }
                            else
                            {
                                PreviewRun nested = character switch
                                {
                                    '*' => style with { Strong = true },
                                    '_' => style with { Emphasis = true },
                                    '+' => style with { Underline = true },
                                    _ => style with { Strike = true },
                                };
                                AddInline(inner, nested, runs, depth + 1);
                            }
                            index = end + length;
                            continue;
                        }
                    }
                }
                _ = plain.Append(character);
                index++;
            }
            Flush();

            void Flush()
            {
                if (plain.Length == 0) { return; }
                AddRun(runs, style with { Text = plain.ToString() });
                _ = plain.Clear();
            }
        }

        private int FindClose(string text, string delimiter, int start)
        {
            for (int index = start; index <= text.Length - delimiter.Length; index++)
            {
                if (!SpendWork()) { return -1; }
                if (index > start && text.AsSpan(index).StartsWith(delimiter, StringComparison.Ordinal)
                    && text[index - 1] != '\\' && !char.IsWhiteSpace(text[index - 1])
                    && (index + delimiter.Length == text.Length || !IsWord(text[index + delimiter.Length])))
                {
                    return index;
                }
            }
            return -1;
        }

        private static bool CanOpen(string text, int index)
        {
            return (index == 0 || !IsWord(text[index - 1])) && index + 1 < text.Length && !char.IsWhiteSpace(text[index + 1]);
        }

        private static bool IsWord(char character)
        {
            return char.IsLetterOrDigit(character) || character == '_';
        }

        private bool TryLink(string text, int start, out int end, out string label)
        {
            int quote = FindLiteral(text, "\":", start + 1);
            end = start;
            label = "";
            if (quote < 0 || quote == start + 1 || quote + 2 >= text.Length || char.IsWhiteSpace(text[quote + 2])) { return false; }
            end = quote + 2;
            while (end < text.Length && !char.IsWhiteSpace(text[end]))
            {
                if (!SpendWork()) { return false; }
                end++;
            }
            label = text[(start + 1)..quote];
            return true;
        }

        private bool TryImage(string text, int start, out int end, out string label)
        {
            int close = FindLiteral(text, "!", start + 1);
            end = start;
            label = "";
            if (close <= start + 1) { return false; }
            string value = text[(start + 1)..close];
            if (value.Contains('\n', StringComparison.Ordinal) || value.Contains('{', StringComparison.Ordinal)) { return false; }
            int nameEnd = value.IndexOf('(', StringComparison.Ordinal);
            if (value.AsSpan(0, nameEnd < 0 ? value.Length : nameEnd).ContainsAny(' ', '\t')) { return false; }
            int caption = value.LastIndexOf('(');
            label = caption >= 0 && value.EndsWith(')') ? value[(caption + 1)..^1] : value;
            end = close + 1;
            return true;
        }

        private ReadOnlyCollection<PreviewRun> Literal(string text, bool code)
        {
            List<PreviewRun> runs = [];
            AddRun(runs, new PreviewRun(text, Code: code));
            return runs.AsReadOnly();
        }

        private void AddRun(List<PreviewRun> runs, PreviewRun run)
        {
            if (runCount >= MaximumRuns) { limited = true; return; }
            runCount++;
            runs.Add(run);
        }

        private bool ReserveBlock()
        {
            if (blockCount >= MaximumBlocks) { limited = true; return false; }
            blockCount++;
            return true;
        }

        private int FindOpaqueEnd(string text, int start, string open, string close)
        {
            int depth = 1;
            for (int index = start + open.Length; index < text.Length; index++)
            {
                if (!SpendWork()) { return -1; }
                if (text.AsSpan(index).StartsWith(open, StringComparison.Ordinal))
                {
                    if (++depth > MaximumDepth) { limited = true; return -1; }
                    index += open.Length - 1;
                }
                else if (text.AsSpan(index).StartsWith(close, StringComparison.Ordinal))
                {
                    if (--depth == 0) { return index + close.Length; }
                    index += close.Length - 1;
                }
            }
            return -1;
        }

        private int FindLiteral(string text, string marker, int start)
        {
            for (int index = start; index <= text.Length - marker.Length; index++)
            {
                if (!SpendWork()) { return -1; }
                if (text.AsSpan(index).StartsWith(marker, StringComparison.Ordinal)) { return index; }
            }
            return -1;
        }

        private static bool IsAttributedBlock(string text)
        {
            int prefix = text.StartsWith("pre", StringComparison.Ordinal) ? 3
                : text.StartsWith("bq", StringComparison.Ordinal) || text.StartsWith("bc", StringComparison.Ordinal) ? 2
                : text.Length > 1 && text[0] == 'h' && text[1] is >= '1' and <= '6' ? 2
                : text.StartsWith('p') ? 1 : 0;
            return prefix > 0 && prefix < text.Length && text[prefix] is '{' or '(' or '[' or '<' or '>' or '=';
        }

        private bool SpendWork()
        {
            if (--remainingWork >= 0) { return true; }
            limited = true;
            return false;
        }

        private static bool IsTable(string line)
        {
            if (line.Length < 2 || line[0] != '|' || line[^1] != '|') { return false; }
            int backslashes = 0;
            for (int index = line.Length - 2; index >= 0 && line[index] == '\\'; index--) { backslashes++; }
            return backslashes % 2 == 0;
        }

        private static bool StartsBlock(string line)
        {
            if (IsTable(line) || line.StartsWith("<pre>", StringComparison.Ordinal) || line.StartsWith("<code>", StringComparison.Ordinal)) { return true; }
            if (line.StartsWith("p. ", StringComparison.Ordinal) || line.StartsWith("bq. ", StringComparison.Ordinal)
                || line.StartsWith("bc. ", StringComparison.Ordinal) || line.StartsWith("pre. ", StringComparison.Ordinal)) { return true; }
            if (line.Length > 3 && line[0] == 'h' && line[1] is >= '1' and <= '6' && line[2] == '.' && line[3] == ' ') { return true; }
            int index = 0;
            while (index < line.Length && line[index] is '*' or '#') { index++; }
            return (index > 0 && index < line.Length && char.IsWhiteSpace(line[index])) || (line.Length >= 4 && line.All(character => character == '-'));
        }
    }
}
