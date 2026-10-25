# ADR 0002: Use SDK tools through one verification entry point

Status: Accepted, revised for PR #26

Date: 2026-10-17

## Context

Local developers, agents, and CI need the same checks on three operating systems.
The verification tools must remain proportionate to the editor's current needs.

## Decision

Use `dotnet msbuild eng/verify.proj`. MSBuild runs locked restore with NuGet audit,
format verification, Release compilation with SDK analyzers, xUnit tests, and TRX report validation.
CI calls this command on Linux, Windows, and macOS.
Linux CI also runs the native editor acceptance script.

Pin the SDK with roll-forward disabled. Centralize compiler settings and package versions.
Commit package lock files. Keep the SDK deterministic compilation and path mapping settings.
These settings do not establish byte-for-byte reproducibility for the complete build.
Pin GitHub Actions by commit SHA.

Use xUnit v3 through Microsoft Testing Platform, selected in `global.json`.
Use the package-generated runner and built-in TRX reports.
Require one nonempty report for each expected test assembly.

PR #26 removes the two-directory hash comparison, its engineering project,
comparison tests, CI uploads, reproduction script, and fixed test entry points.
Avalonia's XAML compiler records absolute paths after C# compilation.
Satisfying hash equality required custom symbol processing unrelated to editor behavior.
The project owner explicitly chose to remove this check rather than maintain that machinery.
There is no optional reproducibility stage or replacement hash checker.
[Issue #28](https://github.com/daniel-kindl/tangopad/issues/28) no longer blocks the editor refactor.

## Consequences

Restore requires NuGet access. Audit warnings, including source failures, are errors.
All known vulnerability severities block verification.
A clean audit does not prove that a dependency has no defect.
Build warnings remain errors. Tests check observable behavior and module dependencies.

A local build cannot prove native startup on another operating system.
[ADR 0003](0003-native-startup-verification.md) defines the native startup check.
The project makes no claim of reproducible managed binaries, native assets, or installers.
A future release requirement would need a separate decision before such a check returns.

SDK updates require a coordinated `global.json` change.
Package updates require central-version and lock-file changes.
Formatting and compiler analysis share `.editorconfig`.
New external tools need a concrete gap and a documented decision.
