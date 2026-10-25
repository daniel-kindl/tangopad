# ADR 0008: Render an inert Markdown preview with native controls

Status: Accepted

Date: 2026-10-18

## Context

[Issue #53](https://github.com/daniel-kindl/tangopad/issues/53) requires an optional,
read-only preview. Source editing and persistence must retain their existing behavior.
Preview must not load resources or execute document content.

## Decision

`Editor.Formats.Markdown` owns parser adaptation. Its original preview records now
live in `Editor.Preview` so the Textile parser can use the same inert data contract.
See [ADR 0010](0010-safe-textile-preview.md) for that migration.
Markdown references Markdig and Editor.Preview. Core and Application receive no new dependencies.
App references the parser and renders Markdown output with Avalonia TextBlock and Run.
No Markdig or Avalonia types cross into Core or Application.

Select [Markdig 1.4.1](https://www.nuget.org/packages/Markdig/1.4.1).
The NuGet version index lists it as the latest stable version at this review.
The maintained 1.x line includes a configurable nesting limit.
The older 0.45.0 line is compatible with .NET but predates that major line.
The selected package provides net10.0 managed assets without an Avalonia
version dependency. The repository builds and headless tests verify integration
with .NET 10 and Avalonia 12.1.4. There are no transitive Markdig dependencies.
Its [BSD-2-Clause license](https://github.com/xoofx/markdig/blob/1.4.1/license.txt)
permits redistribution with attribution and the disclaimer.
The unmodified notice is included in `docs/licenses/Markdig.txt` and copied beside
the application for build and publish output. Locked restore audits vulnerabilities
at low severity and above. Review the actual audit output for each delivery.

Disable HTML parsing. Raw HTML becomes literal text. Use the default CommonMark
pipeline without extensions, script engines, web views, image controls, or browser
launchers. The adapter discards link and image destinations. Link labels are
underlined but inactive. Image descriptions remain text. Autolinks display literal
addresses. No URI scheme causes navigation, including HTTP, HTTPS, file, data, or
JavaScript. External navigation remains deferred.

The preview supports headings, paragraphs, emphasis, strong text, links as inactive
labels, ordered and unordered lists, blockquotes, fenced and indented code, inline
code, line breaks, and thematic breaks. Flat blocks retain nesting through indentation
and text markers. This renderer does not provide full CommonMark visual fidelity.
Tables, task-list controls, diagrams, image display, and HTML styling remain deferred.

`SourcePreviewAdapter` owns the view lifecycle and schedules updates after a
200 ms quiet period. A window runs at most one parser task at a time. New requests
replace pending work. Revision and document identity checks reject stale results.
Hide and format transitions invalidate pending results. Close cancels delays,
removes event subscriptions, and discards active results. Markdig parsing itself
is synchronous and cannot be cancelled after it starts.

Each update attempt handles recoverable parser and renderer exceptions at the preview
boundary. A current failure clears partial output and shows a localized, accessible
message. Stale failures produce no output. The serial loop continues with newer
pending work. A subsequent edit, visibility change, or document transition can retry.
Parser cancellation without window-close cancellation is a recoverable failure.
Window-close cancellation remains silent. Process-fatal exceptions, including
memory exhaustion, remain outside recovery.

Reject input longer than 32,768 UTF-16 code units before parsing. Limit nesting to
32, display blocks to 256, and text runs to 2,048. Show a resource-limit message
when a limit prevents a complete preview. These limits bound input and display work.
They are not a hard wall-clock timeout. Parsing occurs on a background thread so
editing can continue. A parser upgrade requires hostile-input and performance checks.

The window offers Show Preview when `DocumentSession.Format` is Markdown or Textile.
Markdown source uses only the Markdown parser. Preview starts hidden.
A successful format transition hides it and clears the selection of this view option.
Returning to Markdown requires Show Preview.
New and Open follow session notifications, including document identity changes.
Failed and cancelled saves retain the previous format and preview state.
Preview never writes to the editor buffer or session. Theme styles consume existing
semantic brushes. Preview dimensions and messages use presentation resources.

## Alternatives

An HTML web view would require a second active-content and resource-loading policy.
Native text controls need no HTML sanitizer, browser runtime, account, or telemetry.
A third-party Avalonia Markdown control could add image or navigation behavior that
this first version does not need. A small native renderer keeps those capabilities
absent from the output representation.
