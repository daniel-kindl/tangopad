# Tangopad 1.0.0

Status: Draft

## Summary

Tangopad's first public release is in preparation.
This draft describes merged editor behavior. It does not announce a published release or validated production installers.
The [changelog](https://github.com/daniel-kindl/tangopad/blob/dev/CHANGELOG.md) contains the curated change history and source PRs.

## Highlights

- Edit local UTF-8 text with explicit save, undo, and unsaved-change confirmation.
- Search literal text, whole words, or regular expressions, and replace matches in one undo operation.
- Highlight Markdown, Textile, and structured text. Preview supported Markdown and Textile without loading external resources.
- Use desktop menus, font zoom, Word Wrap, line numbers, and light or dark appearance.
- Retain preferences and recent paths in per-user or opt-in portable storage.

## Breaking changes and migration

No prior public release exists. There is no public-version migration to perform.
Development builds with legacy JSON state can migrate to binary state.
Back up state before changing builds. See [application state](https://github.com/daniel-kindl/tangopad/blob/dev/docs/app-state.md).

## Installation and updates

Candidate formats target Windows x64 Setup and ZIP, Linux x64 AppImage, and macOS x64 and arm64 bundles and current-user installers.
These are planned release formats, not a minimum-OS support guarantee.
Follow [installation guidance](https://github.com/daniel-kindl/tangopad/blob/dev/docs/user/install.md) and review the package's platform limits.
Supported self-managed packages offer manual update checks and an explicit update-and-restart action.
Package-managed and protected installations do not support application-controlled replacement.

## Downloads

<!-- release-assets -->

## Verification and trust

Production inventories and update feeds use the repository's ECDSA P-256 update identity.
The publication command verifies inventories, signatures, SHA-256 hashes, versions, source commits, and required native update evidence before creating a GitHub draft.
The rendered download table contains hashes of the actual uploaded files.
Hashes alone do not authenticate an untrusted download source.

Windows and Apple platform certificates and macOS notarization are optional in the current pipeline.
Update-feed signatures do not establish SmartScreen or Gatekeeper trust.
Review each package's actual signing mode and native launch results before publication.
See [release distribution](https://github.com/daniel-kindl/tangopad/blob/dev/docs/release-distribution.md).

## Known limitations

- One document per window. There are no tabs, exports, autosave, or crash recovery of unsaved text.
- Files must decode as UTF-8. Save does not add a BOM or preserve all filesystem metadata.
- Preview is bounded and read-only. Document links stay inactive and images remain descriptions.
- Linux uses X11 or XWayland.
- On Linux, screen readers cannot read the document text. The editor has no AT-SPI `Text` interface.
- Native screen-reader, OS high-contrast, production installation, and final documentation checks remain pending.
- Uninstall is not tested in 1.0.0. The release ships with this limit.

Review [known limitations](https://github.com/daniel-kindl/tangopad/blob/dev/docs/user/troubleshooting.md#known-limitations)
and the [user guide](https://github.com/daniel-kindl/tangopad/blob/dev/docs/user/README.md) before testing candidates.
