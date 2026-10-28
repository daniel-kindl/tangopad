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
        (self.root / "eng/release/notes.md").write_bytes(b"Reviewed fixture release notes.\n")
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
            if runtime == "linux-x64":
                distributions = [f"TangopadDesktop-{runtime}.AppImage"]
            else:
                installer_extension = "exe" if runtime == "win-x64" else "pkg"
                distributions = [f"TangopadDesktop-{runtime}-Portable.zip",
                                 f"TangopadDesktop-{runtime}-Setup.{installer_extension}"]
            for name in distributions:
                (directory / name).write_bytes(b"independent distribution fixture")
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
            results = [{"runtime": runtime, "mode": mode, "source_commit": self.commit, "source_dirty": False,
                        "signature_rejected": True, "partial_download_rejected": True, "checksum_rejected": True,
                        "unsaved_cancel_preserved": True, "save_before_restart": True, "state_preserved": True,
                        "native_failure_recovered": True, "native_recovery_attempts": 5, "other_window_preserved": True, "protected_directory_rejected": runtime != "win-x64", "protected_bundle_rejected": runtime.startswith("osx-"), "native_helper": helper, "replacement_after_backup_recovered": runtime == "win-x64"}
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
            "source_commit": self.commit, "platform_signing": "none", "artifacts": digests,
            "release_notes_sha256": hashlib.sha256((self.root / "eng/release/notes.md").read_bytes()).hexdigest()}), encoding="utf-8")
        self.sign(manifest)

    def test_complete_sets_validate(self):
        artifacts = self.validate(self.packages, self.commit)
        self.assertEqual(len(artifacts), 28)
        self.assertIn(self.packages / "win-x64/native-helper.win-x64.json", artifacts)

    def distribution_files(self):
        return [path for path in self.packages.rglob("*")
                if path.suffix in (".AppImage", ".zip", ".exe", ".pkg")]

    def test_missing_distributions_are_rejected_even_with_signed_inventory(self):
        for path in self.distribution_files():
            with self.subTest(artifact=path.name):
                original = path.read_bytes()
                path.unlink()
                self.write_manifest(path.parent.name)
                try:
                    with self.assertRaisesRegex(ValueError, "distribution artifact"):
                        self.validate(self.packages, self.commit)
                finally:
                    path.write_bytes(original)
                    self.write_manifest(path.parent.name)

    def test_duplicate_distributions_are_rejected_even_with_signed_inventory(self):
        for path in self.distribution_files():
            with self.subTest(artifact=path.name):
                duplicate = path.with_name("duplicate-" + path.name)
                duplicate.write_bytes(path.read_bytes())
                self.write_manifest(path.parent.name)
                try:
                    with self.assertRaisesRegex(ValueError, "distribution artifact"):
                        self.validate(self.packages, self.commit)
                finally:
                    duplicate.unlink()
                    self.write_manifest(path.parent.name)

    def test_empty_distributions_are_rejected_even_with_signed_inventory(self):
        for path in self.distribution_files():
            with self.subTest(artifact=path.name):
                original = path.read_bytes()
                path.write_bytes(b"")
                self.write_manifest(path.parent.name)
                try:
                    with self.assertRaisesRegex(ValueError, "distribution artifact"):
                        self.validate(self.packages, self.commit)
                finally:
                    path.write_bytes(original)
                    self.write_manifest(path.parent.name)

    def test_tampered_installer_is_rejected(self):
        (self.packages / "linux-x64/TangopadDesktop-linux-x64.AppImage").write_bytes(b"tampered installer")
        with self.assertRaisesRegex(ValueError, "signed SHA-256"):
            self.validate(self.packages, self.commit)

    def test_notes_changed_after_packaging_are_rejected(self):
        (self.root / "eng/release/notes.md").write_bytes(b"Different release notes.\n")
        with self.assertRaisesRegex(ValueError, "release notes"):
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

    def test_insufficient_recovery_attempts_are_rejected(self):
        path = self.packages / "osx-arm64/native-evidence/result.json"
        results = json.loads(path.read_bytes())
        results[0]["native_recovery_attempts"] = 1
        path.write_text(json.dumps(results), encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "at least five native recovery attempts"):
            self.validate(self.packages, self.commit)


    def test_signed_update_test_package_is_rejected_by_the_marker_scan(self):
        # Every signature and digest is valid. Only the marker inside the deflated package shows the test build.
        marker = "Tangopad.UpdateTestHook.v1"
        for runtime, encoding in (("linux-x64", "utf-16-le"), ("osx-arm64", "utf-8")):
            with self.subTest(runtime=runtime):
                path = self.packages / f"{runtime}/TangopadDesktop-{self.version}-{runtime}-full.nupkg"
                original = path.read_bytes()
                with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
                    archive.writestr("lib/app/Editor.App", b"MZ" + (marker * 3).encode(encoding))
                self.assertNotIn(marker.encode(encoding), path.read_bytes())
                feed = self.packages / f"{runtime}/releases.{runtime}.json"
                metadata = json.loads(feed.read_bytes())
                metadata["Assets"][0].update(Size=path.stat().st_size, SHA256=hashlib.sha256(path.read_bytes()).hexdigest().upper())
                feed.write_text(json.dumps(metadata), encoding="utf-8")
                self.sign(feed)
                self.write_manifest(runtime)
                try:
                    with self.assertRaisesRegex(ValueError, "update-test marker"):
                        self.validate(self.packages, self.commit)
                finally:
                    path.write_bytes(original)
                    metadata["Assets"][0].update(Size=len(original), SHA256=hashlib.sha256(original).hexdigest().upper())
                    feed.write_text(json.dumps(metadata), encoding="utf-8")
                    self.sign(feed)
                    self.write_manifest(runtime)

    def test_signed_update_test_portable_archive_is_rejected(self):
        path = self.packages / "win-x64/TangopadDesktop-win-x64-Portable.zip"
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("current/Editor.App.exe", "Tangopad.UpdateTestHook.v1".encode("utf-16-le"))
        self.write_manifest("win-x64")
        with self.assertRaisesRegex(ValueError, "update-test marker"):
            self.validate(self.packages, self.commit)

    def test_signed_update_test_manifest_is_rejected(self):
        manifest = self.packages / "linux-x64/package.linux-x64.json"
        content = json.loads(manifest.read_bytes())
        content["update_test"] = True
        manifest.write_text(json.dumps(content), encoding="utf-8")
        self.sign(manifest)
        with self.assertRaisesRegex(ValueError, "Update-test packages"):
            self.validate(self.packages, self.commit)


if __name__ == "__main__":
    unittest.main()
