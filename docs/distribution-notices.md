# Distribution notices

Tangopad packages include license and attribution files in the application's
`licenses/` directory. [The source inventory](../eng/notices.json) records exact
package versions, source locations, and SHA-256 hashes for the files in
[docs/licenses](licenses/). Legal text is copied without rewriting it.
The Git attributes disable text conversion for these files to preserve their bytes.

## Coverage

The inventory accounts for all 29 NuGet packages in the application lock file.
It records 14 component and application notice files. Avalonia.BuildServices has
an explicit build-only exclusion. The graph includes dependencies for multiple
platforms. It does not claim that every package is present in each platform payload.
Lucide attribution and the Tangopad license are recorded separately from NuGet.

Source records identify pinned upstream revisions or restored NuGet package files.
The four previously distributed notices retain their existing text and repository
provenance. Their source records do not represent a new upstream license review.

During self-contained publishing, the packaging script reads the selected
`Microsoft.NETCore.App.Runtime.<rid>` version from the publish assets. It copies
that runtime pack's license and third-party notices without changing their bytes.
It writes `licenses/distribution.json` with the runtime identity and file hashes.
The result contains 18 files, including both inventories.

## Check a change

1. Review each changed dependency and its upstream license and attribution files.
2. Copy the required text into `docs/licenses/` without editing it.
3. Update the exact package versions, source records, and file hashes in `eng/notices.json`.
4. Record a reason for each dependency that does not require a distributed notice.
5. Run these checks from the repository root:

```sh
python3 eng/test-check-notices.py
python3 eng/check-notices.py
dotnet msbuild eng/verify.proj
```

The notice check rejects uncovered dependencies, unreviewed versions, changed
source text, and missing files. It uses local files and requires no network access.
It does not determine license compatibility or replace review of upstream terms.

Build a candidate on each target operating system with the command in
[release distribution](release-distribution.md). Packaging checks the publish
directory and final portable and full update artifacts before signing the feed.
On Linux, it extracts the AppImage and checks its notice bytes. It also requires
the full update package to contain the same AppImage bytes. On Windows and macOS,
it reads the notice files from the portable ZIP and full update archive.
Installer execution and installed-file inspection remain native acceptance work.

To check an extracted application directory that contains `licenses/`, run:

```sh
python3 eng/check-notices.py --payload /path/to/application --rid linux-x64
```

Use the matching runtime identifier for another platform.

## Audit finding and verification

[Issue #113](https://github.com/daniel-kindl/tangopad/issues/113) records a bounded
finding from [audit issue #52](https://github.com/daniel-kindl/tangopad/issues/52).
The earlier Linux candidate contained four component notices. It lacked the
additional dependency and .NET runtime files now included by the packaging script.
The new check rejected that earlier payload because `Avalonia.txt` was missing.

A rebuilt Linux development candidate passed the publish-directory and extracted
AppImage checks. Its full update package contained the same AppImage bytes.
The selected .NET runtime pack was version `10.0.13`. This local candidate used
development signing material and is not a production release.

Windows and macOS native package checks remain untested for this change.
Archive fixtures test the macOS directory shape; they do not establish native
packaging or installer behavior. Independent license review and the remaining
security, performance, accessibility, and platform work in issue #52 remain open.
