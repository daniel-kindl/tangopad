#!/usr/bin/env python3
"""Install the real candidate package and start the installed editor."""
import argparse
import contextlib
import json
import os
from pathlib import Path, PureWindowsPath
import platform
import re
import shutil
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parent.parent
RUNTIMES = {"win-x64": "Windows", "linux-x64": "Linux", "osx-x64": "Darwin", "osx-arm64": "Darwin"}
INSTALLERS = {"win": "*-Setup.exe", "linux": "*.AppImage", "osx": "*-Setup.pkg"}
PASS = "PASS. The shell opened a native window."
CHILD_TIMEOUT = 600
QUERY_TIMEOUT = 60
SETTLE_LIMIT = 60
SETTLE_QUIET = 2


def find_candidate(release, rid):
    """Return the version, directory and installer of the one candidate for a runtime."""
    directories = [path for path in (release / rid).glob("*") if (path / f"package.{rid}.json").is_file()] if (release / rid).is_dir() else []
    if len(directories) != 1:
        raise ValueError(f"Expected one candidate for {rid}, found {len(directories)}.")
    directory = directories[0]
    manifest = json.loads((directory / f"package.{rid}.json").read_bytes())
    if manifest.get("runtime") != rid or manifest.get("version") != directory.name:
        raise ValueError("The candidate manifest does not match its runtime and version.")
    installers = list(directory.glob(INSTALLERS[rid.split("-")[0]]))
    if len(installers) != 1:
        raise ValueError(f"Expected one installer in {directory}, found {len(installers)}.")
    return directory.name, directory, installers[0]


def installed_executable(rid, root):
    """Return the real editor executable that the installer puts below its root."""
    if rid.startswith("win-"):
        return root / "current/Editor.App.exe"
    if rid.startswith("linux-"):
        return root / "Tangopad.AppImage"
    return root / "Tangopad.app/Contents/MacOS/Editor.App"


def smoke_result(exit_code, report):
    """Return the native startup message, or raise when the start did not pass."""
    messages = sorted({message.strip() for message in re.findall(r"(?:PASS|FAILED|NOT TESTED)[.:][^<]*", report)})
    passed = [message for message in messages if message.startswith(PASS)]
    if exit_code != 0 or len(passed) != 1 or len(messages) != 1:
        raise RuntimeError(f"The installed package did not start and close cleanly (exit code {exit_code}): {messages or 'no result'}")
    return passed[0]


def smoke_environment(environment, executable):
    """Point the native startup test at the installed executable."""
    return {**environment, "TANGOPAD_SMOKE_EXECUTABLE": str(executable)}


def editor_processes(root, listing):
    """Return the editor process IDs in a Get-Process JSON listing whose executable is below the root."""
    entries = json.loads(listing) if listing.strip() else []
    entries = [entries] if isinstance(entries, dict) else entries
    prefix = str(PureWindowsPath(root)).rstrip("\\").lower() + "\\"
    return sorted(entry["Id"] for entry in entries if str(PureWindowsPath(entry.get("Path") or "")).lower().startswith(prefix))


def list_editor_processes(root):
    """Return the IDs of the editor processes that run from the installed files."""
    command = "Get-Process | Where-Object { $_.ProcessName -eq 'Editor.App' } | Select-Object Id,Path | ConvertTo-Json -Compress"
    listing = subprocess.run(["powershell", "-NoProfile", "-Command", command], check=True, capture_output=True, text=True,
                             timeout=QUERY_TIMEOUT).stdout
    return editor_processes(root, listing)


def wait_until_settled(executable, processes, clock=time.monotonic, sleep=time.sleep, limit=SETTLE_LIMIT, quiet=SETTLE_QUIET):
    """Wait until the executable exists and the set of installed editor processes has not changed for the quiet time."""
    start = clock()
    last, since = None, start
    while True:
        now = clock()
        current = frozenset(processes()) if executable.is_file() else None
        if current != last:
            last, since = current, now
        elif current is not None and now - since >= quiet:
            return set(current)
        if now - start >= limit:
            raise RuntimeError(f"The installed editor did not settle within {limit} s.")
        sleep(0.5)


def stop_installed(root):
    """Stop editor processes that the installer started from the installed files, and prove that none still run."""
    running = list_editor_processes(root)
    if running:
        subprocess.run(["powershell", "-NoProfile", "-Command", "Stop-Process -Force -Id " + ",".join(map(str, running))],
                       check=False, timeout=QUERY_TIMEOUT)
        remaining = list_editor_processes(root)
        if remaining:
            raise RuntimeError(f"An installed editor is still running after the stop: {remaining}")


def refuse_existing_bundle(bundle):
    """Raise when an application is already at the macOS install destination."""
    if bundle.exists():
        raise RuntimeError("The installer check refuses to replace an existing Tangopad application.")


def check_macos_domains(domains):
    """Raise unless the macOS installer can install only for the current user."""
    if "CurrentUserHomeDirectory" not in domains or "LocalSystem" in domains:
        raise RuntimeError("The macOS installer does not restrict installation to the current user.")


def install(rid, installer, resources, work, home=None):
    if rid.startswith("win-"):
        root = work / "installed"
        # Setup can start the editor after it installs. Wait for Setup to exit and for the editor processes to settle.
        subprocess.run([str(installer), "--silent", "--installto", str(root)], check=True, timeout=CHILD_TIMEOUT)
        wait_until_settled(installed_executable(rid, root), lambda: list_editor_processes(root), time.monotonic, time.sleep)
        stop_installed(root)
        return root
    if rid.startswith("linux-"):
        root = work / "installed"
        root.mkdir()
        shutil.copy2(installer, installed_executable(rid, root))
        installed_executable(rid, root).chmod(0o755)
        return root
    # Installer checks use a fresh hosted macOS account and the package's actual destination.
    root = (home or Path.home()) / "Applications"
    root.mkdir(exist_ok=True)
    bundle = root / "Tangopad.app"
    refuse_existing_bundle(bundle)
    check_macos_domains(subprocess.check_output(["installer", "-pkg", str(installer), "-dominfo"], text=True, timeout=QUERY_TIMEOUT))
    subprocess.run(["installer", "-pkg", str(installer), "-target", "CurrentUserHomeDirectory"], check=True, timeout=CHILD_TIMEOUT)
    resources.callback(shutil.rmtree, bundle, True)
    return root


def run_smoke(command, environment):
    """Run the native startup test against the installed executable with a time limit."""
    return subprocess.run(command, cwd=ROOT, env=environment, check=False, capture_output=True, text=True, timeout=CHILD_TIMEOUT)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", choices=RUNTIMES, required=True)
    parser.add_argument("--release", type=Path, default=ROOT / "artifacts/release")
    args = parser.parse_args()
    rid = args.rid
    if platform.system() != RUNTIMES[rid]:
        raise ValueError("Run this check on its target operating system.")
    if not rid.startswith("linux-") and (os.environ.get("GITHUB_ACTIONS") != "true" or os.environ.get("RUNNER_ENVIRONMENT") != "github-hosted"):
        raise ValueError("Run installer checks on a fresh GitHub-hosted account to protect existing applications and state.")
    version, _, installer = find_candidate(args.release, rid)
    evidence = ROOT / "artifacts/package-launch" / rid
    if evidence.exists():
        shutil.rmtree(evidence)
    evidence.mkdir(parents=True)
    with contextlib.ExitStack() as resources:
        work = Path(resources.enter_context(tempfile.TemporaryDirectory(prefix="tangopad-package-launch-")))
        environment = dict(os.environ)
        if rid.startswith("linux-"):
            environment.update(APPIMAGE_EXTRACT_AND_RUN="1", XDG_DATA_HOME=str(work / "state"))
        root = install(rid, installer, resources, work)
        executable = installed_executable(rid, root)
        if not executable.is_file():
            raise RuntimeError(f"The installer did not create {executable}.")
        command = ["dotnet", "test", "--project", "tests/Editor.Desktop.SmokeTests", "--configuration", "Release", "--no-build",
                   "--report-xunit-trx", "--report-xunit-trx-filename", "package-launch.trx", "--results-directory", str(evidence)]
        print(f"Package launch check {rid}: {executable}", flush=True)
        completed = run_smoke(command, smoke_environment(environment, executable))
        (evidence / "smoke.log").write_text(completed.stdout + completed.stderr, encoding="utf-8")
        reports = "".join(path.read_text(encoding="utf-8") for path in evidence.glob("*.trx"))
        try:
            message = smoke_result(completed.returncode, reports)
        except RuntimeError:
            print((completed.stdout + completed.stderr)[-16000:], flush=True)
            raise
        result = {"runtime": rid, "version": version, "installer": installer.name, "executable": str(executable),
                  "source_commit": source_commit(),
                  "os": platform.platform(), "launch": message}
        (evidence / "result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        print(json.dumps(result, indent=2), flush=True)


def source_commit():
    """Return the commit that the check runs on."""
    return subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()


if __name__ == "__main__":
    main()
