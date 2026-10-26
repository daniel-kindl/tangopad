# Install, update, or remove Tangopad

## Choose a build

The first stable release is not yet published. The formats below are candidate formats.
Their existence in CI does not establish production installation or OS trust acceptance.
Retain backups when testing candidates.

Use [GitHub Releases](https://github.com/daniel-kindl/tangopad/releases) for public artifacts when available.
Read the version's release notes before downloading.
The [release distribution guide](../release-distribution.md) defines artifact validation and signing.
There is no verified package-manager install command.

| Platform and CPU | Candidate package | Installation model |
| --- | --- | --- |
| Windows x64 | Per-user Setup or portable ZIP | Setup installs for your account. Extract the complete ZIP before running its executable. |
| Linux x64 | AppImage | Keep the file in a writable directory that you control. Mark it executable through file properties, then launch it. |
| macOS Intel x64 | Application bundle or current-user installer | The installer uses `~/Applications`. Launch Tangopad from that directory. |
| macOS Apple silicon arm64 | Application bundle or current-user installer | Choose arm64. The same current-user installation policy applies. |

Self-contained packages include the .NET runtime. Source builds require the pinned SDK.
Linux uses X11, or XWayland in a Wayland session.
See [native startup](../native-startup.md) for host libraries.
AppImage host and FUSE requirements need validation on the selected release's supported distributions.
This table establishes no minimum OS version or support for other CPU architectures.

Windows and Apple certificate signing and macOS notarization are optional in the current pipeline.
Unsigned packages can cause SmartScreen or Gatekeeper prompts.
An update-feed signature authenticates update content, but does not replace platform signing.
Tangopad does not bypass OS protections.
If the OS blocks launch, report the package, OS, and exact message.

## Start from source

Use the [developer setup](../development.md) when no suitable published package exists.
After verification, start the editor from the repository root:

```sh
dotnet run --project src/Editor.App --configuration Release
```

An Untitled document appears. Follow the [first editing session](first-file.md).
Source execution is a development workflow, not a production installer.

## Select installed or portable storage

Installed mode is the default, including for a single-file executable.
It keeps preferences and recent paths in your OS user-data directory.
Portable storage is opt-in. Put an empty `tangopad.portable` file beside the distributed executable,
or launch that executable with `--portable`.
On macOS, put the marker beside `Tangopad.app`, outside the bundle.

Portable state lives in a sibling `data/` directory.
On Windows, keep the marker and data at the distribution root, outside `current/`.
`--installed` overrides the marker. Do not supply both switches.
Changing modes selects another state file. It does not transfer preferences or recent paths.
See [state locations and backup](../app-state.md#locations-and-mode-selection).

## Check for an update

This procedure applies to supported self-managed packages with an embedded update key.
It does not authorize replacement of Flatpak, Snap, system packages, or protected installations.

1. Open **Help > Check for Updates**.
2. Read the installed version, result, and available release notes.
3. If an update is available, select **Update and restart**.
4. If prompted about unsaved text, select **Save** or **Cancel**.
5. After restart, open **Help > About Tangopad** to check the version.

Cancel keeps the current editor running. Discard explicitly abandons unsaved edits.
There are no automatic checks, silent installations, or forced restarts.
Offline checks, absent releases, and rejected downloads leave editing available.
Retry after resolving the reported error.
The menu can be absent in builds without update support.
Native package and public-release validation remain release requirements.

## Remove the application

1. Save your documents.
2. Close every Tangopad process.
3. For Windows Setup, use the OS installed-apps removal action if the package provides it.
4. For a ZIP or AppImage, remove the extracted application directory or AppImage file.
5. For macOS, remove the Tangopad application bundle from your Applications directory.

Keep portable `data/` and `tangopad.portable` if you plan to install again.
Documents stay in the locations you selected when saving.
Keep per-user state unless you deliberately want to remove preferences and history.
The [state guide](../app-state.md#writes-migration-and-recovery) explains backup and reset,
including retained legacy JSON files. Package-specific uninstall results still need release-candidate validation.
