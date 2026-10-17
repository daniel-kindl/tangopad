# Proposed GitHub Issues

These are issue drafts. [Issue #1](https://github.com/daniel-kindl/tangopad/issues/1)
tracks the bootstrap in the M0 milestone. The further tasks below remain drafts.

## M0: Establish the development baseline

| Proposed title | Acceptance criteria |
| --- | --- |
| `chore(repo): activate integration workflow and branch protection` | Create `dev` from the accepted bootstrap. Require review and all three CI checks on `main` and `dev`. Confirm fork pull requests need no secrets. |
| `test(desktop): verify native startup on supported systems` | Record a blank-window launch and clean close on Windows, Linux, and macOS. Document native prerequisites and the selected OS baseline. Automate repeatable checks where practical. |
| `chore(quality): enforce contribution metadata` | Select a minimal check for Conventional Commit squash titles. Verify valid and invalid examples. Keep the workflow permission read-only. |

[Issue #11](https://github.com/daniel-kindl/tangopad/issues/11) tracks the
contribution metadata draft in the table above. Use that issue for the current
acceptance criteria.

## M1: Deliver the first plain-text document slice

M1 authorizes future features through separate issues. None are implemented by bootstrap.

| Proposed title | Acceptance criteria |
| --- | --- |
| `feat(core): define plain-text document behavior` | Define identity, text, and change-state rules from explicit use cases. Add meaningful Core tests. Add API rules that prevent domain filesystem and UI calls. |
| `feat(application): orchestrate document load and save` | Define boundary contracts from real consumers. Test success, cancellation, encoding policy, and observable failures without a filesystem dependency. |
| `feat(infrastructure): add local text persistence` | Create the infrastructure module and ADR. Test actual temporary-file round trips and error cases. Verify save reliability on supported systems. |
| `feat(editor): connect AvaloniaEdit to a text document` | Recheck AvaloniaEdit compatibility. Isolate the control in App. Verify typing and change state without framework types in Core or Application. |
| `feat(formats): identify plain text and Markdown` | Define one authoritative format registry for `.txt`, `.md`, and `.markdown`. Test casing and unknown-extension policy. Keep parsing and rendering out of this issue. |

[Issue #5](https://github.com/daniel-kindl/tangopad/issues/5) tracks the native
startup draft in the table above. Use that issue for the current acceptance criteria.

Schedule Markdown parsing and rendering after the first document slice proves the
contracts. Define separate acceptance criteria for parser adaptation and visual output.
Recheck Markdig before adding it. Defer tabs, exports, plugins, and additional formats.
