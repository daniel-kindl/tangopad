# Markdown source highlighting

[Issue #46](https://github.com/daniel-kindl/tangopad/issues/46) adds visual source
highlighting through AvaloniaEdit's native XSHD loader and highlighter.
No Markdown parser or rendered preview is involved.

## Selection and ownership

`DocumentSession.Format` selects highlighting. The ViewModel exposes that value
and sends a notification after Open, New, or successful Save As.
The adapter does not inspect filenames. See [document formats](document-formats.md)
for extension rules and failure behavior.
Untitled, plain-text, and unknown-extension documents have no syntax definition.
Pending, failed, and cancelled saves retain the current highlighting.

`src/Editor.App/Views/MarkdownHighlightingAdapter.cs` and `Markdown.xshd` stay
beside their consuming window. The adapter uses namespace `Editor.App.Views`.
The XSHD file is an embedded resource. The adapter assigns `TextEditor.SyntaxHighlighting` and the visual URL foreground.
It removes its subscriptions when the window closes.
Core, Application, persistence, and the dependency graph retain their current ownership.

## Baseline and limits

The definition recognizes these source forms:

- ATX headings with one to six hash characters followed by whitespace or line end.
- Single-line emphasis and strong text with asterisks or underscores.
  Underscores inside words remain ordinary text.
- Inline code with single backticks.
- Fenced code with exactly three backticks or tildes at the start of a line.
  Opening fences can have an information string. Closing fences allow trailing whitespace.
  An unclosed fence colors the remaining document as code.
- Inline links and images with a label and a parenthesized destination.
- Blockquote lines with a leading `>`.
- Unordered list markers and ordered markers ending in `.` or `)`.

Block markers allow up to three leading spaces. List markers require following whitespace.
Fenced code suppresses inline highlighting. Ordinary text retains the editor foreground.
The definition does not claim CommonMark compliance. It does not handle nested
emphasis, balanced link destinations, reference links, Setext headings, indented code,
longer fences, multiple-backtick inline delimiters, or embedded language highlighting.
Nested formatting is outside this baseline.

## Themes and state

Both theme dictionaries define `Brush.Markdown.<token>` roles for Heading,
Strong, Emphasis, Code, Link, Quote, and List.
Heading, Code, and Link use theme-specific source colors in the shared dictionaries.
Strong and Emphasis use the Fluent primary brush. Quote and List use its muted brush.
Headings and strong text use bold weight. Emphasis uses italic style.
Links use an underline. The adapter reloads the definition when the actual theme changes.
It resolves brushes for the editor's actual theme, including window theme overrides.
AvaloniaEdit URL elements use the Link brush in Markdown documents.
Plain text restores the previous URL brush and retains its existing link behavior.

Highlighting does not assign document text, caret, selection, or undo state.
Save As retains editing history and persists the exact captured text.
The existing session rules still control dirty state and edits during a pending save.

## Verification

`MarkdownHighlightingTests` drives the compiled window and native XSHD highlighter
on the headless dispatcher. It checks recognized formats, ordinary text, representative
constructs, format transitions, pending saves, failures, cancellation, theme changes,
contrast of at least 4.5:1 with the default themes, and retained editing state.


The Linux acceptance script opens a Markdown fixture, captures its source highlighting,
saves it as plain text, and checks exact UTF-8 bytes and undo behavior.
The screenshot requires visual inspection. The script does not infer syntax colors from pixels.

### Recorded local result

Date: 2026-10-18. Fedora Linux 44, x64, .NET 10.0.13.

`dotnet msbuild eng/verify.proj` passed with zero build warnings and errors.
All six TRX reports passed validation. Tests passed 178 cases and skipped one
Windows-only filesystem case. The eight new headless cases passed.
The disabled-highlighting check failed before the definition was restored.
The contrast check failed before the theme colors were corrected.

`python3 eng/check-native-editor.py` passed all 26 groups with Xvfb,
a private D-Bus session, and the GTK portal. Evidence is in
`artifacts/native-editor-akfjcqr3`. The inspected screenshots show Markdown
styling and its removal after Save As to `.txt`. Exact bytes and undo passed.
Windows and macOS native interaction remain untested on this host.
Hosted CI runs headless tests and native startup on all three platforms.
