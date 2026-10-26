# Documentation QA record

[Issue #108](https://github.com/daniel-kindl/tangopad/issues/108) owns documentation QA and final release sign-off.
This record covers the development documentation. It does not establish release-candidate acceptance.
Implementation lives in `eng/check-docs.py`, `eng/review-docs.py`, and their fixture tests.
No application namespace or dependency changes.

## Repeat the structural review

Run the offline checks from the repository root:

```sh
python3 eng/test-check-docs.py
python3 eng/test-review-docs.py
python3 eng/check-docs.py
```

Hosted Linux CI runs these commands without network calls from the fixtures.
The source checker covers inline Markdown links and product media metadata.
The rendered-page fixtures cover reference links, nested heading text, heading collisions,
image descriptions, heading levels, and navigation between preview pages.
They also check HTTP outcome classification, including login redirects and rate limits.

For a live rendering and HTTP review, authenticate `gh` and run:

```sh
python3 eng/review-docs.py --external
```

The command sends the selected Markdown text to GitHub's Markdown API in GFM mode.
The default selection contains the README, changelog, contribution guide, developer guide,
release guides, product review record, this QA record, and the six user-guide pages.
Supply explicit repository-relative Markdown paths to review another selection.
The script uses at most four concurrent requests.

Output lives in `artifacts/docs-review/`:

- `result.json` records the UTC time, source commit, dirty-checkout flag, source hashes, raw GFM body hashes, local preview hashes, link count, errors, and HTTP outcomes.
- Each selected Markdown file has a corresponding HTML preview at the same relative path.

Open `artifacts/docs-review/README.html` to inspect the local preview.
Links to other selected pages open their HTML previews. Images use the repository's local files.
The HTML body comes from GitHub. The small local stylesheet differs from GitHub's page chrome.
The preview tool adds local heading IDs and rewrites local navigation targets.
This tool checks document structure. It does not establish visual layout or accessibility acceptance.
Run it again after changing reviewed content. Use the source hashes to identify the reviewed text.

Without `--external`, the command renders and checks local destinations only.
With `--external`, it requests each distinct HTTP URL and records the status and final destination.
A login redirect remains `review` even when the login page returns HTTP 200.
HTTP 404 and 410 mean `unavailable`. Timeouts, rate limits, and other failures require review.
The command exits with code 1 when a check fails or a URL still needs review.
`INCOMPLETE` preserves those gaps. It is not a production release decision.
HTTP success does not prove that a destination says the right thing or contains the requested external anchor.
Keep this network review outside the required CI gate.

## Recorded development review

Date: 2026-10-26. Source base: `12ec809ef5c33c73902f1195a81155bc819028ae`.
The checkout includes the uncommitted documentation work for issues #48, #49, and #108.
The source hashes in the generated report identify the actual draft contents.

The initial live run covered 13 pages before this QA record was added.
The run inspected 139 rendered links. It found no invalid local paths or local heading targets.
Images had descriptions, selected pages had one level-one heading, and headings did not skip levels.
There were 44 distinct external URLs. Forty-two returned successful responses.
No URL returned 404 or 410 in that run.
Two routes required authentication:

| Route | Observation | Remaining check |
| --- | --- | --- |
| Issue creation | Redirected to GitHub login with HTTP 200. | A signed-in reviewer must confirm the issue chooser and task form. |
| Private vulnerability report | Redirected to GitHub login with HTTP 200. | A signed-in reviewer must confirm the private reporting form. |

A read-only repository API query returned `enabled: true` for private vulnerability reporting.
That confirms the configured feature. It does not prove the signed-in form workflow.
Both routes remain marked for review in the HTTP report.
No issue, advisory, or message was submitted as part of this check.

### Source consistency checks

These checks compare documentation claims with the current implementation.
They are a bounded source review, not a complete security or privacy audit.

| Documentation claim | Inspected implementation | Review limit |
| --- | --- | --- |
| Update checks are explicit and installation is not automatic. | [Startup](../src/Editor.App/Program.cs) disables startup auto-apply. [Update commands](../src/Editor.App/ViewModels/UpdateViewModel.cs) call the updater from Check and Install actions. | Does not establish package-level update acceptance. |
| Update downloads contact constrained GitHub origins. | [Signed release source](../src/Editor.Infrastructure/Updates/SignedReleaseSource.cs) restricts HTTPS origins and verifies feed and package metadata. | Does not establish a general network-privacy guarantee. |
| Preferences and recent paths do not store document text. | [Binary state](../src/Editor.Infrastructure/BinaryAppState.cs) contains presentation values and recent paths. | Does not audit every possible OS or third-party log. |
| Word Wrap defaults off, line numbers default on, and font size defaults to 15 within 8 through 48. | [Editor presentation](../src/Editor.Infrastructure/EditorPresentation.cs) defines those defaults and bounds. | Native preference restoration remains a separate workflow check. |
| Open uses strict UTF-8, and Save adds no BOM. | [File store](../src/Editor.Infrastructure/FileTextDocumentStore.cs) uses strict UTF-8 with no emitted BOM. | Full persistence and platform metadata limits remain documented separately. |
| The project uses MIT licensing and ships applicable third-party notices. | [Project license](../LICENSE), [notices](licenses), and [packaging policy](release-distribution.md). | Final package contents and distribution terms need release-candidate review. |

The native accessibility and platform limits remain explicit in the user guide.
This pass adds no screen-reader, high-contrast, minimum-OS, signing, notarization, or production-installation claim.

## Checks required before final sign-off

- [ ] Inspect the rendered README and every guide page in the intended GitHub and browser views.
- [ ] Confirm the two signed-in reporting workflows without submitting test reports.
- [ ] Check the meaning of external destinations and external fragments.
- [ ] Review final media against the selected release candidate under issue #106.
- [ ] Complete guide-only native workflows under issue #105.
- [ ] Validate artifact links, installed and portable modes, updates, and uninstall behavior under issue #107.
- [ ] Review product claims and the final landing page under issue #104.
- [ ] Record successful hosted verification and native evidence for Windows, Linux, and macOS.
- [ ] Complete the human review and the product acceptance table before closing issues #108 and #48.

No final sign-off is recorded here. The [product review record](product-documentation.md#release-candidate-acceptance-record)
remains the release-candidate acceptance matrix.
