#!/usr/bin/env python3
"""Validate production package sets, then create a draft GitHub release."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import zipfile

import runpy

_package = runpy.run_path(str(Path(__file__).with_name("package-release.py")))
RUNTIMES, ROOT, run, version = (_package[name] for name in ("RUNTIMES", "ROOT", "run", "version"))


def validate_packages(packages, target):
    public_key = ROOT / "eng/release/update-public-key.pem"
    artifacts = []
    for runtime in RUNTIMES:
        manifests = list(packages.rglob(f"package.{runtime}.json"))
        if len(manifests) != 1:
            raise ValueError(f"One production package set is required for {runtime}.")
        run("openssl", "dgst", "-sha256", "-verify", ROOT / "eng/release/update-public-key.pem",
            "-signature", str(manifests[0]) + ".sig", manifests[0])
        manifest = json.loads(manifests[0].read_bytes())
        if (manifest.get("version") != version() or manifest.get("runtime") != runtime
                or manifest.get("production") is not True or manifest.get("source_commit") != target
                or manifest.get("platform_signing") not in ("none", "configured")):
            raise ValueError("Test packages and mismatched versions cannot be published.")
        directory = manifests[0].parent
        expected_files = manifest.get("artifacts", {})
        actual_files = {path.name for path in directory.iterdir() if path.is_file()
                        and path not in (manifests[0], Path(str(manifests[0]) + ".sig"))}
        if not expected_files or set(expected_files) != actual_files:
            raise ValueError("The release artifact inventory is missing or contains unexpected files.")
        for name, digest in expected_files.items():
            path = directory / name
            if Path(name).name != name or path.is_symlink() or hashlib.sha256(path.read_bytes()).hexdigest() != digest:
                raise ValueError("A release artifact does not match its signed SHA-256: " + name)
        evidence = json.loads((directory / "native-evidence/result.json").read_bytes())
        requirements = ("signature_rejected", "partial_download_rejected", "checksum_rejected",
                        "unsaved_cancel_preserved", "save_before_restart", "state_preserved", "native_failure_recovered")
        if (len(evidence) != 2 or {result.get("mode") for result in evidence} != {"--installed", "--portable"}
                or any(result.get("runtime") != runtime or result.get("source_commit") != target
                       or not all(result.get(check) is True for check in requirements) for result in evidence)):
            raise ValueError("Native update evidence is missing, failed, or belongs to another source commit.")
        feed_path = directory / f"releases.{runtime}.json"
        run("openssl", "dgst", "-sha256", "-verify", public_key, "-signature", str(feed_path) + ".sig", feed_path)
        assets = json.loads(feed_path.read_bytes())["Assets"]
        if len(assets) != 1:
            raise ValueError("The feed must contain one full package.")
        asset = assets[0]
        if (asset.get("PackageId") != "TangopadDesktop" or asset.get("Type") != "Full" or asset.get("Version") != version()
                or asset["FileName"] != f'TangopadDesktop-{version()}-{runtime}-full.nupkg'):
            raise ValueError("The package identity does not match the release.")
        package = directory / asset["FileName"]
        if package.stat().st_size != asset["Size"] or hashlib.sha256(package.read_bytes()).hexdigest().upper() != asset["SHA256"].upper():
            raise ValueError("The package does not match its signed metadata.")
        if runtime == "win-x64":
            if any(result.get("replacement_after_backup_recovered") is not True for result in evidence):
                raise ValueError("Windows replacement failure after backup requires recovery evidence in both modes.")
            helper = json.loads((directory / "native-helper.win-x64.json").read_bytes())
            policy = json.loads((ROOT / "eng/velopack/source.json").read_bytes())
            if (any(helper.get(key) != value for key, value in policy.items())
                    or helper.get("patch_sha256") != hashlib.sha256((ROOT / "eng/velopack/windows-recovery.patch").read_bytes()).hexdigest()
                    or any(result.get("native_helper") != helper for result in evidence)):
                raise ValueError("The Windows package and native checks must use the pinned recovery helper.")
            with zipfile.ZipFile(package) as archive:
                helpers = [name for name in archive.namelist() if name.lower().endswith("/squirrel.exe")]
                if len(helpers) != 1 or hashlib.sha256(archive.read(helpers[0])).hexdigest() != helper["binary_sha256"]:
                    raise ValueError("The Windows package does not contain the verified recovery helper.")
        artifacts.extend(path for path in directory.iterdir() if path.is_file())
    return artifacts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--packages", type=Path, required=True)
    parser.add_argument("--validate-only", action="store_true", help="Validate artifacts without creating a release.")
    parser.add_argument("--target", required=True, help="Reviewed main-branch commit SHA.")
    args = parser.parse_args()
    if len(args.target) != 40 or any(c not in "0123456789abcdef" for c in args.target):
        raise ValueError("The release target must be a full commit SHA.")
    main_sha = args.target if args.validate_only else subprocess.check_output(["gh", "api", "repos/daniel-kindl/tangopad/git/ref/heads/main", "--jq", ".object.sha"], text=True).strip()
    if not args.validate_only and args.target != main_sha:
        raise ValueError("The release target must be the current main-branch commit.")
    artifacts = validate_packages(args.packages, args.target)
    if args.validate_only:
        print("Validated four production package sets and their native update evidence.")
        return
    # Keep the release in draft until native update evidence and OS signing checks are reviewed.
    run("gh", "release", "create", "v" + version(), "--repo", "daniel-kindl/tangopad", "--draft",
        "--target", args.target, "--title", "Tangopad " + version(), "--notes-file", ROOT / "eng/release/notes.md", *artifacts)


if __name__ == "__main__":
    main()
