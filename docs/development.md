# Develop Tangopad

## Prepare a host

Install Git and the exact .NET SDK in [global.json](../global.json).
The SDK version is 10.0.402, with roll-forward disabled.
Restore requires network access to NuGet.
Use a desktop session on Windows or macOS.
On Linux, install the [native libraries](native-startup.md) and use X11 or Xvfb.
An XWayland session can supply X11 on a Wayland desktop.
CI uses Ubuntu 24.04, Windows 2025, and macOS 15 runners.
Those test hosts do not establish product minimum OS versions.

Clone the repository and use its root as the working directory.
Run the shared gate:

```sh
dotnet msbuild eng/verify.proj
```

The gate performs locked restore and dependency auditing, formatting and analyzer checks,
Debug and Release builds, all tests, native startup, and TRX report validation.
Build warnings are errors. A missing desktop fails native startup.
Read [CONTRIBUTING.md](../CONTRIBUTING.md) for format fixes and intentional dependency updates.

Start the verified build:

```sh
dotnet run --project src/Editor.App --configuration Release
```

Python 3 is required for documentation checks in hosted CI and engineering scripts.
Run the documentation checker from any platform:

```sh
python3 eng/check-docs.py
```

Linux native workflow and media capture scripts also need xdotool, D-Bus,
GTK desktop portals, and Pillow. The [Verify workflow](../.github/workflows/verify.yml)
lists the installed Ubuntu packages.
[Accessibility checks](ux/47-acceptance.md#reproduce-the-checks) additionally require AT-SPI.

## Find the owning code

| Directory | Ownership |
| --- | --- |
| `src/Editor.Core` | Document rules and filename format identification |
| `src/Editor.Application` | Use cases and boundary contracts |
| `src/Editor.Infrastructure` | Filesystem, state, monitoring, and update adapters |
| `src/Editor.App` | Desktop composition, views, view models, resources, and UI services |
| `src/Editor.Formats.Markdown`, `src/Editor.Formats.Textile` | Format parsers |
| `src/Editor.Preview` | Inactive preview data |
| `tests/` | Behavior, integration, architecture, control, and startup tests |
| `eng/` | Verification, packaging, native acceptance, and capture tools |
| `docs/` | User guide, engineering references, ADRs, and review records |
| `.github/` | Workflows and contribution templates |

[Architecture](architecture.md) defines dependency direction and links the ADRs.
[eng/architecture.json](../eng/architecture.json) is the evaluated dependency policy.
Read the [coding standards](coding-standards.md) before changing code.
Choose placement from the architecture rules and record it in the issue.

## Review and release

Create a short-lived issue branch from `dev`. Target normal pull requests at `dev`.
Use Conventional Commit titles and wait for the required hosted checks and review.
[Integration safeguards](../CONTRIBUTING.md#github-integration-safeguards) define the checks and owner bypass limits.

Release through a reviewed `dev`-to-`main` pull request.
`Directory.Build.props` owns the version. Stable tags use `v<major>.<minor>.<patch>`.
[Release distribution](release-distribution.md) defines native packaging, provenance, authenticated inventories,
optional platform signing, native update evidence, and draft publication.
Packaging requires additional tools on each native host.
A green editor gate does not prove package installation or update recovery.

Keep [eng/release/notes.md](../eng/release/notes.md) consistent with the version being packaged.
Record user-visible changes, breaking changes, platform limits, and known issues from verified work.
Do not invent release dates or imply that an unpublished version shipped.
[Changelog and release notes](release-notes.md) defines the workflow under
[issue #49](https://github.com/daniel-kindl/tangopad/issues/49).
[Product documentation review](product-documentation.md) is a separate pre-release requirement under issue #48.
