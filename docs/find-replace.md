# Find and Replace

[Issue #29](https://github.com/daniel-kindl/tangopad/issues/29) adds literal search to the current document.
The panel uses the existing text resources, semantic tokens, and compiled bindings.
No dependency changes are required.

## Matching

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
It uses literal `IndexOf` searches with constant-size boundary checks and no regular expressions.
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
Replacement uses TextDocument.Replace inside RunUpdate, in reverse offset order.
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

Local verification on Fedora Linux 44, x86_64, passes `dotnet msbuild eng/verify.proj`
with zero build warnings and errors. The Windows-only filesystem test skips on Linux.
The Linux native acceptance script includes search, literal replacement, grouped undo,
and search cancellation alongside the existing document and picker checks.
It uses Xvfb, the GTK portal, and a private D-Bus session.

Windows and macOS native interaction checks require their respective desktop sessions.
Hosted CI results and local verification results are recorded in the linked pull request.
