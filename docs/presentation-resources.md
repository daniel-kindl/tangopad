# Presentation resources

Issues #22, #23, #24, and #27 share one presentation change in PR #26.
The editor keeps its commands, text rules, and dialog decisions.
[The interface design draft](ux/47-design.md) describes the local shell revision.

## Ownership

EditorViewModel owns observable text, title, dirty state, busy state, edit availability, and commands.
It also owns caret line and column, character and word counts, line-ending style, Word Wrap, and font size.
It uses CommunityToolkit.Mvvm's ObservableObject and generated properties and relay commands.
It serializes document operations and retains the caller's synchronization context.
The desktop caller runs commands and edits on the UI thread.
Tests can use the ViewModel without initializing Avalonia.

EditorWindow binds its controls with compiled bindings and `x:DataType`.
Its code-behind sets the DataContext, platform shortcuts, native focus, and close integration.
EditorBufferAdapter transfers exact text and resets caret and undo for a new document identity.
It checks text equality before assigning the buffer, so typing and undo do not replace their own buffer.
The adapter copies AvaloniaEdit caret positions into one-based numeric ViewModel properties when the caret position changes.
The bottom status row uses compiled bindings and `Text.Status.CaretFormat` to display `Ln 1, Col 1` for an empty document.
The status row also binds `CharacterCount` and `WordCount` through `Text.Status.CharacterCountFormat` and `Text.Status.WordCountFormat`.
The ViewModel counts Unicode scalars and non-whitespace runs together when `Text` changes.
Both labels reuse the `editor-status` style and the existing spacing token.
The status row also displays `UTF-8` and the cached `LineEndings` value.
`LineEndingStyle` and its observable property belong to `Editor.App.ViewModels`.
`PresentationResources.LineEndingLabel` maps that value to shared text resources.
The ViewModel performs no resource lookup. Both visible and screen-reader labels use compiled bindings.
The labels use the existing status style and spacing token.
The status items wrap at narrow window widths so all labels remain visible.

### Encoding and line endings

The supported document store reads and writes strict UTF-8. Every supported document therefore displays `UTF-8`, including an untitled document.
The status provides no encoding selection or conversion.

Line-ending detection reads the current source buffer. It applies these rules:

- `CRLF` is an adjacent carriage return and line feed, treated as one ending.
- `LF` is a line feed without an immediately preceding carriage return.
- `CR` is a carriage return without an immediately following line feed.
- `Mixed` means that the buffer contains at least two of these three ending styles.
- `None` means that the buffer contains none of these endings.

An untitled empty document displays `UTF-8` and `Endings: None`.
Text without a newline also displays `Endings: None`.
Unicode separators such as U+0085, U+2028, and U+2029 do not count as these endings.
For example, `\r\n\n` is mixed CRLF and LF. `\r\r\n` is mixed CR and CRLF.
A trailing carriage return counts as CR. Repeated CRLF sequences display CRLF.
Markdown uses its exact source text, including fenced code.

The text-change handler detects endings in the same Unicode-scalar pass that counts characters and words.
It recognizes CRLF across adjacent scalar values without skipping either character in the existing counts.
The cached value changes only when the detected style changes.
Caret movement, selection, scrolling, and theme changes do not scan the document.
New and Open use the same text-change path. Equal text retains the correct cached value across document identities.
Detection never writes text, normalizes endings, or changes undo history or saved bytes.
The status uses descriptive automation names. Ending changes use polite screen-reader announcements.
The labels do not intercept keyboard focus or editor shortcuts.

### Editor presentation controls

The toolbar checkbox binds to the ViewModel's `WordWrap` property with a compiled two-way binding.
The editor binds directly to that property. AvaloniaEdit disables horizontal scrolling during wrapping and restores it when wrapping stops.
Word Wrap is off by default and survives document changes and app restarts. It does not change the document or undo history.
The editor's compiled `FontSize` binding reads the ViewModel's single observable font-size property.
`EditorPresentation` defines the default and bounds. The ViewModel defines the step.
Ctrl++ and Ctrl+- change the size by 1 pixel within 8–48 pixels. Ctrl+0 resets it to 15 pixels.
macOS uses Command instead of Ctrl.
The plus shortcut accepts Shift. Numpad Add, Subtract, and 0 also work.
The shortcuts require editor focus. Find and Replace inputs retain their keyboard behavior.
Font size survives document changes and app restarts.
[Editor preferences](editor-preferences.md) describes storage, defaults, and startup precedence.
Font changes preserve text, identity, dirty state, selection, caret, and undo history.
Position updates do not write to the editor. The adapter removes its event handlers when the window closes.
A close request uses the same ViewModel operation guard as other replacement requests.

IEditorDialogs isolates platform pickers, unsaved confirmation, and errors.
DesktopEditorDialogs implements it at the window boundary.
Core, Application, and Infrastructure have no MVVM or theme dependencies.
No service container, reactive framework, or second orchestration layer is introduced.

## Resource keys and scope

App.axaml merges two application resource dictionaries:

- Resources/Text.axaml owns immutable visible text under `Text.<area>.<name>` keys.
- Resources/Tokens.axaml owns sizing, spacing, insets, typography, radii, and theme dictionaries.

Keys describe their role, such as `Size.Dialog.Width`, `Spacing.Actions`, and `Brush.Text.Primary`.
The Light and Dark dictionaries define the same semantic brush roles.
Shell, canvas, text, border, and accent roles define matching light and dark palettes.
Interaction roles retain Fluent or AvaloniaEdit resources.
Markdown Heading, Code, and Link roles define source colors for each theme.
Fluent retains focus, selection, and control interaction behavior.
The shell uses a shared neutral palette and blue accent roles.
Help contains About and Check for Updates. The status bar contains document information only.
App requests `ThemeVariant.Default`, so the platform theme applies by default.
Changing the application theme updates existing controls through DynamicResource references.

Styles/EditorStyles.axaml consolidates dialog layout, typography, and control presentation.
Default window dimensions are 1040 by 760, with minimum dimensions of 400 by 250.
Dialogs stay 460 pixels wide. The compact icon toolbar uses shared target dimensions and wraps when necessary.
The editor starts at 15 pixels with a monospace font.

## Resource selection and exceptions

Use StaticResource for immutable text, sizes, spacing, typography, and radii.
Use DynamicResource for semantic brushes that can change with the active theme or accent.
Theme dictionaries use StaticResource aliases to select the corresponding Fluent theme resource.
Views and styles consume those aliases through DynamicResource.

Use compiled Binding for document text, titles, state, and commands.
The ViewModel formats its title with immutable labels supplied from Text.axaml.
Do not put document contents or runtime file paths into static resources.

Native picker titles and default names come from PresentationResources.
ErrorDialog receives the actual runtime error message through its constructor.
Operating-system and persistence exception messages retain their original text.
Keyboard gesture tooltips come from Avalonia's KeyGesture formatting for the current platform.
Their key letters and modifier names are protocol inputs, not localized copy.
These code-behind values are deliberate exceptions to static AXAML text resources.

Names, classes, binding paths, orientation, docking, wrapping, booleans, and scrollbar behavior remain local declarations.
These declarations define structure or behavior rather than shared visual values.
No localization framework or translated copy is added.

Markdown source highlighting uses named theme brush roles and the native XSHD adapter.
See [Markdown highlighting](markdown-highlighting.md) for the baseline and state guarantees.

## Verification

Independent ViewModel tests cover commands, notifications, reentrancy, cancellation, and pending-save edits.
Headless tests load compiled views, resolve resource keys, switch themes on existing controls,
and exercise real editor typing, exact newlines, shortcuts, undo, and close protection.
Architecture tests reject duplicate shared keys, inconsistent theme roles, inline visible text,
selected visual literals, and StaticResource references on theme-sensitive properties.
There are no per-view exceptions to the checked AXAML properties.
Native startup and Linux native acceptance remain separate checks.

## Markdown preview

Show Preview adds a read-only native text pane beside Markdown source.
Hide Preview restores the full-width editor. Plain-text and untitled sessions hide
the control. Links remain inactive and images show descriptions only.
The pane uses existing semantic theme brushes, preview tokens, and text resources.
Its runtime text comes exclusively from the bounded Markdown adapter.
[ADR 0008](adr/0008-safe-markdown-preview.md) defines the security and resource limits.
