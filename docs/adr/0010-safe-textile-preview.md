# ADR 0010: Render a bounded, inert Textile preview

Status: Proposed

Date: 2026-10-23

## Context

[Issue #85](https://github.com/daniel-kindl/tangopad/issues/85) adds an offline Textile
preview after filename recognition and highlighting. The preview must preserve source,
undo, dirty state, and persistence. Redmine reference resolution belongs to Issue #86.

## Parser assessment

[Textile.NET 1.3.0](https://www.nuget.org/packages/Textile.NET/1.3.0) targets .NET
Framework 3.5. Its NuGet page records the last update as 2011-04-28. It produces HTML,
which would require a second parser to produce native inert records.

The [NTextile fork](https://github.com/johnradke/NTextile) targets .NET Framework 4.5.2.
The repository reports its last push as 2024-05-24 at this assessment.
Its `Textile/LICENSE.txt` contains Apache-2.0, while the inspected
`Textile/Blocks/ImageBlockModifier.cs` header names CPL-1.0.
The image modifier emits HTML image and anchor elements through regular-expression
replacement. The inspected API does not provide the bounded, inert output this feature needs.
These observations do not establish that every formatter path is unsafe.

No suitable maintained .NET parser was identified in this assessment. Implement the
explicit baseline in `Editor.Formats.Textile` without copying third-party parser code.
Use the [Redmine formatting reference](https://www.redmine.org/projects/redmine/wiki/RedmineTextFormattingTextile)
and [detailed syntax reference](https://www.redmine.org/help/en/wiki_syntax_detailed_textile.html)
as behavior references. This parser does not claim full RedCloth compatibility.

## Boundary

`src/Editor.Formats.Textile/`, namespace `Editor.Formats.Textile`, owns Textile parsing.
`src/Editor.Preview/`, namespace `Editor.Preview`, owns format-independent inert records.
Markdown and Textile reference these records. Neither parser references the other.
App owns the single preview lifecycle and native renderer in `Views/`.
Core and Application gain no dependencies. No new package dependency is required.

Migrate the Markdown record types and every caller together. Delete their old definitions.
Keep Markdown parsing and its existing behavior in `Editor.Formats.Markdown`.
The shared record module contains display data only, without parser or UI implementation.

## Safety and limits

The parser returns text and semantic formatting flags. It cannot return an action,
a resource destination, HTML, or an Avalonia object. Native controls display raw HTML,
CSS attributes, JavaScript, and unsupported forms as literal text.
Link and image labels stay inactive. No source text selects a font, brush, resource,
file, URL, or executable handler.

Reuse the existing preview scheduling and failure handling from ADR 0008.
Keep the 200 ms debounce, one parser task per window, revision and document checks,
close cancellation, and recovery after a failed attempt.
Reject more than 32,768 UTF-16 code units. Bound nesting to 32, blocks to 256,
and text runs to 2,048. Limits produce the existing visible limit notice.

## Alternatives

A Textile-to-HTML converter followed by sanitization introduces an HTML boundary
without improving the native renderer. Independent copies of the preview records
would require App to normalize two equivalent contracts and keep them synchronized.
The shared data module makes both format parsers independent of that normalization.
