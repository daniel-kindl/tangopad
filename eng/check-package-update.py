#!/usr/bin/env python3
"""Install a real update-test package and let the installed editor update itself from a local feed.

The editor runs its own check, download, signature check and apply. The feed is served on 127.0.0.1.
Only the update-test build contains the hook that forwards the three expected feed files to that address.
"""
import argparse
import contextlib
import functools
import http.server
import json
import os
from pathlib import Path
import platform
import runpy
import shutil
import subprocess
import sys
import tempfile
import threading
import time

ROOT = Path(__file__).resolve().parent.parent
LAUNCH = runpy.run_path(str(ROOT / "eng/check-package-launch.py"))
MARKER = runpy.run_path(str(ROOT / "eng/update-test-marker.py"))
OLD, NEW = "1.0.0", "1.0.1"
RESTART_SECONDS = 240


def feed_names(rid, version):
    """Return the only three files that the local feed serves."""
    return [f"releases.{rid}.json", f"releases.{rid}.json.sig", f"TangopadDesktop-{version}-{rid}-full.nupkg"]


def read_events(path):
    """Return the events that the update-test hook wrote."""
    if not path.is_file():
        return []
    events = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]
    if any(event.get("Marker") != MARKER["MARKER"] for event in events):
        raise RuntimeError("An event does not come from the update-test hook.")
    return events


def check_rejected(events):
    """The editor must refuse a feed with a bad signature and must not apply anything."""
    steps = [event["Event"] for event in events]
    if steps != ["start", "error"] or events[0]["Version"] != OLD:
        raise RuntimeError(f"The editor did not reject the bad signature: {events}")
    return events[1]["Error"]


def check_updated(events):
    """The editor must download and apply the new version, and the restarted editor must run it."""
    steps = [event["Event"] for event in events]
    if steps != ["start", "downloaded", "applying", "restarted"]:
        raise RuntimeError(f"The editor did not update and restart: {events}")
    if events[0]["Version"] != OLD or events[1]["Version"] != NEW or events[3]["Version"] != NEW:
        raise RuntimeError(f"The versions do not show {OLD} to {NEW}: {events}")
    if events[3]["ProcessId"] == events[0]["ProcessId"]:
        raise RuntimeError("The restarted editor has the same process.")


def check_report(events):
    if [event["Event"] for event in events] != ["report"] or events[0]["Version"] != NEW:
        raise RuntimeError(f"The installed editor does not report {NEW}: {events}")


def package(rid, version, keys):
    subprocess.run([sys.executable, "eng/package-release.py", "--rid", rid, "--update-test", "--update-key-dir", str(keys),
                    "--test-version", version], cwd=ROOT, check=True)
    directory = ROOT / "artifacts/update-test" / rid / version
    MARKER["require_present"](directory / feed_names(rid, version)[2])
    return directory


def serve(directory):
    handler = functools.partial(http.server.SimpleHTTPRequestHandler, directory=str(directory))
    server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server


def run_editor(executable, environment, timeout=180):
    completed = subprocess.run([str(executable)], env=environment, check=False, timeout=timeout,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, errors="replace")
    return completed.returncode, completed.stdout[-8000:]


def wait_for(path, count, seconds):
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        events = read_events(path)
        if len(events) >= count:
            return events
        time.sleep(2)
    return read_events(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", choices=LAUNCH["RUNTIMES"], required=True)
    args = parser.parse_args()
    rid = args.rid
    if os.environ.get("TANGOPAD_RELEASE_BUILD"):
        raise ValueError("The update test does not run in a release build.")
    if platform.system() != LAUNCH["RUNTIMES"][rid]:
        raise ValueError("Run this check on its target operating system.")
    if not rid.startswith("linux-") and (os.environ.get("GITHUB_ACTIONS") != "true" or os.environ.get("RUNNER_ENVIRONMENT") != "github-hosted"):
        raise ValueError("Run installer checks on a fresh GitHub-hosted account to protect existing applications and state.")
    evidence = ROOT / "artifacts/update-test-evidence" / rid
    if evidence.exists():
        shutil.rmtree(evidence)
    evidence.mkdir(parents=True)
    with contextlib.ExitStack() as resources:
        work = Path(resources.enter_context(tempfile.TemporaryDirectory(prefix="tangopad-update-test-")))
        keys = work / "keys"
        old = package(rid, OLD, keys)
        new = package(rid, NEW, keys)
        good, bad = work / "feed-good", work / "feed-bad"
        for feed in (good, bad):
            feed.mkdir()
            for name in feed_names(rid, NEW):
                shutil.copy2(new / name, feed / name)
        other = work / "other-key.pem"
        subprocess.run(["openssl", "genpkey", "-algorithm", "EC", "-pkeyopt", "ec_paramgen_curve:P-256", "-out", str(other)], check=True)
        subprocess.run(["openssl", "dgst", "-sha256", "-sign", str(other), "-out", str(bad / feed_names(rid, NEW)[1]),
                        str(bad / feed_names(rid, NEW)[0])], check=True)
        installers = list(old.glob(LAUNCH["INSTALLERS"][rid.split("-")[0]]))
        if len(installers) != 1:
            raise RuntimeError(f"Expected one {OLD} installer, found {len(installers)}.")
        root = LAUNCH["install"](rid, installers[0], resources, work)
        executable = LAUNCH["installed_executable"](rid, root)
        if not executable.is_file():
            raise RuntimeError(f"The installer did not create {executable}.")
        environment = dict(os.environ)
        if rid.startswith("linux-"):
            environment.update(APPIMAGE_EXTRACT_AND_RUN="1", XDG_DATA_HOME=str(work / "state"))
        environment.pop("TANGOPAD_UPDATE_TEST_REPORT", None)
        result = {"runtime": rid, "installer": installers[0].name, "executable": str(executable), "from": OLD, "to": NEW,
                  "source_commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                  "os": platform.platform()}
        try:
            for name, feed in (("bad-signature", bad), ("update", good)):
                server = serve(feed)
                resources.callback(server.shutdown)
                events_path = evidence / f"{name}.jsonl"
                code, output = run_editor(executable, {**environment, "TANGOPAD_UPDATE_TEST_RUN": str(events_path),
                                                       "TANGOPAD_UPDATE_TEST_VERSION": NEW,
                                                       "TANGOPAD_UPDATE_TEST_PORT": str(server.server_address[1])})
                (evidence / f"{name}.log").write_text(f"exit code {code}\n{output}", encoding="utf-8")
                if name == "bad-signature":
                    result["bad_signature_error"] = check_rejected(read_events(events_path))
                else:
                    events = wait_for(events_path, 4, RESTART_SECONDS)
                    check_updated(events)
                    result["update_events"] = events
                server.shutdown()
            LAUNCH["stop_installed"](root)
            report = evidence / "report.jsonl"
            code, output = run_editor(executable, {**environment, "TANGOPAD_UPDATE_TEST_REPORT": str(report)})
            (evidence / "report.log").write_text(f"exit code {code}\n{output}", encoding="utf-8")
            check_report(read_events(report))
            result["reported_version"] = NEW
            result["passed"] = True
        finally:
            (evidence / "result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
            print(json.dumps(result, indent=2), flush=True)
            LAUNCH["stop_installed"](root)


if __name__ == "__main__":
    main()
