# Offline Textile preview

The optional Textile preview renders a bounded Redmine Textile baseline with native
text and table controls. It uses no browser, HTML renderer, or Redmine connection.
Textile files use the Textile parser. Markdown files keep their existing Markdown parser.

The preview starts hidden. A `.textile` document offers Show Preview and Hide Preview.
Plain text, `.wiki`, and unknown extensions have no preview.
A successful change between document formats hides and clears the preview.
The source editor retains its text, selection, caret, undo history, and save behavior.

## Supported baseline

The parser follows a subset of the
[Redmine Textile reference](https://www.redmine.org/projects/redmine/wiki/RedmineTextFormattingTextile)
and [detailed syntax reference](https://www.redmine.org/help/en/wiki_syntax_detailed_textile.html).
It does not claim full RedCloth compatibility or the same layout as server HTML.
Block markers start at the first character of a line.

| Source form | Preview |
| --- | --- |
| `h1. Heading` through `h6. Heading` | Heading text with the corresponding heading size |
| `p. Paragraph` or ordinary text | Paragraph text |
| `bq. Quote` | Indented quote with a text marker |
| `* item` and `# item` | Bulleted and numbered items |
| Repeated or mixed list markers, such as `**` and `*#` | Nested item indentation, up to the nesting limit |
| Pipe-delimited rows, such as `\|left\|right\|` | Native table cells in source order |
| A table cell that starts with `_.` | Strong header-cell text, with an optional space after the dot |
| `bc. code` or `pre. code` | Code through the next blank line |
| `<pre>code</pre>` and `<code>code</code>` | Literal code, without HTML execution |
| `@code@` | Inline code |
| A line of at least four hyphens | Horizontal rule |
| `*bold*`, `**bold**`, `_italic_`, `__italic__`, `+underline+`, and `-strike-` | Semantic text styles |
| Nested supported inline styles | Combined styles, up to the nesting limit |
| `==literal==` and `<notextile>literal</notextile>` | Literal content without inner style interpretation |
| A backslash before a markup delimiter | The delimiter as literal text |
| `"label":target` | Inactive link label |
| `!target(label)!` | Inactive image label |
| `!target!` | The target name as inactive text |

Adjacent ordinary lines form one paragraph with retained line breaks.
Blank lines separate paragraphs. Table rows retain empty cells and cell order.
A backslash can escape a pipe inside a cell. Table spans, alignment, and style
attributes have no native layout effect.

Unmatched delimiters remain visible. Inline code and literal escapes suppress inner
formatting. Tagged code removes at most one structural opening and closing newline.
It retains other blank lines. The parser normalizes line endings in its preview copy only.
Unicode and malformed UTF-16 remain text. Parsing does not normalize the editor source.

## Inactive content

Preview records contain text and semantic style flags. They contain no resource
destination, action, HTML node, or Avalonia control.
The renderer creates native TextBlock, Run, Grid, and Border controls from those records.
Link labels can be underlined, but no resource link is clickable.
Image labels remain text. The preview does not load an image or read its file.

Raw HTML, CSS attributes, JavaScript, paths, and network addresses remain inactive text.
No document attribute selects a native brush, font, executable handler, or resource.
The renderer obtains its fonts, dimensions, and colors from fixed presentation resources.
Both themes use the existing Textile heading and code colors.
The accessible pane name is Textile preview.

## Redmine-specific forms

[Issue #86](https://github.com/daniel-kindl/tangopad/issues/86) adds local text for
Redmine references. The syntax follows the Redmine references linked above.
The preview does not check whether a page, issue, attachment, or plugin exists.

| Source form | Classification | Preview |
| --- | --- | --- |
| `[[Page]]` | Supported text | Page title |
| `[[Page|Caption]]` | Supported text | Literal caption, without interpreting inner markup |
| `[[project:Page#Section]]` | Supported text | Page title without the project qualifier or anchor |
| `[[project:Page|Caption]]` | Supported text | Literal caption |
| `[[project:]]` or `[[#section]]` | Supported text | The project or anchor reference itself, because no page title is available |
| `#123`, `##123`, `#123-6`, `#123#note-6`, and `#note-6` | Supported text | Exact reference text, without an invented issue title or state |
| `attachment:file.zip` or `attachment:"file name.zip"` | Supported text | Inactive filename |
| `!image!` or `!image(label)!` | Supported text | Inactive name or label, without reading an image |
| `{{toc}}` | Local-only | Inactive list of parsed local headings |
| `{{include(...)}}` | Unsupported execution | `[macro: include]` |
| `{{collapse...}}` | Unsupported execution | `[macro: collapse]`, with its whole balanced body replaced |
| `{{child_pages}}` | Unsupported execution | `[macro: child_pages]` |
| `{{thumbnail(...)}}` | Unsupported execution | `[macro: thumbnail]` |
| `{{custom_plugin(...)}}` | Unsupported execution | `[macro: custom_plugin]`, using the actual macro name |
| Other server references, malformed forms, and macro names outside ASCII letters, digits, and underscore | Unsupported interpretation | Literal source |

The contents list follows document order and indents entries by heading level.
It includes headings after the marker. Code, table headers, and hidden macro bodies
cannot contribute headings. An empty contents list shows `[toc: no local headings]`.
The preview does not generate anchors, links, remote headings, or navigation.

Balanced nested macro arguments stay inside their outer placeholder.
A nested wiki form in a caption remains literal caption text.
An exclamation mark or backslash before a wiki link or macro suppresses interpretation
and shows the reference without the escape prefix. The same escapes suppress
attachment interpretation. Issue references can use the same escape prefix.
Existing inline code, `==literal==`, and `notextile` spans also suppress these forms.
Unmatched delimiters remain literal. A backslash can escape a pipe in a caption.
Complete wiki links, macros, code spans, and quoted attachments protect their inner
pipes from table-cell splitting. Table layout attributes remain unsupported.

## Scheduling and limits

`SourcePreviewAdapter` owns the shared preview lifecycle. It waits for a 200 ms quiet
period before parsing. Each window runs at most one parser task at a time.
New edits replace pending work. Revision, document identity, and format checks reject
stale results. Hide and format changes invalidate pending results.
Close cancels queued work and discards an active parser's result.
Synchronous parsing already in progress continues within its budgets.

| Budget | Limit |
| --- | --- |
| Source length | 32,768 UTF-16 code units |
| List and inline nesting | 32 |
| Blocks, including table containers, and table rows | 256 |
| Text runs across the document | 2,048 |
| Rendered text, including generated contents lists and placeholders | 32,768 UTF-16 code units |
| Table cells across the document | 1,024 |
| Inline scan work | 2,097,152 counted scanner steps |

Oversized source returns an empty limited preview before parsing.
Other exhausted budgets produce bounded output with a visible limit notice.
The scan budget limits counted inline operations. It is not a hard wall-clock timeout.
Code, raw text, and table structure also remain bounded by source length.

A current parser or renderer failure clears partial output and shows the existing
preview-unavailable message. A later edit, visibility change, or document transition
can retry. Stale failures have no visible output. Window-close cancellation stays silent.

## Ownership and verification

`Editor.Formats.Textile.TextilePreviewParser` owns the grammar and parser budgets.
`Editor.Preview` owns the shared inert data records.
App selects the parser and renders its output. Core and Application gain no dependencies.
[ADR 0010](adr/0010-safe-textile-preview.md) records the parser assessment and module decision.

Parser fixtures compare literal text, styles, list structure, and table cells.
Headless window tests cover lifecycle behavior, failures, both themes, and editor state.
The Linux native script captures the Textile source and preview, edits and saves the
source, checks exact bytes and undo, and changes the destination to plain text.
A loopback listener and a file-access monitor detect preview resource access.
Screenshots require visual inspection. The script does not infer syntax colors or
Textile conformance from pixels.

### Recorded native result

On 2026-10-23, `/usr/bin/python3 eng/check-native-editor.py` passed all 68 workflow
groups on Fedora Linux 44, x64. The session used Xvfb, a private D-Bus, and the GTK portal.
Evidence is in `artifacts/native-editor-6s2p3ctb`.
The inspected Textile screenshot shows the heading, styled text, table cells,
inactive resource labels, raw script text, and literal Redmine references from the first preview slice.
The resource monitors detected no network request or local asset access.
Live edits, exact saved bytes, undo, and the transition to plain text passed.
Native Windows, macOS, Wayland, and screen-reader interaction remain untested on this host.

On 2026-10-23, the Issue #86 native fixture passed all 69 groups in
`artifacts/native-editor-xns3d4jw`. Its inspected screenshot shows wiki captions,
attachment names, named macro placeholders, and a local contents list.
The collapsed macro's internal heading is absent from the preview and contents list.
Socket and file-access monitors detected no resource access. Exact save bytes and
undo checks passed with the reference source unchanged.
