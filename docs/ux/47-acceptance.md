# Accessibility and display acceptance

The desktop shell merged in [PR #101](https://github.com/daniel-kindl/tangopad/pull/101) at `78bb5929`.
Issue [#47](https://github.com/daniel-kindl/tangopad/issues/47) remains open.
This follow-up adds repeatable checks and fixes the checked-state indicator on toolbar toggles.
It does not establish complete accessibility acceptance.

## Automated coverage

| Area | Check | Limit |
| --- | --- | --- |
| Icon commands | Automation peers expose command names, button roles, and enabled states. | Does not verify speech output or native screen-reader navigation. |
| Line numbers | Space changes the checkbox state, the reported toggle state, and the editor gutter. | Tests one keyboard-operated toggle through the real control. |
| Checked state | A checked icon has a visible outline with at least 3:1 contrast against its background. | Does not establish OS high-contrast support. |
| Keyboard focus | The focused toggle has a thicker outline with at least 3:1 contrast. | Other control types retain their existing Fluent focus behavior. |
| Palette | Primary text, muted text, accent text, and icons meet the documented contrast thresholds in both themes. | Covers the named opaque brush pairs, without a claim of full WCAG compliance. |
| Dynamic scaling | Headless windows change among 100%, 125%, 150%, and 200% scaling. | Simulates display changes. It does not move a window between physical monitors. |
| Document fidelity | Scale and theme changes preserve exact mixed endings, Unicode, selection, caret, undo, dirty state, and saved bytes. | Uses a small Markdown fixture. |
| Narrow layouts | Commands and status stay within 400 × 250, 400 × 600, and 640 × 480 logical windows. | Preview needs the larger height. |
| Linux native scale | The real X11 window size matches each scale. Accessible button bounds scale with the window. | Uses Xvfb at fixed 96 DPI with an explicit scale override. |
| Linux accessibility tree | AT-SPI exposes named controls and reports Word Wrap changes after keyboard input. | A successful metadata audit does not mean the source text is accessible. |

The headless checks run through the existing test project on the Linux, Windows, and macOS CI matrix.
The native audit runs only in the Linux job.

## Reproduce the checks

Run the repository gate:

```sh
dotnet msbuild eng/verify.proj
```

Run the existing native workflows:

```sh
python3 eng/check-native-editor.py
```

Run the accessibility and scaling audit:

```sh
python3 eng/check-native-accessibility.py
```

The audit requires Xvfb, xdotool, dbus-daemon, gdbus, Pillow, libatspi, and at-spi2-core.
CI installs the Ubuntu packages before it runs the audit.
The audit starts a private D-Bus session and display. It does not change the active desktop's accessibility settings.
Each scale has a separate application profile.
The audit does not require Python GObject bindings.

Evidence appears under `artifacts/native-accessibility-<suffix>/`:

- `result.json` records tested scales, native window dimensions, and outstanding acceptance work.
- `tree-<scale>.json` records names, roles, bounds, toggle states, and text-interface availability.
- `narrow-<scale>-<theme>.png` records native rendering at narrow widths.
- Process logs record startup and service errors.

The audit compares physical dimensions with the requested scale before accepting a case.
It checks a rendered canvas pixel after each appearance change.
It fails if a required control is missing, bounds are wrong, keyboard state does not change, or a process fails.
`automatedChecks: PASS` covers those checks only.
The report lists uncompleted work in `remainingAcceptance`, and prints each item in the log.
CI uploads these files with the other Linux native evidence.

## Confirmed findings

### Checked state depended on color

The merged toolbar changed a toggle's background tint without an additional state marker.
A new test failed in both themes because the checked outline had zero thickness.
The fix adds a contrasting outline to the checked state.
Keyboard focus uses a thicker outline and a high-contrast foreground color.
The target size remains 32 logical pixels.

### The source editor lacks a text interface on Linux

The native audit enters a nonempty document before inspecting the AT-SPI tree.
`Document editor` appears as a `panel`.
Neither that node nor its descendants exposes an AT-SPI `Text` interface.
Toolbar metadata passes, but assistive technology cannot obtain source text through that interface.
This is a blocker for Linux screen-reader acceptance.

A bounded follow-up must provide an accessible editor text provider or select a supported upstream implementation.
It must preserve exact text and editing behavior.
Acceptance must include reading document text, navigating the caret and selection, and observing changes through a native screen reader.
A test of `AutomationProperties.Name` alone is insufficient.

### OS high contrast remains unverified

Normal light and dark palette tests do not simulate an OS contrast theme.
The pinned Linux [settings adapter](https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.4/src/Avalonia.FreeDesktop/DBusPlatformSettings.cs)
reads appearance and accent settings but does not read the portal contrast preference.
Tangopad has no explicit contrast-mode resource dictionary.
No OS high-contrast acceptance result is recorded here.

A bounded follow-up must define how contrast preferences reach shell, editor, selection, syntax colors, dialogs, and focus indicators.
It must check changes while the application is already open.
The selected state must remain identifiable when color distinctions are unavailable.

## Native platform acceptance still required

Use a copy of the sample documents from [the review guide](47-testing.md).
Record OS version, screen-reader version, display scale, theme, application revision, and the result for each case.

| Case | Linux | Windows | macOS |
| --- | --- | --- | --- |
| Native command names and scaled bounds | Automated with AT-SPI and X11 | Not tested | Not tested |
| Native spoken source text and selection | Blocked by the missing text interface | Not tested | Not tested |
| Screen-reader focus announcements in menus and dialogs | Not tested with Orca | Not tested | Not tested |
| OS high-contrast presentation | Not tested | Not tested | Not tested |
| Physical monitor transitions at different scales | Not tested | Not tested | Not tested |
| Touchpad scrolling and pointer selection across scale changes | Not tested | Not tested | Not tested |

For each platform:

1. Start the platform screen reader.
2. Open the Markdown sample.
3. Navigate menus and toolbar controls with the keyboard.
4. Read source text and move the caret through multiple lines.
5. Change a selection and check the reported range.
6. Open Find and Go To Line.
7. Close each dialog and check the focus announcement.
8. Open About and Updates through Help.
9. Enable the OS contrast preference.
10. Check source, syntax, selection, focus, menus, and dialogs in both appearance modes.
11. Move the window between displays with different scales.
12. Check text clarity, pointer selection, menus, dialogs, scrolling, and exact saved bytes.

Keep issue #47 open until the unresolved cases have recorded native results.

## Local result

The Linux repository gate passed with zero build warnings and errors.
Its reports contain 720 passed tests and one Windows-only skip.
The existing native editor run passed all 87 workflow steps.
The rebuilt application passed the native accessibility metadata and rendering checks at all four scales in both themes.
The nonempty source editor lacked an AT-SPI text interface in every case.
These results leave the native acceptance cases above open.
