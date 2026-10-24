#!/usr/bin/env python3
"""Build the pinned Windows native updater with backup-preserving recovery."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import urllib.request

ROOT = Path(__file__).resolve().parent.parent


def run(*arguments, **kwargs):
    return subprocess.run([str(argument) for argument in arguments], check=True, **kwargs)


def build(output):
    if os.name != "nt":
        raise RuntimeError("Build the Windows updater on Windows with the MSVC toolchain.")
    policy = json.loads((ROOT / "eng/velopack/source.json").read_bytes())
    patch = ROOT / "eng/velopack/windows-recovery.patch"
    output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="tg-vpk-", dir=os.environ.get("RUNNER_TEMP")) as temporary:
        temporary = Path(temporary)
        archive = temporary / "source.tar.gz"
        url = f'https://codeload.github.com/velopack/velopack/tar.gz/{policy["commit"]}'
        with urllib.request.urlopen(url, timeout=60) as response, archive.open("wb") as target:
            shutil.copyfileobj(response, target)
        if hashlib.sha256(archive.read_bytes()).hexdigest() != policy["archive_sha256"]:
            raise RuntimeError("The native updater source archive does not match its pinned SHA-256.")
        with tarfile.open(archive) as source:
            source.extractall(temporary, filter="data")
        source = temporary / ("velopack-" + policy["commit"])
        run("git", "apply", "--check", patch, cwd=source)
        run("git", "apply", patch, cwd=source)
        for name in ("Cargo.toml", "Cargo.lock"):
            path = source / name
            path.write_text(path.read_text(encoding="utf-8").replace("0.0.0-local", policy["version"]), encoding="utf-8")
        toolchain = policy["rust_toolchain"]
        run("rustup", "toolchain", "install", toolchain, "--profile", "minimal")
        environment = dict(os.environ, RUSTFLAGS="-C target-feature=+crt-static")
        run("cargo", "+" + toolchain, "test", "--locked", "--package", "velopack_bins", "--lib", "--features", "windows",
            "--target", policy["target"], "tangopad_recovery_tests", cwd=source, env=environment)
        run("cargo", "+" + toolchain, "build", "--locked", "--release", "--package", "velopack_bins", "--bin", "update",
            "--features", "windows", "--target", policy["target"], cwd=source, env=environment)
        binary = source / "target" / policy["target"] / "release/update.exe"
        destination = output / "update_x64.exe"
        shutil.copyfile(binary, destination)
        provenance = dict(policy, patch_sha256=hashlib.sha256(patch.read_bytes()).hexdigest(),
                          binary_sha256=hashlib.sha256(destination.read_bytes()).hexdigest())
        (output / "native-helper.win-x64.json").write_text(json.dumps(provenance, indent=2) + "\n", encoding="utf-8")
    return destination


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/native-helper/win-x64")
    print(build(parser.parse_args().output))
