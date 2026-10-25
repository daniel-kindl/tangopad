# Find and Replace

[Issue #29](https://github.com/daniel-kindl/tangopad/issues/29) adds literal search to the current document.
The panel uses the existing text resources, semantic tokens, and compiled bindings.
No dependency changes are required.

## Literal matching

Search uses ordinal comparison. Match case selects case-sensitive comparison.
The default uses `StringComparison.OrdinalIgnoreCase`, independent of the current culture.
Search preserves Unicode and exact LF, CRLF, and CR sequences.
It does not normalize text. A pasted query can span lines.
Match whole word is off by default. It filters the same literal matches for
navigation, match counts, Replace current, and Replace all. Match case remains independent.
Both ends of a whole-word match must be Unicode scalar boundaries.
The scalar before the match and the scalar after it must be absent or non-word characters.
The query itself can contain punctuation, whitespace, or multiple words.

Word characters use these Unicode categories:

- Letters: uppercase, lowercase, titlecase, modifier, and other letters.
- Decimal digits, including non-ASCII decimal digits.
- Marks: nonspacing, spacing combining, and enclosing marks.
- Connector punctuation, including `_` and `‿`.

Thus `cat` does not match inside `cat_cat`, `cat2`, `猫cat`, or `cat` followed by a combining mark.
The query `e` does not match the start of `e\u0301`, but the exact query `e\u0301` matches that complete sequence.
Precomposed `é` and decomposed `e\u0301` remain different text.
Spaces, line endings, apostrophes, hyphens, and other punctuation separate words.
Both straight and curly apostrophes separate words, so `cat` matches in `cat's` and `cat’s`.
Supplementary letters and decimal digits are word characters. Emoji separate words.
Whole-word matches cannot split a UTF-16 surrogate pair. Isolated surrogates count as non-word characters.
This rule does not perform linguistic segmentation or grapheme-cluster matching.
Literal mode uses `IndexOf` searches with constant-size boundary checks.
An empty query has no matches and disables navigation and replacement.
A nonempty query without results displays No matches.

Find next and Find previous include overlapping candidates and wrap at the document boundaries.
For example, `ana` has two matches in `banana`.
Navigation starts at the editor selection or caret.
Find next includes a match at the caret unless that match is already selected.
Find previous selects a match before the selection, or wraps to the last match.
Search does not change the document or its undo history.

Replace current requires an exact selection of a valid match.
A changed query, text, matching option, or selection updates command availability.
The replacement remains selected after the operation.
Replace all chooses non-overlapping matches from left to right.
For example, replacing `ana` with `x` in `banana` produces `bxna` with a replacement count of one.
Replacement text is literal, including dollar signs, backslashes, Unicode, and newlines.
Inserted text is not searched again during the same replacement operation.

The panel reports match and replacement counts separately.
Text, query, or matching option changes clear the previous replacement count.
Closing the panel preserves the query and replacement text.
It returns focus to the editor without changing the document.
Matching options remain in the current window across panel changes, New, Open, and Reload.
Each new window starts with Match whole word off. The option is not saved to preferences.

## Regular expressions

[Issue #81](https://github.com/daniel-kindl/tangopad/issues/81) adds the **Use regular expressions** option.
It starts off and stays local to the window. It survives panel changes, New, Open, and Reload.
Literal mode retains the rules above, including overlapping navigation and literal replacement text.

Regex mode uses the [.NET regex language](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference).
Patterns support captures, Unicode categories, backreferences, lookarounds, escaping, and inline options.
The constructor sets `RegexOptions.CultureInvariant` and adds `IgnoreCase` when Match case is off.
Regex case folding follows .NET invariant rules. Literal mode continues to use ordinal comparison.
[Inline options](https://learn.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-options) can override Match case, such as `(?i)` and `(?-i)`.
The application does not enable compiled, right-to-left, ECMAScript, or nonbacktracking options.

Patterns operate on the exact UTF-16 source. Text and line endings are not normalized.
By default, `.` excludes LF and `^` and `$` apply to the complete input.
Use `(?s)` for dot matching across LF and `(?m)` for line anchors.
.NET line anchors use LF, not isolated CR. A CRLF line pattern can use `(?m)^cat\r?$`.
A pattern can explicitly use `\r\n`, `\n`, and `\r` to distinguish source line endings.
.NET character classes can match UTF-16 code units rather than complete supplementary Unicode scalars.

Match whole word uses the scalar boundary filter described above on each regex result.
The application does not rewrite patterns or insert `\b`. Both options remain available together.
Regex candidates are non-overlapping and ordered from left to right before whole-word filtering.
Filtering does not restart the engine inside a rejected result.
For example, `ana` has one regex match in `banana`, while literal navigation has two.
A zero-width lookaround can be part of a consuming match, such as `(?<=foo)\d+`.
If any candidate consumes zero characters, the entire search fails with panel feedback.
This includes `^`, `$`, `(?=ana)`, and a trailing empty result from `a*`.
Navigation and replacement stay disabled until the pattern produces only consuming matches.

Regex replacement supports this explicit subset of .NET substitution syntax:

| Replacement text | Expansion |
| --- | --- |
| `$1`, `$2`, `${1}` | The final capture of an existing numbered group. |
| `${name}` | The final capture of an existing named group. |
| `$0`, `${0}`, `$&` | The complete match. |
| `$$` | A literal dollar sign. |
| Other text | Exact literal text, including backslashes and actual line endings. |

An existing group that did not participate expands to empty text.
An unknown group, an incomplete token, or another dollar token disables both replacement commands.
For example, `$10` requires group 10. It does not mean group 1 followed by `0`.
Use `${1}0` for that result. Prefix, suffix, entire-input, and last-group substitutions are unsupported.
Thus `$` must be written as `$$`. `\n` in replacement text remains a backslash and `n`.
The Replace panel shows a capture syntax hint and invalid-replacement feedback.
Find ignores the hidden replacement text, including invalid tokens and oversized templates.
Opening Replace starts substitution preparation and validates the current template.
Navigation remains available when only replacement text is invalid.
Changing replacement text clears its previous replacement count.

All substitutions are prepared from one source snapshot before the editor changes.
Replace current requires the exact selected match and keeps the expanded text selected.
Replace all applies each accepted match once and creates one undo operation.
Inserted text is not searched again within that operation. The replacement count describes completed edits.
Source and preview use the existing document synchronization and UTF-8 persistence paths.

### Execution limits

Regex scans and substitution preparation run on a worker thread.
The panel shows Searching while commands wait for complete results.
Only one regex worker runs per window. A newer request cancels its predecessor.
Query, option, panel mode, text, and buffer changes invalidate obsolete results.
Replacement changes invalidate results in Replace mode.
Selection changes reuse the completed results. Close and window disposal cancel pending work.
Reopening the panel starts a cancelled scan again. Replacement follows the current `CanEdit` permission.

The current bounds are:

| Limit | Value |
| --- | --- |
| Source length | 1,000,000 UTF-16 code units. |
| Pattern length | 4,096 UTF-16 code units. |
| Replacement template length | 4,096 UTF-16 code units. |
| Accepted matches | 20,000. |
| Each .NET match attempt | 50 ms timeout. |
| Scan and substitution preparation | 250 ms elapsed budget. |
| Total expanded replacement text | 8,000,000 UTF-16 code units. |
| Resulting document length | 8,000,000 UTF-16 code units. |

The elapsed budget is checked between match attempts and substitutions.
.NET timeout checks are cooperative. These values are safety limits rather than exact wall-clock guarantees.
Cancellation also waits for an active match attempt to finish or time out.
Size bounds apply only in regex mode.

Invalid patterns, timeouts, zero-length candidates, and exceeded limits show nonmodal panel feedback.
Search failures discard partial match results. Replacement failures never edit the document.
Output-limit failures retain navigation results but disable replacement.
The next successful request clears the feedback. No partial replacement plan is applied.

## Presentation boundary

SearchViewModel owns panel state, match offsets, command availability, and replacement counts.
It references no Avalonia controls and receives an internal ISearchBuffer contract.
EditorViewModel exposes the search state and transfers its edit permission.
Search navigation remains available during file operations.
Replacement follows the existing CanEdit permission, including edits during Save.

EditorBufferAdapter implements ISearchBuffer with the existing AvaloniaEdit document.
It uses the existing ViewModel text as the search input.
Match offsets are derived state. They do not own document contents or a clean baseline.
Selection changes update command availability without repeating the text search.
The adapter selects results and brings the caret into view without moving keyboard focus.
The adapter combines per-match text and unchanged gaps into one contiguous TextDocument.Replace inside RunUpdate.
This bounds editor update overhead for larger batches. Undo retains the replaced span, including its unchanged gaps.
This produces one undo operation for Replace all and preserves earlier undo entries.
TextChanged transfers replacements and undo to the existing DocumentSession.
Save and unsaved-close confirmation therefore retain their existing behavior.

EditorWindow owns platform gestures, query-field focus, and Escape handling.
Enter and Shift+Enter navigate while focus is in a search field.
Enter retains its editor behavior when focus is in the document.
Find and Replace focus and select the query field.
Labels target their fields, controls retain keyboard focus indicators, and status uses a polite live region.
The panel uses the existing light and dark resources.

## Verification

SearchViewModelTests exercises commands without Avalonia initialization.
It covers wrap navigation, overlapping candidates, Unicode, mixed line endings,
ordinal case comparison, literal replacement, empty queries, and command availability.
Whole-word cases cover Unicode categories, supplementary scalars, combining sequences,
identifier fragments, punctuation, mixed line endings, and repeated input.
WholeWordSearchTests checks keyboard toggling in both themes, accessible names,
selection and caret stability, wrap navigation, replacements, grouped undo and redo,
window-local options, document transitions, and exact Markdown persistence.
Headless EditorBindingTests exercise production AXAML, keyboard gestures, query focus,
selection, horizontal scrolling, replacement, grouped undo and redo, dirty state, Save, and close protection.
Architecture tests enforce text and token resources, including field accessibility names.

RegexSearchViewModelTests covers regex syntax, capture substitution, Unicode, case folding,
line endings, whole-word filtering, timeouts, cancellation, limits, and replacement failures.
RegexSearchTests checks the compiled controls in both themes, accessibility, status, focus,
selection, grouped undo and redo, document transitions, Markdown preview, and exact UTF-8 bytes.
The native acceptance script also exercises capture replacement, undo, redo, and invalid-pattern and empty-match feedback.

Local verification on Fedora Linux 44, x86_64, passes `dotnet msbuild eng/verify.proj`
with zero build warnings and errors. The Windows-only filesystem test skips on Linux.
The Linux native acceptance script includes search, literal replacement, grouped undo,
and search cancellation alongside the existing document and picker checks.
It uses Xvfb, the GTK portal, and a private D-Bus session.

Windows and macOS native interaction checks require their respective desktop sessions.
Hosted CI results and local verification results are recorded in the linked pull request.

### Regex verification on 2026-10-21

The first headless toggle test failed because the regex checkbox was absent.
The hidden-replacement regression test first failed because Find reported a replacement error.
All 54 focused regex cases now pass. The repository gate passes with zero Debug and Release build diagnostics.
Seven validated TRX reports contain 454 passed tests and one Windows-only skip.
Linux native startup passes. The native acceptance script passes all 63 workflow steps.
Screenshots confirm the regex option, capture hint, disabled commands, invalid-pattern feedback, and empty-match feedback.
Local evidence is in `artifacts/native-editor-hodzesz9`.
Windows and macOS native interaction remain untested on this host. Hosted checks run on the pull request.

A Release benchmark on this Fedora Linux 44 x86_64 host used 405,000 UTF-16 code units and 15,000 matches.
Scanning and substitution preparation took 149.3 ms. Applying the batch took 172.4 ms.
A separate 180,000-unit scan with 15,000 matches took 33.3 ms.
The editor benchmark also checks exact output, one-step undo, and retention of the original source.

After a Release build, repeat the benchmark with:

```sh
dotnet test --project tests/Editor.App.Tests --configuration Release --no-build --no-restore -- --filter-method '*Benchmark' --show-live-output on
```
