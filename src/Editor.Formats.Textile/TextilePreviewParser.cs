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
        private readonly HashSet<PreviewRun> contentsRuns = new(ReferenceEqualityComparer.Instance);
        private int position;
        private int blockCount;
        private int runCount;
        private int cellCount;
        private int renderedLength;
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
            ExpandContents();
            return new PreviewDocument(blocks.AsReadOnly(), limited);
        }

        private bool TryReadMultilineMacro(string line, out string content)
        {
            content = "";
            int start = line.IndexOf("{{", StringComparison.Ordinal);
            while (start >= 0)
            {
                if (!FindMacroEnd(start, out int endLine, out int endColumn)) { return false; }
                if (endLine == position)
                {
                    start = FindLiteral(line, "{{", endColumn);
                    continue;
                }
                StringBuilder text = new(line);
                for (int row = position + 1; row < endLine; row++)
                {
                    _ = text.Append('\n').Append(lines[row]);
                }
                _ = text.Append('\n').Append(lines[endLine].AsSpan(0, endColumn));
                content = text.ToString();
                string tail = lines[endLine][endColumn..];
                position = endLine;
                if (tail.Length == 0) { position++; }
                else { lines[position] = tail; }
                return true;
            }
            return false;
        }

        private bool FindMacroEnd(int start, out int endLine, out int endColumn)
        {
            endLine = position;
            endColumn = start;
            int depth = 1;
            for (int row = position; row < lines.Length; row++)
            {
                string line = lines[row];
                for (int column = row == position ? start + 2 : 0; column < line.Length; column++)
                {
                    if (!SpendWork()) { return false; }
                    if (line.AsSpan(column).StartsWith("{{", StringComparison.Ordinal))
                    {
                        if (++depth > MaximumDepth) { limited = true; return false; }
                        column++;
                    }
                    else if (line.AsSpan(column).StartsWith("}}", StringComparison.Ordinal))
                    {
                        if (--depth == 0)
                        {
                            endLine = row;
                            endColumn = column + 2;
                            return true;
                        }
                        column++;
                    }
                }
            }
            return false;
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
                bool multiline = TryReadMultilineMacro(line, out string listContent);
                blocks.Add(new PreviewTextBlock(Inline((multiline ? listContent : line)[(listDepth + 1)..]), prefix, listDepth));
                if (!multiline) { position++; }
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
            string originalLine = line;
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
            if (!code && TryReadMultilineMacro(originalLine, out string macroContent))
            {
                line = macroContent[(originalLine.Length - line.Length)..];
            }
            else { position++; }
            StringBuilder text = new(line);
            while (position < lines.Length && !limited && !string.IsNullOrWhiteSpace(lines[position]) && (code || !StartsBlock(lines[position])))
            {
                string next = !code && TryReadMultilineMacro(lines[position], out string paragraphContent)
                    ? paragraphContent : lines[position++];
                _ = text.Append('\n').Append(next);
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
                    string? close = line.AsSpan(index).StartsWith("[[", StringComparison.Ordinal) ? "]]"
                        : line.AsSpan(index).StartsWith("{{", StringComparison.Ordinal) ? "}}" : null;
                    int protectedEnd = close is null ? -1 : FindOpaqueEnd(line, index, line.Substring(index, 2), close);
                    if (!escaped && protectedEnd < 0 && character is '@' or '=')
                    {
                        string delimiter = character == '@' ? "@" : "==";
                        if (line.AsSpan(index).StartsWith(delimiter, StringComparison.Ordinal))
                        {
                            int end = FindClose(line, delimiter, index + delimiter.Length);
                            if (end >= 0) { protectedEnd = end + delimiter.Length; }
                        }
                    }
                    if (!escaped && protectedEnd < 0 && TryAttachment(line, index, out int attachmentEnd, out _))
                    {
                        protectedEnd = attachmentEnd;
                    }
                    if (protectedEnd >= 0)
                    {
                        index = protectedEnd - 1;
                        escaped = false;
                        continue;
                    }
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
                if (character is '!' or '\\' && index + 1 < text.Length)
                {
                    string? escapedClose = text.AsSpan(index + 1).StartsWith("[[", StringComparison.Ordinal) ? "]]"
                        : text.AsSpan(index + 1).StartsWith("{{", StringComparison.Ordinal) ? "}}" : null;
                    if (escapedClose is not null)
                    {
                        int end = FindOpaqueEnd(text, index + 1, text.Substring(index + 1, 2), escapedClose);
                        if (end >= 0)
                        {
                            _ = plain.Append(text.AsSpan(index + 1, end - index - 1));
                            index = end;
                            continue;
                        }
                    }
                    if (TryAttachment(text, index + 1, out int escapedEnd, out _))
                    {
                        _ = plain.Append(text.AsSpan(index + 1, escapedEnd - index - 1));
                        index = escapedEnd;
                        continue;
                    }
                    if (text[index + 1] == '#')
                    {
                        _ = plain.Append(text[index + 1]);
                        index += 2;
                        continue;
                    }
                }
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
                    Flush();
                    string content = text[(index + 2)..(end - 2)];
                    if (literalEnd == "]]")
                    {
                        bool valid = content.Length > 0 && content[0] != '|' && !content.EndsWith('|');
                        AddRun(runs, style with { Text = valid ? WikiLabel(content) : text[index..end], Link = valid });
                    }
                    else if (content == "toc")
                    {
                        PreviewRun contents = style with { Text = "" };
                        int previous = runCount;
                        AddRun(runs, contents);
                        if (runCount > previous) { _ = contentsRuns.Add(contents); }
                    }
                    else
                    {
                        int nameEnd = 0;
                        while (nameEnd < content.Length && (char.IsAsciiLetterOrDigit(content[nameEnd]) || content[nameEnd] == '_')) { nameEnd++; }
                        bool named = nameEnd > 0 && (nameEnd == content.Length || content[nameEnd] == '(' || char.IsWhiteSpace(content[nameEnd]));
                        string macroLabel = named ? "[macro: " + content[..nameEnd] + "]" : text[index..end];
                        AddRun(runs, style with { Text = macroLabel });
                    }
                    index = end;
                    continue;
                }
                if (TryAttachment(text, index, out int referenceEnd, out string name))
                {
                    Flush();
                    AddRun(runs, style with { Text = name, Link = true });
                    index = referenceEnd;
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
            if (runCount >= MaximumRuns || run.Text.Length > MaximumSourceLength - renderedLength) { limited = true; return; }
            runCount++;
            renderedLength += run.Text.Length;
            runs.Add(run);
        }

        private static string WikiLabel(string content)
        {
            int separator = -1;
            for (int index = 0; index < content.Length; index++)
            {
                if (content[index] == '\\') { index++; }
                else if (content[index] == '|') { separator = index; break; }
            }
            string label = separator >= 0 ? content[(separator + 1)..] : content;
            if (separator < 0)
            {
                int project = label.IndexOf(':', StringComparison.Ordinal);
                if (project > 0 && project + 1 < label.Length && label[..project].All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
                {
                    label = label[(project + 1)..];
                }
                int anchor = label.IndexOf('#', StringComparison.Ordinal);
                if (anchor > 0) { label = label[..anchor]; }
            }
            return UnescapeLabel(label);
        }

        private static string UnescapeLabel(string label)
        {
            StringBuilder result = new();
            for (int index = 0; index < label.Length; index++)
            {
                if (label[index] == '\\' && index + 1 < label.Length && "\\[]|\"".Contains(label[index + 1], StringComparison.Ordinal)) { index++; }
                _ = result.Append(label[index]);
            }
            return result.ToString();
        }

        private bool TryAttachment(string text, int start, out int end, out string name)
        {
            end = start;
            name = "";
            const string prefix = "attachment:";
            if (!text.AsSpan(start).StartsWith(prefix, StringComparison.Ordinal) || (start > 0 && IsWord(text[start - 1]))) { return false; }
            int value = start + prefix.Length;
            if (value >= text.Length || char.IsWhiteSpace(text[value])) { return false; }
            if (text[value] == '"')
            {
                int close = value + 1;
                while (close < text.Length && text[close] != '"')
                {
                    if (!SpendWork() || text[close] == '\n') { return false; }
                    if (text[close] == '\\' && close + 1 < text.Length) { close++; }
                    close++;
                }
                if (close == text.Length || close == value + 1) { return false; }
                name = UnescapeLabel(text[(value + 1)..close]);
                end = close + 1;
            }
            else
            {
                end = value;
                while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] != '|')
                {
                    if (!SpendWork()) { return false; }
                    end++;
                }
                name = text[value..end];
            }
            return name.Length > 0;
        }

        private void ExpandContents()
        {
            if (contentsRuns.Count == 0) { return; }
            (int Level, string Text)[] headings = [.. blocks.OfType<PreviewTextBlock>()
                .Where(block => block.HeadingLevel > 0)
                .Select(block => (block.HeadingLevel, string.Concat(block.Runs.Where(run => !contentsRuns.Contains(run)).Select(run => run.Text))))
                .Where(heading => heading.Item2.Length > 0)];
            for (int index = 0; index < blocks.Count; index++)
            {
                blocks[index] = blocks[index] switch
                {
                    PreviewTextBlock text => text with { Runs = ExpandRuns(text.Runs) },
                    PreviewTableBlock table => table with
                    {
                        Rows = table.Rows.Select(row => row with
                        {
                            Cells = row.Cells.Select(cell => cell with { Runs = ExpandRuns(cell.Runs) }).ToList().AsReadOnly(),
                        }).ToList().AsReadOnly(),
                    },
                    _ => blocks[index],
                };
            }

            ReadOnlyCollection<PreviewRun> ExpandRuns(IReadOnlyList<PreviewRun> runs)
            {
                return runs.Select(run => contentsRuns.Contains(run) ? run with { Text = BuildContents() } : run).ToList().AsReadOnly();
            }

            string BuildContents()
            {
                StringBuilder contents = new();
                if (headings.Length == 0)
                {
                    const string empty = "[toc: no local headings]";
                    if (empty.Length > MaximumSourceLength - renderedLength) { limited = true; return ""; }
                    renderedLength += empty.Length;
                    return empty;
                }
                foreach ((int level, string title) in headings)
                {
                    if (!SpendWork()) { break; }
                    int indentation = (level - 1) * 2;
                    int length = indentation + 2 + title.Length + (contents.Length == 0 ? 0 : 1);
                    if (length > MaximumSourceLength - renderedLength) { limited = true; break; }
                    if (contents.Length > 0) { _ = contents.Append('\n'); }
                    _ = contents.Append(' ', indentation).Append("• ").Append(title);
                    renderedLength += length;
                }
                return contents.ToString();
            }
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
