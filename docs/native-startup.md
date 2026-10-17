# Native startup

The blank shell must open a native window and close with exit code 0.
`dotnet msbuild eng/verify.proj` runs this check with the other tests.
The check does not add editing, documents, parsing, or rendering.

## Baseline

The pinned SDK is in [global.json](../global.json).
The current SDK is 10.0.402.
That SDK includes the .NET 10.0.13 runtime.
The shell targets `net10.0`.

| Host | Session | Close action |
| --- | --- | --- |
| Linux with X11 | An X11 display, or Xvfb when no display is open | `WM_DELETE_WINDOW` |
| Windows | An interactive desktop session | `WM_CLOSE` through `PostMessageW` |
| macOS | An interactive desktop session | The window close button |

`user32.dll` exports `PostMessageW`.
`PostMessage` is a macro, and it is not an entry point.

CI runs the same verification command on `ubuntu-24.04`, `windows-2025`, and `macos-15`.
Linux uses the Avalonia X11 backend.
A Wayland session reaches that backend through XWayland.
The shell does not reference the native Wayland package.

Avalonia publishes the broader platform tiers in its
[platform policy](https://docs.avaloniaui.net/docs/supported-platforms).
This page records the hosts this repository checks.

## Native libraries

The shell adds no native package beyond `Avalonia.Desktop` 12.1.4.
Linux still needs host libraries before the process can open a window:

- `libX11`
- `libICE`
- `libSM`
- `fontconfig`

Debian and Ubuntu package those libraries as `libx11-6`, `libice6`, `libsm6`, and `libfontconfig1`.
Fedora supplies the same libraries as `libX11`, `libICE`, `libSM`, and `fontconfig`.
A headless Linux host also needs the `Xvfb` command.
The Linux CI job installs the Debian packages and `xvfb` before verification.
Windows and macOS use the native binaries inside the Avalonia packages.
Those two hosts need no extra native package for this check.

## Check

`Editor.Desktop.SmokeTests` builds `Editor.App` and starts its executable.
The test does not call shell types.
It waits until a native window belongs to that process.
It then sends the close action in the table above.
The shell must exit with code 0.

The executable path is `src/Editor.App/bin/<Configuration>/net10.0/`.
Release verification uses the Release build.
A missing executable fails the check.
Build that path, then run `dotnet msbuild eng/verify.proj` again.

A host that cannot open a window does not pass.
The test output starts with `NOT TESTED:` and states a follow-up.
A host that opens a window and then fails starts with `FAILED.`.

## Results

Date: 2026-10-17.

| Host | Architecture | Runtime | Display | Result |
| --- | --- | --- | --- | --- |
| Fedora Linux 44 (Container Image) | x64 | .NET 10.0.13 | X11 `:0` | PASS. `WM_DELETE_WINDOW` closed the shell with exit code 0. |
| Ubuntu 24.04.5 LTS | x64 | .NET 10.0.13 | Xvfb `:99` | PASS. `WM_DELETE_WINDOW` closed the shell with exit code 0. |
| Microsoft Windows 10.0.26100 | x64 | .NET 10.0.13 | Win32 desktop | PASS. `WM_CLOSE` closed the shell with exit code 0. |
| macOS 15.7.9 | arm64 | .NET 10.0.13 | macOS desktop | PASS. `AXCloseButton` closed the shell with exit code 0. |

The Fedora result is the output of `Editor.Desktop.SmokeTests.BlankShellStartsAndCloses` on this host.
The session type was Wayland.
The shell used XWayland through `DISPLAY=:0`.

Run `39036041068`, job `Verify (ubuntu-24.04)`, recorded the Ubuntu result.
The TRX message is:

`PASS. The shell opened a native window. The check sent WM_DELETE_WINDOW. The shell exited with code 0. Display: Xvfb :99. Host: Ubuntu 24.04.5 LTS. Architecture: X64. Runtime: .NET 10.0.13.`

Run `39036549092` also passed `Verify (ubuntu-24.04)`.
That run's uploaded artifact does not contain the smoke-test message.

Run `39036549092`, job `Verify (windows-2025)`, recorded the Windows result.
The job image is `windows-2025-vs2026`.
The job reports Microsoft Windows Server 2025 Datacenter.
The TRX message is:

`PASS. The shell opened a native window. The check sent WM_CLOSE. The shell exited with code 0. Display: Win32 desktop. Host: Microsoft Windows 10.0.26100. Architecture: X64. Runtime: .NET 10.0.13.`

Run `39036041068` on that job failed before this result.
The check found a visible window.
The call to `PostMessage` then threw `EntryPointNotFoundException`.

Run `39036549092`, job `Verify (macos-15)`, recorded the macOS result.
The job image is `macos-15-arm64`.
The TRX message is:

`PASS. The shell opened a native window. The check sent AXCloseButton. The shell exited with code 0. Display: macOS desktop. Host: macOS 15.7.9. Architecture: Arm64. Runtime: .NET 10.0.13.`

## Limits

A result on one host does not prove another host.
The check does not measure rendering, input, or startup time.
[PR #6](https://github.com/daniel-kindl/tangopad/pull/6) found a TRX file name collision on macOS and Ubuntu.
The uploaded artifact omitted the smoke-test message, although the job log reported that the assembly passed.
Verification now writes `Editor.Architecture.Tests.trx` and `Editor.Desktop.SmokeTests.trx`.
It checks that each report contains results from the expected assembly before CI uploads the directory.
A 32-bit Linux process is `NOT TESTED`.
The X11 close path uses the 64-bit Xlib event layout.
Headless Linux uses Xvfb, which has no GPU.
Avalonia can fall back from GLX to software rendering on that display.
The Fedora result used the existing X11 display, not Xvfb.
macOS close uses `osascript` to activate the window close button.
That action needs assistive access for the session that runs the test.
A denied request is `NOT TESTED`, not a pass.
