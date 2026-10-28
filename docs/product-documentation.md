# Product documentation review

[Issue #48](https://github.com/daniel-kindl/tangopad/issues/48) blocks the first broadly distributed stable release.
This change prepares documentation for the redesigned editor. It does not establish release-candidate acceptance.
No stable release was listed when this draft was prepared.

## Ownership and focused issues

Product documentation lives under `docs/user/`. The README links that guide and the developer entry point.
Existing engineering references remain under `docs/`. No application namespace or dependency changes.
The guide uses Markdown without a documentation website or a search-service dependency.

| Deliverable | Follow-up | Completion requirement |
| --- | --- | --- |
| Product README | [#104](https://github.com/daniel-kindl/tangopad/issues/104) | Review highlights, status, artifact links, and final hero against the release candidate. |
| User guide | [#105](https://github.com/daniel-kindl/tangopad/issues/105) | Follow the tutorial and review reference pages on each native platform. |
| Product media | [#106](https://github.com/daniel-kindl/tangopad/issues/106) | Capture the final build and inspect every image and caption. |
| Installation and releases | [#107](https://github.com/daniel-kindl/tangopad/issues/107) | Validate published formats, OS limits, trust prompts, updates, and removal. |
| Documentation QA | [#108](https://github.com/daniel-kindl/tangopad/issues/108) | Record complete evidence and review accuracy before stable publication. |

[Issue #49](https://github.com/daniel-kindl/tangopad/issues/49) owns the full changelog workflow.
[Issue #52](https://github.com/daniel-kindl/tangopad/issues/52) owns the release go/no-go review.
Issues #45 and #47 are closed, but their engineering records still list native acceptance gaps.
Use the [release evidence requirements](release-distribution.md#required-native-evidence) and
[accessibility record](ux/47-acceptance.md) as evidence boundaries.
A closed issue does not replace a recorded package or accessibility result.
The [native user-guide record](user-guide-verification.md) documents a Linux candidate run and the remaining platform checks.

## Capture real application images

The committed images show the 1.0.0 Linux AppImage from a Package candidates run on `dev`.
[Capture metadata](user/media/capture.json) records the source commit, source diff hash,
executable and assembly hashes, host, sample hash, dimensions, and image hashes.
It also records the build run, the artifact name, and the AppImage hash.
The AppImage has one single-file executable, so the executable hash and the assembly hash are the same.
The images are not final release-candidate evidence until the draft release exists.
If the interface changes before the draft release, capture the images again from the draft release candidate.
The capture uses only [the synthetic sample](user/samples/field-notes.md).
No personal document, user profile, or local file path appears in the images.

The Linux capture requires the native editor workflow dependencies from
[the Verify workflow](../.github/workflows/verify.yml), plus the pinned SDK.
Run as a non-root user from the repository root after verification:

```sh
dotnet msbuild eng/verify.proj
python3 eng/capture-product-media.py
```

The script builds Release with no restore. It starts an isolated Xvfb display,
private D-Bus session, GTK file portal, and disposable application profile.
It never sends input to the active desktop.
It opens the sample through the actual file picker, selects Light through the menu,
shows preview, and opens Replace with the query `draft`.
It captures the 1040 × 760 application client area without window-manager decorations.
It crops the display to that area and optimizes PNG encoding without compositing or altering the UI.
The source sample remains unchanged. The editor must close with exit code 0.

Inspect both PNGs in `artifacts/product-media/` before copying them to `docs/user/media/`.
Copy `capture.json` with those images. Keep process logs in ignored artifacts.
The manifest identifies the actual build. Do not relabel a development capture as a release candidate.
For the final pass, capture the selected release-candidate source and compare against its native package.
Record that comparison in the review table before marking release-candidate validation complete.

Product PNGs must be between 400 × 250 and 1600 × 1200 pixels.
Each PNG must be at most 350 KiB. Their combined size must be at most 1 MiB.
Keep image alt text and a platform caption beside each use.
The guide includes text steps for every illustrated task. Current workflows need only still images.
If a later clip adds useful information, retain those text steps and provide a still fallback.
Do not add animation that carries the only instructions for a task.

## Check the documentation

Run the local checks:

```sh
python3 eng/test-check-docs.py
python3 eng/check-docs.py
```

The checker uses the Python standard library and tracked or unignored Markdown files.
It checks inline Markdown links and images, referenced local paths, ATX heading anchors,
image descriptions, the product PNG inventory, dimensions, byte budgets, and capture hashes.
Fenced code examples are excluded. Duplicate heading anchors are supported.
Hosted Linux CI runs the checker and its regression fixtures.
The shared .NET gate remains `dotnet msbuild eng/verify.proj` on all three hosted operating systems.

The checker does not request external URLs or implement a complete Markdown parser.
Reference-style links, raw HTML, and non-ATX headings require manual review if introduced.
It does not prove installation behavior, visual correctness, accessibility, or freshness against a future release candidate.
Review external links, rendered pages, and product instructions during final acceptance.
The [documentation QA record](documentation-qa.md) gives the live rendering command,
HTTP review procedure, development results, and remaining manual checks.

## Release-candidate acceptance record

For every result, record the release version, full source commit, artifact filename and hash,
OS version, CPU, package and storage mode, reviewer, date, and evidence location.
Use the same candidate across documentation review. Repeat affected checks when that candidate changes.

| Check | Linux x64 | Windows x64 | macOS x64 | macOS arm64 |
| --- | --- | --- | --- | --- |
| Production artifact install, first launch, and OS trust prompts | Pending | Pending | Pending | Pending |
| Guide-only New, Open, edit, Save, Save As, and reopen | Pending | Pending | Pending | Pending |
| Unsaved Cancel, failed save, disk conflict, and recovery advice | Pending | Pending | Pending | Pending |
| Shortcuts, Find/Replace, navigation, appearance, and preview limits | Pending | Pending | Pending | Pending |
| Portable and installed state, update, failure recovery, and removal | Pending | Pending | Pending | Pending |
| Final screenshots and captions match the candidate | Pending | Pending | Pending | Pending |
| Licensing, privacy claims, native accessibility limits, and OS support | Pending | Pending | Pending | Pending |

The two development screenshots were visually inspected on Linux.
The local .NET gate and documentation checker results belong in the change report.
They do not fill any production-package cell in this table.
Before closing #48, also check the following items:

- [ ] Confirm the public versioned artifact links and remove candidate-only wording where the release justifies it.
- [ ] Render every guide page and the README in the intended Markdown viewer.
- [ ] Check all external links, image descriptions, captions, dimensions, and navigation.
- [ ] Confirm that offline claims permit explicit update checks and project-link navigation.
- [ ] Check the package license notices and all supported installation paths.
- [ ] Review actual OS prompts without claiming platform signing or notarization when absent.
- [ ] Link successful hosted verification for the selected source commit.
- [ ] Complete the focused issues and record a human documentation review.
