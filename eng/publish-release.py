#!/usr/bin/env python3
"""Validate production package sets, then create a draft GitHub release."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

import runpy

_package = runpy.run_path(str(Path(__file__).with_name("package-release.py")))
RUNTIMES, ROOT, run, version = (_package[name] for name in ("RUNTIMES", "ROOT", "run", "version"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--packages", type=Path, required=True)
    parser.add_argument("--target", required=True, help="Reviewed main-branch commit SHA.")
    args = parser.parse_args()
    if len(args.target) != 40 or any(c not in "0123456789abcdef" for c in args.target):
        raise ValueError("The release target must be a full commit SHA.")
    main_sha = subprocess.check_output(["gh", "api", "repos/daniel-kindl/tangopad/git/ref/heads/main", "--jq", ".object.sha"], text=True).strip()
    if args.target != main_sha:
        raise ValueError("The release target must be the current main-branch commit.")
    public_key = ROOT / "eng/release/update-public-key.pem"
    artifacts = []
    for runtime in RUNTIMES:
        manifests = list(args.packages.rglob(f"package.{runtime}.json"))
        if len(manifests) != 1:
            raise ValueError(f"One production package set is required for {runtime}.")
        manifest = json.loads(manifests[0].read_bytes())
        if (manifest.get("version") != version() or manifest.get("runtime") != runtime
                or manifest.get("production") is not True or manifest.get("source_commit") != args.target
                or manifest.get("platform_signing") not in ("none", "configured")):
            raise ValueError("Test packages and mismatched versions cannot be published.")
        directory = manifests[0].parent
        feed_path = directory / f"releases.{runtime}.json"
        run("openssl", "dgst", "-sha256", "-verify", public_key, "-signature", str(feed_path) + ".sig", feed_path)
        assets = json.loads(feed_path.read_bytes())["Assets"]
        if len(assets) != 1:
            raise ValueError("The feed must contain one full package.")
        asset = assets[0]
        if asset["FileName"] != f'TangopadDesktop-{version()}-{runtime}-full.nupkg':
            raise ValueError("The package identity does not match the release.")
        package = directory / asset["FileName"]
        if package.stat().st_size != asset["Size"] or hashlib.sha256(package.read_bytes()).hexdigest().upper() != asset["SHA256"].upper():
            raise ValueError("The package does not match its signed metadata.")
        artifacts.extend(path for path in directory.iterdir() if path.suffix in {".nupkg", ".AppImage", ".exe", ".zip", ".pkg"})
        artifacts.extend([feed_path, Path(str(feed_path) + ".sig")])
    # Keep the release in draft until native update evidence and OS signing checks are reviewed.
    run("gh", "release", "create", "v" + version(), "--repo", "daniel-kindl/tangopad", "--draft",
        "--target", args.target, "--title", "Tangopad " + version(), "--notes-file", ROOT / "eng/release/notes.md", *artifacts)


if __name__ == "__main__":
    main()
