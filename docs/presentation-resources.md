# Presentation resources

Issues #22, #23, #24, and #27 share one presentation change in PR #26.
The editor keeps its existing layout, commands, text rules, and dialog decisions.

## Ownership

EditorViewModel owns observable text, title, dirty state, busy state, edit availability, caret line and column, and commands.
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
Each role aliases an existing Fluent or AvaloniaEdit resource.
There is no second raw palette. Fluent retains its platform accent and control states.
App requests `ThemeVariant.Default`, so the platform theme applies by default.
Changing the application theme updates existing controls through DynamicResource references.

Styles/EditorStyles.axaml consolidates dialog layout, typography, and control presentation.
Window dimensions stay 960 by 680, with minimum dimensions of 400 by 250.
Dialogs stay 460 pixels wide. Toolbar spacing stays 8 pixels.
The editor stays at 15 pixels with a monospace font.

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

## Verification

Independent ViewModel tests cover commands, notifications, reentrancy, cancellation, and pending-save edits.
Headless tests load compiled views, resolve resource keys, switch themes on existing controls,
and exercise real editor typing, exact newlines, shortcuts, undo, and close protection.
Architecture tests reject duplicate shared keys, inconsistent theme roles, inline visible text,
selected visual literals, and StaticResource references on theme-sensitive properties.
There are no per-view exceptions to the checked AXAML properties.
Native startup and Linux native acceptance remain separate checks.
