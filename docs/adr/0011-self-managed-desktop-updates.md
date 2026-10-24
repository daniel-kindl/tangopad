# ADR 0011: Authenticated updates for self-managed desktop packages

Status: Proposed. Production activation requires the platform evidence in [release distribution](../release-distribution.md).

Date: 2026-10-24

## Context

Issue #45 requires one-click updates without losing document edits or application state.
Existing state uses OS data directories or an explicitly selected portable distribution.
The repository had no package or publication workflow before this change.

## Decision

Use Velopack 1.2.161 for native package replacement and restart.
Pin the SDK library and packaging tool to the same version.
The .NET 10 library declares no package dependencies. Its license is MIT.
Retain its copyright and license notice in application distributions.
The packaging tool has a larger dependency graph. It is a build tool, not an application dependency.

Use self-contained Windows x64 per-user installers and portable archives, Linux x64 AppImages,
and macOS x64 and arm64 bundles and installers. These are the initial candidate matrix.
Native package tests must establish the supported OS versions before publication.
Do not overwrite Flatpak, Snap, or OS package-manager installations.
Do not add an elevation path. A protected distribution must fail before native apply starts.

Application owns `IApplicationUpdater` and `UpdateRelease` in `Updates/`.
Infrastructure owns update operations through Velopack, HTTP, signatures, staging, and package paths in `Updates/`.
App calls `VelopackApp.Build().SetAutoApplyOnStartup(false).Run()` directly at the start of `Program.Main`.
Windows packaging inspects this call in the entry assembly. The composition root therefore references the same pinned Velopack SDK.
App owns the update ViewModel, shared text resources, and the update panel.
`eng/architecture.json` permits Velopack in App and Infrastructure.
Core retains its existing rules and dependencies.

Authenticate exact feed bytes with an embedded ECDSA P-256 public key and SHA-256.
Accept one full stable package with the expected application ID, runtime, filename, size, and hash.
Fetch metadata and packages only through the fixed GitHub repository and constrained HTTPS redirect hosts.
Use version-specific package URLs after authentication.
Reject prereleases, build metadata, downgrades, and unsigned feeds.
Verify the staged package again immediately before native apply.
OS signing remains separate from the update-feed signature.
The owner chose a release path without paid platform certificates.
Keep feed signing with a free locally generated P-256 key.
Respect SmartScreen and Gatekeeper prompts. Do not claim unattended launch where the OS requires consent.

Disable automatic apply on startup. A cancelled restart must not apply later.
Require the existing Save, Discard, or Cancel decision before restart.
Disable editing while state flushes and apply starts. A failed state flush prevents apply.
Retain explicit `--portable` and `--installed` switches on restart.
A second detected Tangopad process prevents apply because the native updater can terminate other processes.
This process check is conservative. Startup and apply share a mutex and pending-update marker.
A new editor waits until the initiating process and native helper stop.
Stale markers permit recovery after a crash. Native concurrency acceptance remains required.

Keep Windows portable state in the package root, outside `current/`.
Use the AppImage's external directory on Linux and the bundle's external directory on macOS.
Use `TangopadDesktop` as the package ID so a Windows uninstall cannot delete the installed-mode `Tangopad` state directory.
Exclude all state files and portable markers from package inputs.

## Alternatives

A custom replacement helper would require new native process, locking, rollback, and signing code on every platform.
A browser download would fail the one-click requirement.
OS package managers remain responsible for managed installations.

## Evidence and limits

A local Velopack Linux x64 probe updated an AppImage from 0.1.0 to 0.2.0 and restarted on 0.2.0.
The prototype used a local feed and the native updater. It did not prove Windows or macOS behavior.
A self-contained Tangopad AppImage and signed test feed built locally.
These checks do not establish production signing, notarization, rollback, or package-level application-data preservation.
Stable publication remains deferred until these requirements have evidence.
The publication workflow creates a draft for review.

The repository has no configured release secrets or variables at this change's start.
A free update-signing key was generated locally after the owner requested a path without platform certificates.
Only its public half is in the repository. Its private half remains in the user's local data directory.
The draft-release workflow needs that private key in its `UPDATE_SIGNING_KEY` secret.
Builds without a public key show that in-app updates are unavailable.

## Sources

- [Velopack integration and package replacement](https://docs.velopack.io/integrating/overview)
- [Preserved files](https://docs.velopack.io/integrating/preserved-files)
- [Linux packaging](https://docs.velopack.io/packaging/operating-systems/linux)
- [Signing and notarization](https://docs.velopack.io/packaging/signing)
- [Pinned library source](https://github.com/velopack/velopack/tree/92d6a1c91716729d449034df5c50307dcce39493)
