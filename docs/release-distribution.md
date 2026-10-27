# Release distribution and updates

Issue #45 has a candidate implementation. Native package-update evidence is still required before stable publication.
The repository includes a public update key. Its private key remains outside the repository.
Windows and Apple platform certificates are optional. The default release path does not require them.

## Package matrix

| Runtime | Candidate distribution | State outside updated content |
| --- | --- | --- |
| Windows x64 | Per-user Setup and portable ZIP | OS data directory, or package-root `data/` outside `current/` |
| Linux x64 | Self-contained AppImage | OS data directory, or `data/` beside the external AppImage |
| macOS x64 and arm64 | Application bundle and current-user installer | OS data directory, or `data/` beside the bundle |

Flatpak, Snap, system packages, and protected installations do not support application-controlled replacement.
Do not install these packages over an OS-managed application.
The macOS installer permits only the current user's home directory. It installs into `~/Applications`.
A system-wide root-owned bundle cannot use application-controlled replacement without elevation.
The installer does not add an elevation path or start the application as root.
See [Apple's installation-domain reference](https://developer.apple.com/library/archive/documentation/DeveloperTools/Reference/DistributionDefinitionRef/Chapters/Distribution_XML_Ref.html).
Initial package support does not imply support for every OS version or CPU architecture.
The candidate workflow builds each package on its native operating system.

Before creating a draft release, the publisher requires one nonempty distribution
file for each format in the table. It selects Linux `*.AppImage`, Windows `*.zip`
and `*-Setup.exe`, and macOS `*.zip` and `*-Setup.pkg` from the signed inventory.
A missing file, an empty file, or multiple matches fails validation, even when
the inventory signature is valid. The existing checks also require a full update
package, matching signed hashes, and native update evidence for each runtime.
Filename and size checks do not establish installer validity. Native package
acceptance remains required. [Issue #115](https://github.com/daniel-kindl/tangopad/issues/115)
records the omission finding and its regression coverage under audit #52.

Version comes from `Directory.Build.props`. Stable publication uses `v<major>.<minor>.<patch>` tags.
[Changelog and release notes](release-notes.md) defines the canonical history, notes template, and human review procedure.
Update feeds use one runtime channel per row, such as `linux-x64`.
These channels contain stable releases only. There is no automatic installation or forced restart.
The initial check is manual. Automatic checks remain deferred.

## Build candidates

Install the pinned .NET SDK, Python 3, and OpenSSL.
Linux also requires `squashfs-tools`.
The packaging script restores the repository's pinned `vpk` tool.
Windows also requires Rustup and the MSVC toolchain. The script builds a pinned native helper with the recovery patch in `eng/velopack/`.
The isolated packaging tool uses that helper. It does not modify the user's tool cache.
Run on the target OS:

```sh
python eng/package-release.py --rid linux-x64
```

Use `win-x64`, `osx-x64`, or `osx-arm64` on their corresponding hosts.
The command creates a temporary test key, embeds its public key, and signs the test feed.
It deletes the private key when packaging ends.
Candidate packages are development artifacts. They are not production installers.
Candidate packaging validates the current draft notes. A local `--test-version` override does not promote those notes to a public release.
Their temporary key cannot authenticate production update feeds.
Outputs are in `artifacts/release/<runtime>/<version>/`.
`package.<runtime>.json` marks test output with `production: false`.

`--test-version 0.1.1` permits a local N-to-N+1 packaging test.
Production mode rejects this version override.
Package inputs reject portable data, state files, legacy JSON, portable markers, and symbolic links.
Native libraries and license notices remain in the package.
Packaging checks [distribution notices](distribution-notices.md) in the publish
directory and final portable and full update artifacts before signing the feed.
The package includes source records and notices from its selected .NET runtime pack.
The script isolates runtime publish lock files from the repository's ordinary build lock files.
The SDK, package versions, tool version, and inputs are pinned. Binary byte equality remains outside the verification policy.

The `Package candidates` workflow verifies the repository and builds all candidate runtimes.
It has read-only repository permissions, receives no production credentials, and uploads expiring CI artifacts.
It never publishes a GitHub Release.

## Configure production identities

Create the free ECDSA P-256 update-signing key once:

```sh
python eng/initialize-update-key.py
```

The command is idempotent. It refuses to replace an existing trust root with a different key.
It writes the public key to `eng/release/update-public-key.pem`.
It keeps the private key in the user's local data directory, outside the repository.
On Unix, the private key uses owner-only file permissions.
The initial implementation generated this key locally. Do not generate a different key for each release.
Protect its backup and access through the selected signing system.
A compromised key requires a separate recovery and key-rotation plan before publication resumes.
An app with no embedded public key disables in-app checks.

Local production packaging finds the initialized private key automatically.
Use `TANGOPAD_UPDATE_PRIVATE_KEY` to select another protected path.
The optional certificate path uses these environment values:

| Variable | Meaning |
| --- | --- |
| `TANGOPAD_UPDATE_PRIVATE_KEY` | Path to the protected PEM signing key |
| `TANGOPAD_WINDOWS_SIGN_PARAMS` | Signing parameters for the configured Windows certificate or signing provider |
| `TANGOPAD_MAC_APP_IDENTITY` | Installed Developer ID Application identity |
| `TANGOPAD_MAC_INSTALL_IDENTITY` | Installed Developer ID Installer identity |
| `TANGOPAD_MAC_NOTARY_PROFILE` | Configured `notarytool` credential profile |

With `--platform-signing configured`, Windows and macOS credentials must already be configured on the packaging host.
The default `--platform-signing none` uses no Windows or Apple certificate and does not notarize macOS packages.
Users may receive SmartScreen or Gatekeeper prompts. macOS launch and relaunch acceptance remain subject to OS policy.
See [Apple's launch policy](https://support.apple.com/102445).
The updater does not disable or bypass these checks.
The script does not buy a certificate, create an identity, or bypass OS trust checks.
Review [Velopack signing](https://docs.velopack.io/packaging/signing) for provider setup.
Certificate signing and notarization are optional future setup. They remain untested.

Prepare versioned changelog entries and reviewed notes through the [release notes workflow](release-notes.md).
Production packaging rejects Draft notes. It records the accepted notes hash in each signed package inventory.

Run the repository verification before production packaging:

```sh
dotnet msbuild eng/verify.proj
python eng/package-release.py --rid linux-x64 --production
```

Repeat on the other target hosts with the same source commit, version, update key, and release notes.
If platform signing is configured, check Windows Authenticode signatures and macOS signatures and notarization on the resulting packages.
Verify those platform identities against the chosen release identity.
For packages without platform certificates, record the actual OS prompts and launch results.
The update-feed signature does not replace these OS checks.

## Publish a reviewed release

Complete the acceptance checks below before publication.
Collect production output from all four runtimes in one directory.
Use the reviewed current `main` commit as the target:

```sh
python eng/publish-release.py --packages artifacts/production --target <full-main-commit-sha>
```

The command verifies the signed artifact inventory, production markers, versions, feed signatures, package sizes, and SHA-256 hashes.
It also verifies the packaged notes hash and publication-ready changelog and notes.
The local checkout must match the target commit.
It writes `artifacts/release-notes.md` with versioned download links and checksums from the verified upload files.
The inventory authenticates installers and portable archives as well as update packages.
The command also requires successful native evidence from the target commit in both storage modes.
Windows evidence must include recovery after backup. The full package must contain the same patched helper used by those checks.
Use `--validate-only` to check all four package sets without creating a draft.
Run `python eng/check-release-validation.py` to check rejection of tampered installers, unsigned manifests, stale evidence, missing runtimes, and unpatched helpers.
It rejects test output and a target that differs from current remote `main`.
It creates a draft GitHub Release. It does not activate the release.
The `Prepare draft release` workflow performs this sequence from `main`.
The `release` environment now contains `UPDATE_SIGNING_KEY` for the committed public key.
Its deployment policy permits only the `main` branch. The environment configuration was checked on 2026-10-24.
Repository administrators can change this remote configuration.
Do not put the private key in a repository variable, source file, issue, or log.
The workflow exposes the secret only to the packaging step and removes the temporary key after packaging.
Only the draft job has repository write permission.
Review every attached package and the recorded platform evidence before publishing the draft as stable.
A published stable release becomes the GitHub `latest` feed.
Never replace an asset under an existing release version.

## Update behavior and trust

The Updates panel shows the installed version, check result, available version, download bytes, release notes, progress, and errors.
`Update and restart` downloads a full package and verifies its authenticated size and SHA-256.
Before apply, the editor uses its existing Save, Discard, or Cancel decision.
Cancel leaves the current app running. A downloaded package cannot apply automatically on a later startup.
The editor disables document editing during the final state flush and apply.
A state-write failure prevents restart.
Explicit storage-mode arguments survive restart. The portable marker remains outside replaced content.

The adapter accepts one signed stable feed from the fixed Tangopad GitHub repository.
It validates application ID, runtime, filename, package type, size, version, and SHA-256.
It rejects invalid signatures, prereleases, build metadata, and downgrades.
It permits HTTPS redirects only to GitHub and its specified release-asset hosts.
It checks the staged package again before invoking the native updater.
Offline checks, missing releases, and rejected downloads leave editing available and permit retry.

The application refuses apply when another same-named process is detected.
Startup and apply also share a mutex and a pending-update marker outside replaced content.
New editors wait while the initiating process or native helper is active.
A stale marker clears after those processes exit, which permits recovery after a crash.
On Unix, an owner that exits during the process identity query can produce `ESRCH`.
The startup gate treats that result as an exited owner. Permission errors and other native query failures still stop startup.
The marker records the target version. If the previous version restarts after a failed replacement, the editor displays a recovery message.
Native apply runs silently. The restarted editor displays the failure instead of leaving a native error dialog open.
Concurrent-start behavior still needs native Windows and macOS acceptance.
Native replacement uses Velopack. Windows packaging includes a source-pinned recovery patch.
Upstream 1.2.161 deletes the backup after a failed replacement. The patch restores that backup before restarting the previous editor.
If restoration fails, the helper preserves the backup. Three Rust filesystem tests check recovery and backup retention.
`eng/velopack/source.json` pins the source archive, toolchain, and target. Each Windows package records the helper hash and patch hash.
An application startup smoke test does not prove rollback or update safety.

## Required native evidence

Record the source commit, runtime, OS version, package signatures, test versions, result, and evidence location for each check.
Run both installed and portable storage modes where the format supports them.

1. Install N, change preferences, and record recent files and exact document bytes.
2. Update to N+1 through the app. Require one-click download, apply, and restart.
3. Verify the running package version and preserved preferences, recent files, documents, `data/`, and marker.
4. Repeat with unsaved edits. Save must finish first. Cancel must keep N running.
5. Interrupt download. Retry must succeed without a partial package being applied.
6. Reject a tampered feed, signature, or package. Preserve N and its state.
7. Force native replacement failure. Require the previous working version and a usable recovery path.
8. Check protected paths and package-managed environments. Require no application overwrite or silent elevation.
9. Check concurrent instances and starts during apply. Require no lost unsaved edits.

Run the native editor checks on each target host:

```sh
python eng/check-native-update.py --rid linux-x64
```

Use the other runtime names on their corresponding hosted runners.
Windows and macOS installer checks require a fresh GitHub-hosted account.
The probe refuses existing state and portable files. It uses the separate `TangopadUpdateProbe` package identity and a temporary feed key.
Installed-mode probe state uses the corresponding `TangopadUpdateProbe` directory beside the production `Tangopad` directory.
Both paths use the same OS data location. Portable probe state uses the actual distribution-root `data/` directory.
The production public key and fixed GitHub source remain unchanged.
`eng/Editor.UpdateProbe/` composes the actual editor, commands, dialogs, storage, and update adapter.
It supplies a local HTTP transport through an internal constructor. Production packages do not include the probe.
Both workflows run this check for all four runtimes and upload JSON reports, screenshots, and process logs.
The draft workflow requires the native checks before it collects production artifacts.

The probe checks signature rejection, partial downloads, checksum rejection, unsaved Cancel, no automatic startup apply,
native rejection and recovery, Save before restart, the running version, and exact state and document preservation.
A second editor retains its exact unsaved text when it prevents apply.
Unix portable checks also reject a read-only distribution directory before native apply.
On Windows it also blocks a staged directory to force replacement failure after backup, then retries.
Evidence is in `artifacts/native-update/<runtime>/`.
Use `--recovery-attempts 5` to repeat native failure recovery five times in each storage mode.
The candidate workflow exposes the same `recovery_attempts` input for manual runs and defaults to five attempts.
The probe records startup checkpoints. Failed runs retain process state, pending markers, and available macOS launch and crash logs.
The production workflow requires at least five successful native recovery attempts in each storage mode.
The production validator rejects evidence with fewer attempts.
These local feeds do not prove the published GitHub release path or downloaded-file OS trust prompts.
Optional platform signing, notarization, protected Windows installations, and starts during native apply still require acceptance evidence.
Issue #45 must remain open until these checks and production release validation pass.
See [ADR 0011](adr/0011-self-managed-desktop-updates.md) for ownership and alternatives.
