# Changelog

This file is the canonical history of user-visible changes.
Entries follow [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) categories.
Published versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
The first public release is pending. The entries below describe merged, unreleased work.
See [the release workflow](docs/release-notes.md) for curation and publication rules.

## Unreleased

### Added

- Edit and save one local UTF-8 document, with undo, redo, and unsaved-change confirmation.
  See [PR #20](https://github.com/daniel-kindl/tangopad/pull/20).
- Find literal text, whole words, or regular expressions, and replace matches in one undo operation.
  See [PR #31](https://github.com/daniel-kindl/tangopad/pull/31),
  [PR #78](https://github.com/daniel-kindl/tangopad/pull/78), and [PR #83](https://github.com/daniel-kindl/tangopad/pull/83).
- Navigate to a line, change font zoom, wrap long lines, and toggle line numbers.
  See [PR #40](https://github.com/daniel-kindl/tangopad/pull/40),
  [PR #42](https://github.com/daniel-kindl/tangopad/pull/42),
  [PR #44](https://github.com/daniel-kindl/tangopad/pull/44), and [PR #70](https://github.com/daniel-kindl/tangopad/pull/70).
- Highlight Markdown and Textile source, with optional bounded, read-only previews that keep document resources inactive.
  See [PR #51](https://github.com/daniel-kindl/tangopad/pull/51),
  [PR #54](https://github.com/daniel-kindl/tangopad/pull/54), and [PR #88](https://github.com/daniel-kindl/tangopad/pull/88).
- Highlight JSON, XML, XAML, YAML, CSV, and TSV source without changing file contents.
  See [PR #90](https://github.com/daniel-kindl/tangopad/pull/90) and [PR #92](https://github.com/daniel-kindl/tangopad/pull/92).
- Open a local file by drag and drop, reopen recent files, and reload from disk with unsaved-change protection.
  See [PR #62](https://github.com/daniel-kindl/tangopad/pull/62),
  [PR #64](https://github.com/daniel-kindl/tangopad/pull/64), and [PR #68](https://github.com/daniel-kindl/tangopad/pull/68).
- Show external file changes and require a decision before overwriting a changed destination.
  See [PR #74](https://github.com/daniel-kindl/tangopad/pull/74) and [PR #76](https://github.com/daniel-kindl/tangopad/pull/76).
- Retain editor preferences and recent paths in per-user storage or opt-in portable storage.
  See [PR #94](https://github.com/daniel-kindl/tangopad/pull/94).
- Provide authenticated update candidates for self-managed desktop packages.
  Production publication and platform acceptance remain subject to the release checks.
  See [PR #95](https://github.com/daniel-kindl/tangopad/pull/95) and [PR #96](https://github.com/daniel-kindl/tangopad/pull/96).
- Provide desktop menus, light and dark appearance choices, and document status information.
  See [PR #101](https://github.com/daniel-kindl/tangopad/pull/101).

### Fixed

- Reject release package sets that omit a required portable distribution or installer.
  See [issue #115](https://github.com/daniel-kindl/tangopad/issues/115).
- Include dependency licenses, third-party notices, and selected .NET runtime notices in desktop packages.
  See [issue #113](https://github.com/daniel-kindl/tangopad/issues/113).
- Recover from a process exit during update startup coordination.
  See [PR #100](https://github.com/daniel-kindl/tangopad/pull/100).
- Distinguish checked toolbar toggles with an outline in both themes.
  Native screen-reader and OS high-contrast acceptance remain incomplete.
  See [PR #102](https://github.com/daniel-kindl/tangopad/pull/102).
