# ADR 0003: Check native startup from the shared verification command

Status: Accepted

Date: 2026-10-17

## Context

Compile and architecture tests do not open a native window.
The blank shell must start and close on Linux, Windows, and macOS.
A headless Avalonia test would not use the Win32, AppKit, or X11 backend.
A flag inside the shell would not send the platform close message.

## Decision

Run `Editor.Desktop.SmokeTests` from the existing test step in `eng/verify.proj`.
The test starts the built `Editor.App` executable.
It sends `WM_DELETE_WINDOW` on Linux, `WM_CLOSE` on Windows, or the macOS close button.
It requires exit code 0.
The test project references `Editor.App` so the shell builds first.
The test does not call shell types.
Do not add a second build or a new package.

Linux uses the current X11 display.
When that display does not open, the test starts Xvfb if that command exists.
A host that cannot open a window fails the check.
The failure text starts with `NOT TESTED:` and gives a follow-up.
The shell has no test-only startup mode.

## Consequences

`dotnet msbuild eng/verify.proj` needs a display, or Xvfb on Linux.
The Linux CI job installs `libx11-6`, `libice6`, `libsm6`, `libfontconfig1`, and `xvfb` before that command.
Windows and macOS use the interactive session of the hosted runner.
A result on one host does not prove another host.
See [native startup](../native-startup.md) for the baseline and the recorded results.
