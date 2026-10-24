#!/usr/bin/env python3
"""Check release rejection rules with independently signed artifact fixtures."""
import hashlib
import json
from pathlib import Path
import runpy
import shutil
import subprocess
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parent.parent
VALIDATOR = runpy.run_path(str(ROOT / "eng/publish-release.py"))


class ReleaseValidationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="tangopad-release-validation-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "eng/release").mkdir(parents=True)
        shutil.copytree(ROOT / "eng/velopack", self.root / "eng/velopack")
        self.private = self.root / "private.pem"
        self.public = self.root / "eng/release/update-public-key.pem"
        self.execute("openssl", "genpkey", "-algorithm", "EC", "-pkeyopt", "ec_paramgen_curve:P-256", "-out", self.private)
        self.execute("openssl", "pkey", "-in", self.private, "-pubout", "-out", self.public)
        self.validate = VALIDATOR["validate_packages"]
        self.previous_root = self.validate.__globals__["ROOT"]
        self.validate.__globals__["ROOT"] = self.root
        self.addCleanup(self.validate.__globals__.__setitem__, "ROOT", self.previous_root)
        self.commit = "a" * 40
        self.version = VALIDATOR["version"]()
        self.packages = self.root / "packages"
        self.helper_bytes = b"independent fixture native helper"
        for runtime in ("win-x64", "linux-x64", "osx-x64", "osx-arm64"):
            directory = self.packages / runtime
            directory.mkdir(parents=True)
            package = directory / f"TangopadDesktop-{self.version}-{runtime}-full.nupkg"
            with zipfile.ZipFile(package, "w") as archive:
                archive.writestr("lib/net45/Squirrel.exe", self.helper_bytes)
                archive.writestr("fixture.txt", "native package fixture")
            (directory / f"installer-{runtime}.zip").write_bytes(b"independent installer fixture")
            feed = directory / f"releases.{runtime}.json"
            feed.write_text(json.dumps({"Assets": [{"PackageId": "TangopadDesktop", "Version": self.version,
                "Type": "Full", "FileName": package.name, "Size": package.stat().st_size,
                "SHA256": hashlib.sha256(package.read_bytes()).hexdigest().upper()}]}), encoding="utf-8")
            self.sign(feed)
            helper = None
            if runtime == "win-x64":
                helper = json.loads((self.root / "eng/velopack/source.json").read_bytes())
                helper.update(binary_sha256=hashlib.sha256(self.helper_bytes).hexdigest(),
                              patch_sha256=hashlib.sha256((self.root / "eng/velopack/windows-recovery.patch").read_bytes()).hexdigest())
                (directory / "native-helper.win-x64.json").write_text(json.dumps(helper), encoding="utf-8")
            (directory / "native-evidence").mkdir()
            results = [{"runtime": runtime, "mode": mode, "source_commit": self.commit,
                        "signature_rejected": True, "partial_download_rejected": True, "checksum_rejected": True,
                        "unsaved_cancel_preserved": True, "save_before_restart": True, "state_preserved": True,
                        "native_failure_recovered": True, "native_helper": helper, "replacement_after_backup_recovered": runtime == "win-x64"}
                       for mode in ("--installed", "--portable")]
            (directory / "native-evidence/result.json").write_text(json.dumps(results), encoding="utf-8")
            self.write_manifest(runtime)

    @staticmethod
    def execute(*arguments):
        subprocess.run([str(argument) for argument in arguments], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def sign(self, path):
        self.execute("openssl", "dgst", "-sha256", "-sign", self.private, "-out", str(path) + ".sig", path)

    def write_manifest(self, runtime):
        directory = self.packages / runtime
        manifest = directory / f"package.{runtime}.json"
        digests = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in directory.iterdir()
                   if path.is_file() and path not in (manifest, Path(str(manifest) + ".sig"))}
        manifest.write_text(json.dumps({"version": self.version, "runtime": runtime, "production": True,
            "source_commit": self.commit, "platform_signing": "none", "artifacts": digests}), encoding="utf-8")
        self.sign(manifest)

    def test_complete_sets_validate(self):
        artifacts = self.validate(self.packages, self.commit)
        self.assertEqual(len(artifacts), 25)
        self.assertIn(self.packages / "win-x64/native-helper.win-x64.json", artifacts)

    def test_tampered_installer_is_rejected(self):
        (self.packages / "linux-x64/installer-linux-x64.zip").write_bytes(b"tampered installer")
        with self.assertRaisesRegex(ValueError, "signed SHA-256"):
            self.validate(self.packages, self.commit)

    def test_unsigned_production_marker_is_rejected(self):
        manifest = self.packages / "win-x64/package.win-x64.json"
        manifest.write_bytes(manifest.read_bytes() + b" ")
        with self.assertRaises(subprocess.CalledProcessError):
            self.validate(self.packages, self.commit)

    def test_evidence_from_another_commit_is_rejected(self):
        path = self.packages / "win-x64/native-evidence/result.json"
        results = json.loads(path.read_bytes())
        results[0]["source_commit"] = "b" * 40
        path.write_text(json.dumps(results), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "another source commit"):
            self.validate(self.packages, self.commit)

    def test_unpatched_packaged_helper_is_rejected(self):
        path = self.packages / f"win-x64/TangopadDesktop-{self.version}-win-x64-full.nupkg"
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("lib/net45/Squirrel.exe", b"unpatched helper")
        feed = self.packages / "win-x64/releases.win-x64.json"
        metadata = json.loads(feed.read_bytes())
        metadata["Assets"][0].update(Size=path.stat().st_size, SHA256=hashlib.sha256(path.read_bytes()).hexdigest().upper())
        feed.write_text(json.dumps(metadata), encoding="utf-8")
        self.sign(feed)
        self.write_manifest("win-x64")
        with self.assertRaisesRegex(ValueError, "verified recovery helper"):
            self.validate(self.packages, self.commit)

    def test_missing_runtime_is_rejected(self):
        shutil.rmtree(self.packages / "osx-arm64")
        with self.assertRaisesRegex(ValueError, "required for osx-arm64"):
            self.validate(self.packages, self.commit)


if __name__ == "__main__":
    unittest.main()
