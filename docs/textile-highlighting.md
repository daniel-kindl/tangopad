# Textile source highlighting

[Issue #84](https://github.com/daniel-kindl/tangopad/issues/84) adds visual source
highlighting for Redmine Textile. The definition uses AvaloniaEdit's native XSHD loader.
Highlighting works independently of the [Textile preview](textile-preview.md).

## Dialect

The supported name is Redmine Textile. The source forms follow these references:

- [Redmine Textile formatting](https://www.redmine.org/projects/redmine/wiki/RedmineTextFormattingTextile)
- [Detailed Textile syntax](https://www.redmine.org/help/en/wiki_syntax_detailed_textile.html)

The highlighter does not target one Redmine server version.
It does not claim the same result as Redmine HTML output.
[Issue #85](https://github.com/daniel-kindl/tangopad/issues/85) adds the offline preview baseline.
[Issue #86](https://github.com/daniel-kindl/tangopad/issues/86) owns Redmine-specific preview forms.

## Selection and ownership

`DocumentSession.Format` selects highlighting. The ViewModel exposes that value
and sends a notification after Open, New, or successful Save As.
The adapter does not inspect filenames. See [document formats](document-formats.md)
for extension rules and failure behavior.
`.textile` selects this definition.
`.wiki`, untitled text, plain text, and unknown extensions do not select it.
Pending, failed, and cancelled saves retain the current highlighting.

`src/Editor.App/Views/SourceHighlightingAdapter.cs` and `Textile.xshd` stay
beside their consuming window. The adapter uses namespace `Editor.App.Views`.
The XSHD file is an embedded resource. The adapter assigns `TextEditor.SyntaxHighlighting`.
It also assigns the visual URL foreground from the Link color.
It removes its subscriptions when the window closes.
The same adapter still loads `Markdown.xshd` for Markdown.
Core, Application, persistence, and the dependency graph retain their current ownership.
The open and save pickers stay unfiltered.

## Baseline and limits

The definition recognizes these source forms:

- `h1.` through `h6.` with a space after the dot.
  The marker may include one alignment form: `(`, `)`, `>`, `=`, or `<>`.
- The `p.` marker, with the same alignment forms.
  The highlighter colors the marker. The paragraph text stays ordinary.
- `bq.` or `bq..` when a space and text follow on that line.
- List markers made from `*` and `#` at the start of a line, including nested and mixed markers.
  The marker must have following whitespace. The highlighter colors only the marker.
- A table row that starts with `|`. The whole row uses one color.
- A line that contains only `----`, with optional trailing whitespace.
- `*bold*`, `_italic_`, `+underline+`, and `-strike-` on one line.
- Inline code with one pair of `@` characters.
- One-line `<code>` and `<code ...>` regions.
- `<pre>` or `<pre ...>` through `</pre>`.
- A `bc.` or `pre.` line, including `bc..` and `pre..`.
  Following lines stay code through the line before the next blank line.
  The blank line stays ordinary. Text after that blank line stays ordinary.
- `<notextile>` through `</notextile>`, and one-line `== ==`.
  Inner markers stay ordinary. An unclosed region continues to the end of its span.
- `"label":target` when the target has no whitespace.
- `[[WikiPage]]` and `[[Page|Caption]]`, including a colon inside the brackets.
- `!image!`, including one `<`, `>`, or `=` immediately after the opening `!`.
- `#123`, `##123`, `#123-6`, and `#123#note-6`.
- A single-line `{{macro}}`, including arguments such as `{{include(Wiki)}}`.

Block markers start at the first character of the line.
Fenced code and literal spans suppress inner highlighting.
Ordinary text retains the editor foreground.
The definition does not execute HTML, macros, links, or images.
It does not read files or open a network connection.

These forms stay ordinary text:

- Class and id markers such as `h1(#id). Title`.
- Lines that follow `bq.`.
- `bc..` or `pre..` text after a blank line.
- Nested inline markers.
- `^superscript^`, `~subscript~`, and `??citation??`.
- Revision names such as `r123`.
- Attachment names such as `attachment:file.png`.
- A macro that spans more than one line, except a line that itself matches `{{...}}`.
- HTML other than `pre`, `code`, and `notextile`. `<script>` stays ordinary text.
- Inline markers inside a table row, a heading, a quote, or a horizontal rule.

## Themes and state

Both theme dictionaries define `Brush.Textile.<token>` roles for Heading, Strong,
Emphasis, Underline, Strike, Code, Link, Quote, List, Table, Rule, Paragraph, and Macro.
Heading, Code, and Link use the same theme colors as the Markdown source roles.
Strong, Emphasis, Underline, and Strike use the primary text brush.
Quote, List, Table, Rule, Paragraph, and Macro use the muted text brush.
Headings and strong text use bold weight. Emphasis uses italic style.
Underline and links use an underline. Strike uses strikethrough.
The adapter reloads the definition when the actual theme changes.
It resolves brushes for the editor's actual theme, including window theme overrides.
AvaloniaEdit URL elements use the Link brush in Textile documents.
Plain text restores the previous URL brush.

Highlighting does not assign document text, caret, selection, or undo state.
Save As retains editing history and persists the exact captured text.
An open preview closes when a successful save changes the format.
A Textile document offers its own optional preview. Its syntax baseline differs
from the source highlighter. See [Textile preview](textile-preview.md).
The existing session rules still control dirty state and edits during a pending save.

## Verification

`TextileHighlightingTests` drives the compiled window and native XSHD highlighter
on the headless dispatcher. It checks recognized names, ordinary text, the baseline
above, literal escapes, hostile HTML, theme contrast of at least 4.5:1, retained
editing state, and closure of an open Markdown preview after Save As to `.textile`.
`MarkdownHighlightingTests` checks transitions among Markdown, Textile, `.wiki`, and plain text.
`DocumentFormatsTests`, `DocumentSessionTests`, and `FileTextDocumentStoreTests` check
the filename rule and exact UTF-8 bytes.

The Linux acceptance script opens a `.textile` fixture, captures its source highlighting,
saves it as plain text, and checks exact bytes and undo behavior.
The screenshot requires visual inspection. The script does not infer syntax colors from pixels.

### Recorded local result

Date: 2026-10-22. Fedora Linux 44, x64, .NET 10.0.402.

`dotnet msbuild eng/verify.proj` passed with zero build warnings and errors.
Locked restore, formatting, analyzers, and Debug and Release builds passed.
Tests passed 482 cases and skipped one Windows-only filesystem case.
All seven TRX reports passed validation. Native startup passed.

`python3 eng/check-native-editor.py` passed all 65 workflow groups with Xvfb,
a private D-Bus session, and the GTK portal. Evidence is in
`artifacts/native-editor-0sy8c1iy`. The inspected Textile screenshot shows source
colors for the heading, emphasis, code, and link. The plain-text screenshot follows
Save As. Exact bytes and undo passed. Windows and macOS native interaction remain
untested on this host. Hosted CI runs headless tests and native startup on all three platforms.
