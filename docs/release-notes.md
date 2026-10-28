# Maintain the changelog and release notes

[CHANGELOG.md](../CHANGELOG.md) is the canonical history of user-visible changes.
It uses [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) categories where they apply.
[Semantic Versioning](https://semver.org/spec/v2.0.0.html) defines the version format and published-version rules.
Tangopad's stable packaging path currently accepts only `major.minor.patch` versions.
Prerelease feeds and prerelease package publication remain outside this workflow.

`Directory.Build.props` is the authoritative version source.
The 1.0.0 draft is not a published release.
The 1.0.0 entry records the planned first release. It is not public until publication.
Do not infer a release date from a commit date, tag creation time, or closed issue.

## Document a user-visible change

Maintain Unreleased on `dev` through ordinary feature and fix pull requests.
Use only the categories that contain actual changes:

- Added introduces a capability.
- Changed changes existing behavior.
- Deprecated identifies behavior scheduled for removal.
- Removed identifies removed behavior.
- Fixed describes a corrected defect.
- Security describes a public security correction after coordinated disclosure.

For each user-visible change:

1. Add or revise a concise Unreleased entry.
2. Describe the resulting user behavior.
3. Link the issue or PR that establishes the change.
4. Combine related implementation commits into one entry where that improves clarity.
5. Mark the PR with `Changelog: updated`.

For maintenance with no material user effect, use `Changelog: none.` followed by a short reason in the PR description.
A dependency bump, test refactor, or CI adjustment does not require an entry unless it materially affects users.
Conventional Commit titles and commit logs supply evidence. They do not determine the final wording.
The contributor drafts the entry. The PR reviewer checks it against the behavior and linked evidence.
The release maintainer curates the final grouping and removes duplicates.
Do not add speculative features or unmerged work to release history.

## Prepare versioned history

Use a normal preparation PR to `dev` before the reviewed `dev`-to-`main` release PR.
Keep the version, changelog, and notes in that reviewed source.

1. Select the version under the repository's Semantic Versioning policy.
2. Set `Version` in `Directory.Build.props`.
3. Review Unreleased against merged PRs and the selected release candidate.
4. Move the accepted entries into `## VERSION - YYYY-MM-DD` below Unreleased.
5. Use the intended publication day's actual calendar date.
6. Leave Unreleased above the versioned entry for subsequent changes.
7. Add the comparison links described below.
8. Prepare the release notes from the accepted entries.

Do not add empty category headings or future dates.
A versioned entry in a release-preparation PR records the planned release contents.
It does not establish that the release is public.
If publication moves to a different day, correct the date through review and rebuild from the updated source.
Keep versioned entries in descending version and date order.

Before 1.0, document incompatible behavior in each minor release, as required by CONTRIBUTING.md.
For 1.0 and later, apply Semantic Versioning to the documented public behavior.
For a breaking change, identify affected users, previous and resulting behavior, required backups, and migration steps.
Link detailed migration instructions where necessary.
If migration is unnecessary, state that fact rather than inventing a procedure.

For security corrections, coordinate the advisory and release timing privately.
Do not put an embargoed vulnerability or an exploit into Unreleased or a public PR.
After disclosure, describe the user impact, fixed versions, advisory link, and required action.

### Add tag and comparison links

Stable Git tags use `vVERSION`. Do not invent comparison ranges before there is a prior version.
The first prepared version links to its corresponding GitHub release tag.
Later versions link the immediately preceding version tag to the new version tag.
Unreleased links the latest version tag to `dev`.

For example, after actual 0.1.0 and 0.2.0 releases exist, the 0.2.0 entry compares `v0.1.0...v0.2.0`.
The Unreleased comparison then uses `v0.2.0...dev`.
These are format examples, not Tangopad release-history claims.
The validator checks that each range uses adjacent versions in the changelog.
The release maintainer must also confirm that historical remote tags exist and identify the expected source.
A new version's tag and download URLs can remain unavailable while its GitHub Release is a draft.

## Prepare the user release notes

Use [the notes template](../eng/release/notes-template.md).
The [current draft](../eng/release/notes.md) is an example grounded in merged editor work.
Keep only one packaging notes input at `eng/release/notes.md`.
Do not add a competing history generator or copy the complete commit log into that file.

1. Copy the template's structure into `eng/release/notes.md` when preparing a new version.
2. Replace VERSION in the heading with the authoritative version.
3. Keep `Status: Draft` while curating the text.
4. Write a short summary and select the changes most relevant to users.
5. Describe breaking changes, migrations, compatibility constraints, and known limitations.
6. Identify verified OS and CPU combinations, package formats, and update limits.
7. Link the version-tagged changelog and documentation under `blob/vVERSION/`.
8. Keep the single `release-assets` comment in Downloads.
9. Review every statement against the selected source, packages, and platform evidence.
10. Set `Status: Ready for release review` only after that review.

Do not type predicted package names or checksum values into the notes.
The publisher replaces the Downloads comment with links and SHA-256 values from the verified files it will upload.
It rejects duplicate upload names.
It removes the internal Status line from the rendered GitHub body.
GitHub's draft state identifies the unpublished release.
The package's embedded notes retain the reviewed input text.
No tool in this workflow publishes the draft as stable.

Keep Windows, Linux, and macOS differences explicit.
Distinguish the update-feed identity from optional Windows or Apple platform certificates and notarization.
Do not claim trust prompts or installer behavior without native evidence.
Describe package-manager limits and installed or portable data preservation accurately.
Use the [installation guide](user/install.md) and [distribution requirements](release-distribution.md) as supporting references.
Final notes remain deferred until the documentation and release acceptance work is complete.

## Validate and preview without publishing

Run from the repository root:

```sh
python3 eng/test-release-notes.py
python3 eng/release-notes.py --preview artifacts/release-notes-preview.md
python3 eng/check-release-validation.py
python3 eng/test-check-docs.py
python3 eng/check-docs.py
dotnet msbuild eng/verify.proj
```

The preview command writes a local Markdown file. It creates no tag, package, or GitHub Release.
With no verified artifacts, the preview keeps Draft and states that production downloads are unavailable.
It does not fabricate asset links.
The fixture tests exercise prepared history and actual temporary files without calling GitHub or publishing anything.
The signed-package regression tests reject changed notes and tampered installers.
These tests prove the local workflow rules. They do not establish native package acceptance.

Run `python3 eng/release-notes.py --publication` to check publication readiness.
It intentionally fails on the current Draft input.
Production packaging and publication call the same validator.
It checks the authoritative version, status, required sections, category contents, version/date order,
comparison ranges, and version-tagged documentation links.
Default validation permits Unreleased drafts and runs in hosted Linux verification and candidate packaging CI.
Production CI requires ready notes before using signing credentials to build packages.
All checks use the Python standard library. Existing signed-package tests also require OpenSSL.

## Review and publish a release

Follow [release distribution](release-distribution.md#publish-a-reviewed-release) for native tools, protected signing credentials, package validation, and the draft workflow.
Use this checklist in the release PR and draft review:

- [ ] Confirm the changelog entries describe merged changes in the release candidate.
- [ ] Confirm version, date, comparison ranges, tag targets, and version-tagged documentation.
- [ ] Review migrations, security-advisory timing, known issues, and platform limits.
- [ ] Complete product documentation acceptance under issue #48 and release review under issue #52.
- [ ] Record successful hosted verification and required native package evidence for the same source commit.
- [ ] Merge the reviewed release PR from this repository's `dev` branch to `main`.
- [ ] Build all production formats from the reviewed, clean `main` source.
- [ ] Confirm every signed package inventory records the SHA-256 of the accepted `eng/release/notes.md`.
- [ ] Validate production output with `eng/publish-release.py --validate-only` and the full target commit SHA.
- [ ] Inspect `artifacts/release-notes.md`, including actual asset links and hashes.
- [ ] Create the GitHub draft through the publication command or workflow.
- [ ] Review attached installers, archives, update metadata, signing mode, native evidence, and rendered notes.
- [ ] Confirm the version tag identifies the reviewed `main` source, or will be created there on publication.
- [ ] Publish the GitHub draft only after the required human review.
- [ ] Check public asset links and the update feed after publication.

The publisher requires the local checkout to match the target commit.
It verifies artifact inventories and requires all four runtime sets and their native update evidence.
It rejects a package whose signed notes hash differs from the current accepted notes.
`--validate-only` writes the rendered notes but creates no GitHub draft or tag.
The normal command creates a draft. Publication remains a separate human action.

Never change assets under an already published version.
For a binary correction, prepare a new version and record the user-visible fix.
Correct release prose without changing the meaning of the accepted version or rewriting its history.
After publication, synchronize the accepted release record into `dev` before adding the next Unreleased changes.
