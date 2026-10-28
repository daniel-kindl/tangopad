# Install, update, or remove Tangopad

## Choose a build

The first stable release, 1.0.0, is not yet published.
Version 1.0.0 targets Windows x64, Linux x64, macOS x64 (Intel), and macOS arm64 (Apple silicon).
There is no package for other platforms or CPU architectures.
The formats below are candidate formats.
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

### Open an unsigned package

The 1.0.0 packages are not signed with a Windows or Apple certificate.
The macOS packages are not notarized.
An update-feed signature authenticates update content, but does not replace platform signing.
Tangopad does not bypass OS protections.
Download packages only from [GitHub Releases](https://github.com/daniel-kindl/tangopad/releases).
Compare the SHA-256 hash with the value in the release notes before you open a package.

On Windows, Microsoft Defender SmartScreen shows a warning for a downloaded file that has no established reputation.
The unsigned Tangopad Setup file has no such reputation, so expect this warning.
Read the [SmartScreen overview](https://learn.microsoft.com/windows/security/operating-system-security/virus-and-threat-protection/microsoft-defender-smartscreen/) for how SmartScreen evaluates files.
This guide does not give steps to continue past the warning.

On macOS, Gatekeeper blocks the first open of Tangopad because the app is not registered with Apple by a known developer.
To open the app after you confirm the download, do these steps from
[Apple's instructions](https://support.apple.com/guide/mac-help/open-a-mac-app-from-an-unknown-developer-mh40616/mac):

1. Try to open Tangopad. macOS shows a warning and does not open the app.
2. Choose **Apple menu > System Settings**, then click **Privacy & Security** in the sidebar.
3. Go to **Security**, then click **Open**.
4. Click **Open Anyway**. This button is available for about an hour after you try to open the app.
5. Enter your login password, then click **OK**.

macOS saves the app as an exception. Later opens do not show the warning.
If the OS blocks launch in a different way, report the package, OS, and exact message.

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
