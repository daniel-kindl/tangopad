#!/usr/bin/env python3
"""Build self-managed packages with authenticated update feeds."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import runpy
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
RUNTIMES = {"win-x64": "Windows", "linux-x64": "Linux", "osx-x64": "Darwin", "osx-arm64": "Darwin"}
FORBIDDEN = {"data", "state.bin", "recent-files.json", "editor-preferences.json", "tangopad.portable", ".tangopad-update-pending"}


def run(*args, **kwargs):
    return subprocess.run([str(arg) for arg in args], check=True, cwd=ROOT, **kwargs)


def required_environment(name):
    value = os.environ.get(name, "")
    if not value.strip():
        raise ValueError(f"Configure {name} before production packaging.")
    return value


def version():
    return ET.parse(ROOT / "Directory.Build.props").findtext(".//Version")


def check_payload(directory):
    for path in directory.rglob("*"):
        if path.is_symlink() or any(part.lower() in FORBIDDEN for part in path.relative_to(directory).parts):
            raise ValueError(f"The application payload contains a user-data path or symbolic link: {path}")


def sign_feed(directory, channel, private_key, public_key):
    feed_path = directory / f"releases.{channel}.json"
    feed = json.loads(feed_path.read_bytes())
    assets = [asset for asset in feed["Assets"] if asset["Type"] == "Full"]
    if len(assets) != 1:
        raise ValueError("A release must contain exactly one full update package.")
    asset = assets[0]
    package = directory / asset["FileName"]
    if package.name != f'TangopadDesktop-{asset["Version"]}-{channel}-full.nupkg':
        raise ValueError("The package filename does not match the release identity.")
    if package.stat().st_size != asset["Size"] or hashlib.sha256(package.read_bytes()).hexdigest().upper() != asset["SHA256"].upper():
        raise ValueError("The package does not match its release metadata.")
    feed_path.write_text(json.dumps({"Assets": assets}, separators=(",", ":")), encoding="utf-8")
    if feed_path.stat().st_size > 65536:
        raise ValueError("The release metadata exceeds its size limit.")
    signature = feed_path.with_suffix(".json.sig")
    run("openssl", "dgst", "-sha256", "-sign", private_key, "-out", signature, feed_path)
    run("openssl", "dgst", "-sha256", "-verify", public_key, "-signature", signature, feed_path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", choices=RUNTIMES, required=True)
    parser.add_argument("--production", action="store_true")
    parser.add_argument("--platform-signing", choices=("none", "configured"), default="none")
    parser.add_argument("--test-version", help="Use a stable version for local upgrade tests only.")
    parser.add_argument("--update-test", action="store_true",
                        help="Build the update-test hook into the editor. Output goes to artifacts/update-test, never to a release.")
    parser.add_argument("--update-key-dir", type=Path,
                        help="Keep the per-run update-test key here, so that two update-test versions share it.")
    args = parser.parse_args()
    if args.production and (args.update_test or args.update_key_dir):
        raise ValueError("Production packaging cannot use --update-test or --update-key-dir.")
    if args.update_key_dir and not args.update_test:
        raise ValueError("--update-key-dir is only for --update-test packages.")
    if args.update_test and os.environ.get("TANGOPAD_RELEASE_BUILD"):
        raise ValueError("An update-test package is not allowed in a release build (TANGOPAD_RELEASE_BUILD is set).")
    private_key_pem = os.environ.pop("TANGOPAD_UPDATE_PRIVATE_KEY_PEM", None)
    release_version = args.test_version or version()
    if not re.fullmatch(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)", release_version or ""):
        raise ValueError("The release version must use stable major.minor.patch syntax.")
    if platform.system() != RUNTIMES[args.rid]:
        raise ValueError("Build this package on its target operating system.")
    if args.production and args.test_version:
        raise ValueError("Production packaging cannot override the authoritative version.")
    runpy.run_path(str(ROOT / "eng/release-notes.py"))["validate"](ROOT, publication=args.production)
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    if args.production and subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip():
        raise ValueError("Production packages require a clean checkout of the reviewed source commit.")
    output = ROOT / "artifacts" / ("update-test" if args.update_test else "release") / args.rid / release_version
    if output.exists():
        shutil.rmtree(output)
    output.mkdir(parents=True)
    with tempfile.TemporaryDirectory(prefix="tangopad-package-") as temporary:
        temporary = Path(temporary)
        tool = runpy.run_path(str(ROOT / "eng/native-vpk.py"))["prepare"](temporary, args.rid, force_rebuild=args.production)
        signing = []
        public_key = ROOT / "eng/release/update-public-key.pem"
        if args.production:
            if private_key_pem:
                private_key = temporary / "update-private.pem"
                descriptor = os.open(private_key, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
                with os.fdopen(descriptor, "w", encoding="utf-8") as output_key:
                    output_key.write(private_key_pem)
                private_key_pem = None
            else:
                default_private = Path(os.environ.get("LOCALAPPDATA", Path.home() / ".local/share")) / "tangopad-release-signing/update-private.pem"
                private_key = Path(os.environ.get("TANGOPAD_UPDATE_PRIVATE_KEY", default_private))
                if not private_key.is_file():
                    raise ValueError("Create an update-feed key with eng/initialize-update-key.py before production packaging.")
            if not public_key.is_file():
                raise ValueError("Commit the release update public key before production packaging.")
            if args.platform_signing == "configured" and args.rid.startswith("win-"):
                signing = ["--signParams", required_environment("TANGOPAD_WINDOWS_SIGN_PARAMS")]
            elif args.platform_signing == "configured" and args.rid.startswith("osx-"):
                signing = ["--signAppIdentity", required_environment("TANGOPAD_MAC_APP_IDENTITY"),
                           "--signInstallIdentity", required_environment("TANGOPAD_MAC_INSTALL_IDENTITY"),
                           "--notaryProfile", required_environment("TANGOPAD_MAC_NOTARY_PROFILE")]
        else:
            key_directory = args.update_key_dir or temporary
            key_directory.mkdir(parents=True, exist_ok=True)
            private_key = key_directory / "test-private.pem"
            public_key = key_directory / "test-public.pem"
            if not private_key.is_file():
                run("openssl", "genpkey", "-algorithm", "EC", "-pkeyopt", "ec_paramgen_curve:P-256", "-out", private_key)
            run("openssl", "pkey", "-in", private_key, "-pubout", "-out", public_key)
            if public_key.read_bytes().strip() == (ROOT / "eng/release/update-public-key.pem").read_bytes().strip():
                raise ValueError("Test packages cannot use the production update key.")
        key_info = subprocess.check_output(["openssl", "pkey", "-pubin", "-in", str(public_key), "-text", "-noout"], text=True)
        if "ASN1 OID: prime256v1" not in key_info:
            raise ValueError("The update trust key must use ECDSA P-256.")
        # Check the private key against the pinned trust root before building any packages.
        challenge = temporary / "challenge"
        challenge.write_bytes(b"Tangopad update identity")
        signature = temporary / "challenge.sig"
        run("openssl", "dgst", "-sha256", "-sign", private_key, "-out", signature, challenge)
        run("openssl", "dgst", "-sha256", "-verify", public_key, "-signature", signature, challenge)
        publish = temporary / "publish"
        run("dotnet", "publish", "src/Editor.App/Editor.App.csproj", "--configuration", "Release",
            "--runtime", args.rid, "--self-contained", "true", "--disable-build-servers",
            "-p:PublishSingleFile=true", f"-p:Version={release_version}",
            f"-p:UpdatePublicKeyPath={public_key}",
            *(["-p:TangopadUpdateTest=true"] if args.update_test else []),
            f"-p:ReleaseLockDirectory={temporary}/locks", "--output", publish)
        notices = runpy.run_path(str(ROOT / "eng/check-notices.py"))
        notice_inventory = notices["prepare_payload"](ROOT, publish, args.rid, ROOT / "src/Editor.App/obj/project.assets.json")
        check_payload(publish)
        prepack = runpy.run_path(str(ROOT / "eng/update-test-marker.py"))
        # Early signal before packing. The package scan after packing stays the gate.
        if args.update_test:
            prepack["require_present_in"](prepack["directory_files"](publish))
        else:
            prepack["require_absent"](prepack["directory_files"](publish))
        executable = "Editor.App.exe" if args.rid.startswith("win-") else "Editor.App"
        pack_args = [*tool, "pack", "--packId", "TangopadDesktop",
                     "--packTitle", "Tangopad", "--packVersion", release_version, "--runtime", args.rid,
                     "--channel", args.rid, "--packDir", str(publish), "--mainExe", executable,
                     "--delta", "None", "--releaseNotes", str(ROOT / "eng/release/notes.md"),
                     "--outputDir", str(output)]
        if args.rid.startswith("linux-"):
            pack_args += ["--icon", str(ROOT / "eng/release/tangopad.svg")]
        if args.rid.startswith("osx-"):
            pack_args += ["--noInst"]
        run(*pack_args, *signing)
        if args.rid.startswith("osx-"):
            runpy.run_path(str(ROOT / "eng/package-macos-installer.py"))["build"](
                output, args.rid, "TangopadDesktop", "Tangopad", release_version,
                os.environ.get("TANGOPAD_MAC_INSTALL_IDENTITY") if args.platform_signing == "configured" else None,
                os.environ.get("TANGOPAD_MAC_NOTARY_PROFILE") if args.platform_signing == "configured" else None)
        notices["verify_packages"](ROOT, output, args.rid, notice_inventory)
        sign_feed(output, args.rid, private_key, public_key)
        marker = runpy.run_path(str(ROOT / "eng/update-test-marker.py"))
        if args.update_test:
            # Positive control: the same scan that guards releases must find the marker here.
            for artifact in marker["zip_artifacts"](output):
                if artifact.suffix.lower() in (".nupkg", ".appimage"):
                    marker["require_present"](artifact)
        else:
            marker["require_absent"](marker["zip_artifacts"](output))
        if args.rid == "win-x64":
            shutil.copyfile(ROOT / "artifacts/native-helper/win-x64/native-helper.win-x64.json", output / "native-helper.win-x64.json")
        shutil.copyfile(public_key, output / f"update-key.{args.rid}.pem")
        manifest = output / f"package.{args.rid}.json"
        digests = {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
                   for path in sorted(output.iterdir()) if path.is_file()}
        manifest.write_text(json.dumps({"version": release_version,
            "runtime": args.rid, "production": args.production, "source_commit": commit,
            "release_notes_sha256": hashlib.sha256((ROOT / "eng/release/notes.md").read_bytes()).hexdigest(),
            "platform_signing": args.platform_signing, "update_test": args.update_test, "artifacts": digests}, indent=2) + "\n", encoding="utf-8")
        run("openssl", "dgst", "-sha256", "-sign", private_key, "-out", str(manifest) + ".sig", manifest)
    print(f"Packages: {output}")


if __name__ == "__main__":
    main()
