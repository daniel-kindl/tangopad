# Structured-text source highlighting

The source-highlighting phases of [Issue #50](https://github.com/daniel-kindl/tangopad/issues/50)
add visual syntax highlighting for `.json`, `.xml`, `.xaml`, `.yaml`, `.yml`, `.csv`, and `.tsv` files.
`.jsonc` and `.axaml` remain plain text.

## Selection and ownership

`Editor.Core.DocumentFormats` owns extension identification. `DocumentSession.Format`
provides the format to `SourceHighlightingAdapter` in `src/Editor.App/Views/`.
The adapter uses namespace `Editor.App.Views`. Core types remain in `Editor.Core`.
The dependency graph and persistence contracts do not change.

The adapter loads XML from the pinned AvaloniaEdit package.
`Views/Json.xshd` adapts [AvaloniaEdit's JSON definition](https://github.com/AvaloniaUI/AvaloniaEdit/blob/7277e36366d754219d7ede16b6613786b32caf4c/src/AvaloniaEdit/Highlighting/Resources/Json.xshd). It bounds object value spans
and replaces open-ended comma spans with punctuation rules.
These changes keep later fields and array values visible to the highlighter.
The upstream MIT license is in `docs/licenses/AvaloniaEdit.txt` and accompanies the application.
It creates a separate definition for each editor and theme change.
It does not change the global highlighting definitions.
`Xaml` selects the same XML definition as `Xml`.
The built-in XML extension list does not control format identification.
For example, `.csproj` and `.config` remain plain text.

## Baseline and limits

JSON highlighting recognizes field names, strings with backslash escapes, numbers,
booleans, null values, and punctuation. Object and array spans can cross lines.
The upstream definition is permissive. It also colors single-quoted strings and
hexadecimal numbers. Highlighting does not prove JSON validity.
JSON comments have no comment role. `.jsonc` support needs a separate policy.

XML highlighting recognizes tags, attributes with either quote style, entities,
comments, CDATA, declarations, and document type declarations.
Comments, CDATA, tags, and attribute values can cross lines.
The upstream rules recover some incomplete syntax, including a missing attribute quote
before a new tag. They do not validate XML or fully parse document type internal subsets.

XAML receives general XML syntax colors. The editor does not resolve namespaces,
load assemblies, evaluate bindings, or validate platform schemas.
The highlighter does not parse the document with an XML parser or resolve external entities.
No preview, diagnostics, formatting, grid, folding, or completion behavior is added.

## YAML baseline and limits

`Views/Yaml.xshd` supplies local lexical rules. The pinned AvaloniaEdit package
has no YAML definition. The rules add no parser dependency.
YAML highlighting recognizes plain and quoted mapping keys, quoted strings,
backslash escapes in double quotes, and doubled single quotes.
Quoted strings can cross lines. Unclosed quotes continue to the end of the buffer.
The rules also recognize common YAML 1.2 boolean, null, and number spellings,
flow punctuation, sequence markers, anchors, aliases, tags, directives, and document markers.
A comment starts with `#` at the start of a line or after whitespace.
Hashes inside quoted strings and URL fragments do not start comments.

Block scalar indicators such as `|-` and `>+` receive punctuation colors.
The rules do not track block indentation or assign a string role to block scalar content.
That content receives the same lexical rules as other unquoted text.
Complex keys and schema-dependent scalar types have no semantic interpretation.
Highlighting does not validate YAML, construct objects, expand aliases, or process tags.

## CSV and TSV baseline and limits

`Views/Csv.xshd` uses commas as separators. `Views/Tsv.xshd` uses tabs.
The extension selects the separator. The editor does not detect a delimiter from the contents.
Semicolons and the other format's separator remain ordinary field text.
A double quote at the start of a field opens a quoted field.
Quoted fields can contain separators, doubled quotes, and newlines.
An unclosed quoted field retains its color through the end of the buffer.
Quotes inside an unquoted field do not open a quoted field.
Malformed input remains editable without validation or repair.

Unquoted fields keep the normal text color. The highlighter does not infer a header,
convert numbers or dates, trim whitespace, or evaluate formulas.
Leading zeros, empty fields, trailing separators, and literal formula text remain source text.
There is no grid, delimiter conversion, or automatic formatting.
The existing storage policy supports strict UTF-8. A save removes an initial UTF-8 BOM.
Legacy encodings remain unsupported. The highlighting phase does not change these storage limits.

## Themes and source preservation

Both theme dictionaries define `Brush.<definition>.<token>` roles for every structured-text definition.
Colors use the existing source palette and Fluent foreground resources.
Tests require a contrast ratio of at least 4.5:1 in both default themes.
The editor retains its normal URL foreground for these formats.

Highlighting does not assign document text, caret, selection, undo history, or dirty state.
Open, New, and Save As use the existing document workflow.
Pending, failed, and cancelled saves retain the previous highlighting.
The [persistence policy](adr/0006-local-text-persistence.md) still controls byte storage.
Format identification and highlighting add no text transformations.

## Verification

Core tests check case variants, filename collisions, path separators, and fallback names.
Application tests check Save As transitions. Infrastructure tests check exact UTF-8 bytes.
Headless window tests check source tokens, malformed input, theme colors, contrast,
selection, caret, undo/redo, New, saves, and format transitions.
Large-buffer tests highlight 20,000 lines per format and report time and allocation counts.
These measurements describe the test host. They are not performance guarantees.

`python3 eng/check-native-editor.py` opens JSON, XML, XAML, YAML, CSV, and TSV fixtures on Linux.
It captures screenshots and checks exact bytes after edits, undo, Save, and Save As to plain text.
Screenshots need visual inspection. The script does not infer syntax colors from pixels.

### Recorded JSON and XML/XAML phase result

Date: 2026-10-23. Fedora Linux 44, x64, .NET SDK 10.0.402 and runtime 10.0.13.

`dotnet msbuild eng/verify.proj` passed with zero build warnings and errors.
Tests passed 605 cases and skipped one Windows-only filesystem case.
All eight TRX reports passed validation.
The extension tests failed before implementation. The JSON token test exposed
upstream comma spans that hid later values. The adapted definition passed all token tests.

The Linux native script passed all 75 groups with Xvfb, a private D-Bus session,
and the GTK portal. Evidence is in `artifacts/native-editor-yaxym7px`.
The inspected JSON, XML, and XAML screenshots show source colors.
The inspected JSON Save As screenshot shows plain text without syntax colors.
Exact bytes and undo checks passed for all three formats.
Windows and macOS native interaction remain untested on this host.
Hosted CI still needs to run for this change.

A separate Release run measured the large-buffer tests with captured output:

| Format | UTF-16 code units | Full scan | Thread allocations |
| --- | ---: | ---: | ---: |
| JSON | 1,140,000 | 648 ms | 175,062,728 bytes |
| XML | 1,000,000 | 223 ms | 74,251,680 bytes |

Each buffer contains 20,000 lines. Allocations include the highlighter and per-line test assertions.
They measure cumulative allocated bytes, not retained memory or peak process memory.
The editor normally highlights visible lines. These full scans exercise the complete buffer.

### Recorded YAML and CSV/TSV phase result

Date: 2026-10-23. Fedora Linux 44, x64, .NET SDK 10.0.402 and runtime 10.0.13.

`dotnet msbuild eng/verify.proj` passed with zero build warnings and errors.
Tests passed 648 cases and skipped one Windows-only filesystem case.
All eight TRX reports passed validation.
The new extension tests failed before implementation.
A negative YAML test exposed a quote inside an unquoted word that incorrectly opened a string span.
Quote boundary rules corrected this behavior. Multiline edits and undo also passed.

The Linux native script passed all 83 groups with Xvfb, a private D-Bus session,
and the GTK portal. Evidence is in `artifacts/native-editor-u1n77hx5`.
The inspected YAML, YML, CSV, and TSV screenshots show source colors.
Exact bytes and undo checks passed for every new extension, including Save As to plain text.
Windows and macOS native interaction remain untested on this host.
Hosted CI still needs to run for this phase.

A separate Release run measured the new large-buffer cases with captured output:

| Format | UTF-16 code units | Full scan | Thread allocations |
| --- | ---: | ---: | ---: |
| YAML | 840,000 | 211 ms | 99,705,712 bytes |
| CSV | 600,000 | 83 ms | 45,124,808 bytes |
| TSV | 580,000 | 175 ms | 45,124,792 bytes |

Each buffer contains 20,000 lines. The allocation and host limits stated above also apply here.
