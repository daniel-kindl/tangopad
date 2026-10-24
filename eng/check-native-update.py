#!/usr/bin/env python3
"""Exercise the editor's native update commands with signed package feeds."""
import argparse
import contextlib
import hashlib
import json
import io
import os
from pathlib import Path
import platform
import runpy
import shutil
import subprocess
import tempfile
import time
import threading
import zipfile

ROOT = Path(__file__).resolve().parent.parent
POLICY = runpy.run_path(str(ROOT / "eng/package-release.py"))
RUNTIMES = POLICY["RUNTIMES"]
ORIGINAL = "keep λ café\r\n".encode()


def run(*arguments, **kwargs):
    return subprocess.run([str(arg) for arg in arguments], check=True, cwd=ROOT, **kwargs)


def extract_zip(archive, destination):
    with zipfile.ZipFile(archive) as package:
        for entry in package.infolist():
            target = destination / entry.filename
            if not target.resolve().is_relative_to(destination.resolve()):
                raise ValueError("The portable archive contains an unsafe path.")
            package.extract(entry, destination)
            mode = entry.external_attr >> 16
            if mode and not entry.is_dir():
                target.chmod(mode & 0o777)


def write_json(path, value):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value), encoding="utf-8")
    temporary.replace(path)


def wait_report(path, expected_version, process, timeout=90):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if path.is_file():
            result = json.loads(path.read_bytes())
            if result.get("failure"):
                raise RuntimeError(result["failure"])
            if result.get("Version") == expected_version:
                return result
            raise RuntimeError("The update did not apply: " + json.dumps(result))
        if process.poll() not in (None, 0):
            raise RuntimeError(f"The native test host exited with code {process.returncode}. Read its log.")
        time.sleep(0.1)
    raise RuntimeError("The native test host did not report the expected version. Read its log.")


def wait_for_exit(pid):
    if platform.system() == "Windows":
        import ctypes
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.OpenProcess.argtypes = [ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
        kernel.OpenProcess.restype = ctypes.c_void_p
        kernel.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
        kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        handle = kernel.OpenProcess(0x100000, False, pid)
        if handle:
            try:
                if kernel.WaitForSingleObject(handle, 20000) != 0:
                    raise RuntimeError("The restarted editor did not exit after its report.")
            finally:
                kernel.CloseHandle(handle)
        return
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        try:
            status = Path(f"/proc/{pid}/stat")
            if status.exists() and status.read_text().rsplit(")", 1)[1].strip().startswith("Z"):
                return
            os.kill(pid, 0)
        except ProcessLookupError:
            return
        time.sleep(0.1)
    raise RuntimeError("The restarted editor did not exit after its report.")


@contextlib.contextmanager
def block_windows_replacement(packages, current):
    """Hold a staged directory without delete sharing until native recovery completes."""
    import ctypes
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_ulong, ctypes.c_ulong, ctypes.c_void_p,
                                  ctypes.c_ulong, ctypes.c_ulong, ctypes.c_void_p]
    kernel.CreateFileW.restype = ctypes.c_void_p
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    stopped, locked, backed_up = threading.Event(), threading.Event(), threading.Event()
    errors = []

    def hold():
        handle = None
        try:
            deadline = time.monotonic() + 75
            while not stopped.is_set() and time.monotonic() < deadline:
                for executable in (packages / "VelopackTemp").glob("*/Tangopad.UpdateProbe.exe"):
                    handle = kernel.CreateFileW(str(executable.parent), 0x80000000, 3, None, 3, 0x02000000, None)
                    if handle not in (None, ctypes.c_void_p(-1).value):
                        locked.set()
                        break
                if locked.is_set():
                    break
                stopped.wait(0.005)
            while locked.is_set() and not stopped.is_set():
                if not current.exists():
                    backed_up.set()
                stopped.wait(0.005)
        except OSError as error:
            errors.append(str(error))
        finally:
            if handle not in (None, ctypes.c_void_p(-1).value):
                kernel.CloseHandle(handle)

    worker = threading.Thread(target=hold, daemon=True)
    worker.start()
    try:
        yield
    finally:
        stopped.set()
        worker.join(timeout=5)
    if errors or not locked.is_set() or not backed_up.is_set():
        raise RuntimeError("The Windows failure check did not reach replacement after backup: " + str(errors))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", choices=RUNTIMES, required=True)
    args = parser.parse_args()
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip())
    rid = args.rid
    if platform.system() != RUNTIMES[rid]:
        raise ValueError("Run this check on its target operating system.")
    if not rid.startswith("linux-") and (os.environ.get("GITHUB_ACTIONS") != "true" or os.environ.get("RUNNER_ENVIRONMENT") != "github-hosted"):
        raise ValueError("Run installer checks on a fresh GitHub-hosted account to protect existing applications and state.")
    evidence = ROOT / "artifacts/native-update" / rid
    if evidence.exists():
        shutil.rmtree(evidence)
    evidence.mkdir(parents=True)
    results = []
    with contextlib.ExitStack() as resources:
        work = Path(resources.enter_context(tempfile.TemporaryDirectory(prefix="tangopad-native-update-")))
        environment = dict(os.environ)
        if rid.startswith("linux-"):
            environment.update(APPIMAGE_EXTRACT_AND_RUN="1", XDG_DATA_HOME=str(work / "installed-state"))
            if not environment.get("DISPLAY"):
                log = resources.enter_context((evidence / "xvfb.log").open("w"))
                display = subprocess.Popen(["Xvfb", "-displayfd", "1", "-screen", "0", "1280x900x24", "-nolisten", "tcp"], stdout=subprocess.PIPE, stderr=log, text=True)
                resources.callback(display.wait)
                resources.callback(display.terminate)
                number = display.stdout.readline().strip()
                if not number.isdecimal():
                    raise RuntimeError("The native test display did not start.")
                environment["DISPLAY"] = ":" + number
        key = work / "private.pem"
        public = work / "public.pem"
        run("openssl", "genpkey", "-algorithm", "EC", "-pkeyopt", "ec_paramgen_curve:P-256", "-out", key)
        run("openssl", "pkey", "-in", key, "-pubout", "-out", public)
        publish = work / "publish"
        tool = runpy.run_path(str(ROOT / "eng/native-vpk.py"))["prepare"](work, rid)
        run("dotnet", "publish", ROOT / "eng/Editor.UpdateProbe/Editor.UpdateProbe.csproj", "--configuration", "Release",
            "--runtime", rid, "--self-contained", "true", "-p:PublishSingleFile=true",
            f"-p:UpdatePublicKeyPath={public}", f"-p:ReleaseLockDirectory={work}/locks", "--output", publish)
        feed = work / "feed"
        initial = work / "initial"
        newer = f"0.2.{int(time.time())}"
        for version in ("0.1.0", newer):
            executable = "Tangopad.UpdateProbe.exe" if rid.startswith("win-") else "Tangopad.UpdateProbe"
            command = [*tool, "pack", "--packId", "TangopadUpdateProbe", "--packTitle", "Tangopad Update Probe",
                       "--packVersion", version, "--packDir", str(publish), "--mainExe", executable,
                       "--runtime", rid, "--channel", rid, "--delta", "None", "--outputDir", str(feed)]
            if rid.startswith("linux-"):
                command += ["--icon", str(ROOT / "eng/release/tangopad.svg")]
            if rid.startswith("osx-"):
                command += ["--noInst"]
            run(*command)
            if rid.startswith("osx-"):
                runpy.run_path(str(ROOT / "eng/package-macos-installer.py"))["build"](
                    feed, rid, "TangopadUpdateProbe", "Tangopad Update Probe", version)
            if version == "0.1.0":
                shutil.copytree(feed, initial)
        feed_file = feed / f"releases.{rid}.json"
        metadata = json.loads(feed_file.read_bytes())
        metadata["Assets"] = [asset for asset in metadata["Assets"] if asset["Version"] == newer]
        write_json(feed_file, metadata)
        signature_file = Path(str(feed_file) + ".sig")
        run("openssl", "dgst", "-sha256", "-sign", key, "-out", signature_file, feed_file)
        signature = signature_file.read_bytes()
        package = feed / metadata["Assets"][0]["FileName"]
        package_bytes = package.read_bytes()

        for mode in ("--installed", "--portable"):
            directory = work / mode[2:]
            directory.mkdir()
            if rid.startswith("linux-"):
                application = directory / "Tangopad.AppImage"
                shutil.copy2(next(initial.glob("*.AppImage")), application)
                application.chmod(0o755)
                root = directory
            elif mode == "--portable":
                if rid.startswith("osx-"):
                    run("ditto", "-x", "-k", next(initial.glob("*.zip")), directory)
                else:
                    extract_zip(next(initial.glob("*.zip")), directory)
                if rid.startswith("win-"):
                    application = next(directory.rglob("current/Tangopad.UpdateProbe.exe"))
                    root = application.parent.parent
                else:
                    bundle = next(directory.glob("*.app"))
                    application = bundle / "Contents/MacOS/Tangopad.UpdateProbe"
                    root = directory
            elif rid.startswith("win-"):
                run(next(initial.glob("*-Setup.exe")), "--silent", "--installto", directory)
                application = directory / "current/Tangopad.UpdateProbe.exe"
                root = directory
            else:
                # Installer tests use a fresh hosted macOS account and the package's actual destination.
                root = Path.home() / "Applications"
                root.mkdir(exist_ok=True)
                application = root / "Tangopad Update Probe.app/Contents/MacOS/Tangopad.UpdateProbe"
                if application.parent.parent.parent.exists():
                    raise RuntimeError("The installer check refuses to replace an existing Tangopad application.")
                installer = next(initial.glob("*.pkg"))
                domains = subprocess.check_output(["installer", "-pkg", str(installer), "-dominfo"], text=True)
                if "CurrentUserHomeDirectory" not in domains or "LocalSystem" in domains:
                    raise RuntimeError("The macOS installer does not restrict installation to the current user.")
                run("installer", "-pkg", installer, "-target", "CurrentUserHomeDirectory")
                resources.callback(shutil.rmtree, application.parent.parent.parent, True)
            configuration_path = root / ".tangopad-update-probe.json"
            marker = root / "tangopad.portable"
            sibling = root / "data/keep.bin"
            if configuration_path.exists() or marker.exists() or sibling.parent.exists():
                raise RuntimeError("The native check refuses to replace existing portable files.")
            marker.write_bytes(b"preserve marker")
            sibling.parent.mkdir()
            sibling.write_bytes(b"preserve sibling data")
            resources.callback(shutil.rmtree, sibling.parent, True)
            resources.callback(marker.unlink, missing_ok=True)
            resources.callback(configuration_path.unlink, missing_ok=True)
            document = directory / "document.txt"
            document.write_bytes(ORIGINAL)
            result_path = work / (mode[2:] + "-result.json")
            configuration = {"FeedDirectory": str(feed), "DocumentPath": str(document), "ResultPath": str(result_path),
                             "EvidenceDirectory": "", "Action": "initialize", "Fault": "none"}

            def launch(action, expected="0.1.0", fault="none", label=None, protect_root=False, protect_bundle=False):
                print(f"Native update check {rid} {mode}: {label or action}", flush=True)
                step_evidence = evidence / mode[2:] / (label or action)
                step_evidence.mkdir(parents=True, exist_ok=True)
                result_path.unlink(missing_ok=True)
                configuration.update(Action=action, Fault=fault, EvidenceDirectory=str(step_evidence))
                write_json(configuration_path, configuration)
                bundle_manifest = application.parent.parent / "Info.plist"
                bundle_mode = bundle_manifest.stat().st_mode if protect_bundle else None
                if protect_bundle:
                    bundle_manifest.chmod(bundle_mode & ~0o222)
                original_mode = root.stat().st_mode
                if protect_root:
                    root.chmod(original_mode & ~0o222)
                with (step_evidence / "process.log").open("w", encoding="utf-8") as log:
                    process = subprocess.Popen([str(application), mode], env=environment, stdout=log, stderr=log)
                    try:
                        result = wait_report(result_path, expected, process)
                        process.wait(timeout=20)
                        wait_for_exit(result["ProcessId"])
                    except (RuntimeError, subprocess.TimeoutExpired):
                        log.flush()
                        print((step_evidence / "process.log").read_text(encoding="utf-8", errors="replace")[-16000:], flush=True)
                        raise
                    finally:
                        if protect_root:
                            root.chmod(original_mode)
                        if protect_bundle:
                            bundle_manifest.chmod(bundle_mode)
                        if process.poll() is None:
                            process.kill()
                            process.wait()
                write_json(step_evidence / "result.json", result)
                if not result["WindowVisible"] or not result["CanEdit"]:
                    raise RuntimeError("The editor was not available after the update operation.")
                return result

            first = launch("initialize")
            state_path = Path(first["StatePath"])
            state = state_path.read_bytes()
            resources.callback(state_path.unlink, missing_ok=True)
            cached = Path(first["PackagesDirectory"]) / package.name
            if cached.exists():
                if cached.is_symlink() or hashlib.sha256(cached.read_bytes()).digest() != hashlib.sha256(package_bytes).digest():
                    raise RuntimeError("The probe refuses to remove an unrelated cached package.")
                cached.unlink()

            signature_file.write_bytes(bytes([signature[0] ^ 1]) + signature[1:])
            rejected = launch("fault", label="signature-rejection")
            if not rejected["Error"]:
                raise RuntimeError("The editor accepted an invalid feed signature.")
            signature_file.write_bytes(signature)
            truncated = launch("fault", fault="truncate", label="interrupted-download")
            if not truncated["Error"]:
                raise RuntimeError("The editor accepted a partial package download.")
            package.write_bytes(package_bytes[:-1] + bytes([package_bytes[-1] ^ 1]))
            corrupted = launch("fault", label="checksum-rejection")
            if not corrupted["Error"]:
                raise RuntimeError("The editor accepted a package checksum mismatch.")
            package.write_bytes(package_bytes)
            cancelled = launch("cancel")
            if not cancelled["Dirty"] or cancelled["Text"] != ORIGINAL.decode() + "pending update edit\r\n":
                raise RuntimeError("Cancelled restart did not preserve unsaved text.")
            launch("report")
            if mode == "--portable" and not rid.startswith("win-"):
                protected = launch("protected", label="protected-directory", protect_root=True)
                if not protected["Error"] or protected["RecoveryError"] or (root / ".tangopad-update-pending").exists():
                    raise RuntimeError("The editor did not reject update in a protected directory before native apply.")
            if rid.startswith("osx-"):
                protected_bundle = launch("protected", label="protected-bundle", protect_bundle=True)
                if not protected_bundle["Error"] or protected_bundle["RecoveryError"]:
                    raise RuntimeError("The editor did not reject a protected bundle before native apply.")
            secondary_path = Path(str(result_path) + ".secondary")
            secondary_path.unlink(missing_ok=True)
            release_secondary = Path(str(secondary_path) + ".release")
            release_secondary.unlink(missing_ok=True)
            second_evidence = evidence / mode[2:] / "second-window"
            second_evidence.mkdir(parents=True, exist_ok=True)
            configuration["EvidenceDirectory"] = str(second_evidence)
            write_json(configuration_path, configuration)
            with (second_evidence / "process.log").open("w", encoding="utf-8") as log:
                secondary = subprocess.Popen([str(application), mode, "--probe-secondary"], env=environment, stdout=log, stderr=log)
                try:
                    wait_report(secondary_path, "0.1.0", secondary)
                    refused = launch("other-instance")
                    if not refused["Error"] or secondary.poll() is not None:
                        raise RuntimeError("The update did not preserve the other running editor.")
                    release_secondary.touch()
                    secondary.wait(timeout=20)
                    after = json.loads(secondary_path.read_bytes())
                    write_json(evidence / mode[2:] / "second-window.json", after)
                    if secondary.returncode != 0 or not after["Dirty"] or after["Text"] != ORIGINAL.decode() + "second window unsaved edit\r\n":
                        raise RuntimeError("The other editor lost its unsaved text during update refusal.")
                finally:
                    release_secondary.touch()
                    if secondary.poll() is None:
                        secondary.kill()
                        secondary.wait()
            if state_path.read_bytes() != state or document.read_bytes() != ORIGINAL:
                raise RuntimeError("A rejected or cancelled update changed application state or the document.")
            # The authenticated archive has an invalid native manifest. Download succeeds,
            # but the native helper must reject installation and restart the old editor.
            invalid = io.BytesIO()
            with zipfile.ZipFile(io.BytesIO(package_bytes)) as source, zipfile.ZipFile(invalid, "w") as target:
                for entry in source.infolist():
                    content = b"invalid native manifest" if entry.filename.endswith(".nuspec") else source.read(entry)
                    target.writestr(entry, content)
            package.write_bytes(invalid.getvalue())
            asset = metadata["Assets"][0]
            asset.update(Size=package.stat().st_size, SHA256=hashlib.sha256(package.read_bytes()).hexdigest().upper())
            write_json(feed_file, metadata)
            run("openssl", "dgst", "-sha256", "-sign", key, "-out", signature_file, feed_file)
            cached.unlink(missing_ok=True)
            recovered = launch("recover", label="native-recovery")
            if not recovered["RecoveryError"] or recovered["Dirty"]:
                raise RuntimeError("The native update did not report recovery to the previous editor.")
            if state_path.read_bytes() != state or document.read_bytes() != ORIGINAL:
                raise RuntimeError("A failed native update changed application state or the document.")
            package.write_bytes(package_bytes)
            asset.update(Size=len(package_bytes), SHA256=hashlib.sha256(package_bytes).hexdigest().upper())
            write_json(feed_file, metadata)
            run("openssl", "dgst", "-sha256", "-sign", key, "-out", signature_file, feed_file)
            cached.unlink(missing_ok=True)
            if rid == "win-x64":
                with block_windows_replacement(Path(first["PackagesDirectory"]), root / "current"):
                    rolled_back = launch("recover", label="replacement-rollback")
                if not rolled_back["RecoveryError"] or state_path.read_bytes() != state or document.read_bytes() != ORIGINAL:
                    raise RuntimeError("Windows did not restore the previous editor and preserve its data after replacement failed.")
            updated = launch("apply", expected=newer)
            if updated["Dirty"] or document.read_bytes() != ORIGINAL + b"pending update edit\r\n":
                raise RuntimeError("The update did not save the exact edited document before restart.")
            if Path(updated["StatePath"]) != state_path or state_path.read_bytes() != state:
                raise RuntimeError("The native update changed the selected application-state file.")
            if marker.read_bytes() != b"preserve marker" or sibling.read_bytes() != b"preserve sibling data":
                raise RuntimeError("The native update changed sibling portable data.")
            results.append({"runtime": rid, "source_commit": commit,
                            "source_dirty": dirty or bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip())
                                or subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip() != commit,
                            "os": platform.platform(), "mode": mode,
                            "native_helper": json.loads((ROOT / "artifacts/native-helper/win-x64/native-helper.win-x64.json").read_bytes()) if rid == "win-x64" else None, "old_version": "0.1.0", "new_version": newer,
                            "signature_rejected": True, "partial_download_rejected": True, "checksum_rejected": True,
                            "unsaved_cancel_preserved": True, "save_before_restart": True, "state_preserved": True, "native_failure_recovered": True, "replacement_after_backup_recovered": rid == "win-x64", "other_window_preserved": True,
                            "protected_directory_rejected": mode == "--portable" and not rid.startswith("win-"),
                            "protected_bundle_rejected": rid.startswith("osx-")})
            write_json(evidence / "result.json", results)
        print(json.dumps(results, indent=2))


if __name__ == "__main__":
    main()
