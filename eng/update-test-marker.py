#!/usr/bin/env python3
"""Find the update-test marker in a package, also inside compressed zip entries.

A .nupkg and a portable .zip are deflate archives, so a plain byte search can miss the
marker. This scan opens each zip entry, also nested archives, and searches the bytes for
the marker in UTF-8 (assembly metadata) and in UTF-16 (the IL string literal).

The Linux package keeps the app inside an AppImage with a compressed SquashFS image. The scan
extracts each AppImage with its own runtime (--appimage-extract) into a fresh temporary
directory, without network and with a timeout, and scans the extracted files. It never runs the
app. The scan fails closed: on a host that is not Linux, when the extraction fails or times out,
and when it gives no files.
"""
import io
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
import zipfile

MARKER = "Tangopad.UpdateTestHook.v1"
PATTERNS = (MARKER.encode("utf-8"), MARKER.encode("utf-16-le"))
MAX_DEPTH = 4
MAX_ENTRY = 2 * 1024 * 1024 * 1024
EXTRACT_SECONDS = 300
platform_system = platform.system


def is_appimage(content):
    """Return True for an ELF file with the type 2 AppImage magic."""
    return content[:4] == b"\x7fELF" and content[8:11] == b"AI\x02"


def run_extractor(image, destination, timeout):
    """Extract an AppImage with its own runtime. No network, no proxy, a timeout, and the app does not run."""
    environment = {"PATH": "/usr/bin:/bin", "HOME": str(destination), "LANG": "C.UTF-8"}
    subprocess.run(["unshare", "--user", "--map-root-user", "--net", "--", str(image), "--appimage-extract"],
                   cwd=destination, env=environment, timeout=timeout, check=True,
                   stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)


def _appimage_contains(content, depth):
    if platform_system() != "Linux":
        raise ValueError("The AppImage marker scan runs only on Linux. It cannot pass on another host.")
    work = Path(tempfile.mkdtemp(prefix="tangopad-marker-scan-"))
    try:
        image = work / "package.AppImage"
        image.write_bytes(content)
        image.chmod(0o700)
        output = work / "out"
        output.mkdir()
        try:
            run_extractor(image, output, EXTRACT_SECONDS)
        except (subprocess.CalledProcessError, subprocess.TimeoutExpired, OSError) as error:
            raise ValueError(f"The AppImage extraction failed, so the marker scan cannot pass: {type(error).__name__}") from error
        files = [path for path in (output / "squashfs-root").rglob("*") if path.is_file() and not path.is_symlink()]
        if not files:
            raise ValueError("The AppImage extraction gave no files, so the marker scan cannot pass.")
        return any(_contains(path.read_bytes(), depth + 1) for path in files)
    finally:
        shutil.rmtree(work, ignore_errors=True)


def _contains(content, depth):
    if any(pattern in content for pattern in PATTERNS):
        return True
    if is_appimage(content):
        if depth >= MAX_DEPTH:
            raise ValueError("The AppImage is nested too deep to scan.")
        return _appimage_contains(content, depth)
    if depth >= MAX_DEPTH or not zipfile.is_zipfile(io.BytesIO(content)):
        return False
    with zipfile.ZipFile(io.BytesIO(content)) as archive:
        for entry in archive.infolist():
            if entry.is_dir():
                continue
            if entry.file_size > MAX_ENTRY:
                raise ValueError(f"The package entry is too large to scan: {entry.filename}")
            if _contains(archive.read(entry), depth + 1):
                return True
    return False


def contains_marker(path):
    """Return True when the file, or any zip entry in it, contains the update-test marker."""
    return _contains(Path(path).read_bytes(), 0)


def require_absent(paths):
    """Raise when a production or default package contains the update-test marker."""
    for path in paths:
        if contains_marker(path):
            raise ValueError(f"The package contains the update-test marker and cannot be a release package: {Path(path).name}")


def require_present(path):
    """Positive control: prove the scan finds the marker in a real update-test package."""
    if not contains_marker(path):
        raise ValueError(f"The marker scan positive control failed. The update-test package has no marker: {Path(path).name}")


def require_present_in(paths):
    """Positive control for a folder: at least one file must contain the marker."""
    if not any(contains_marker(path) for path in paths):
        raise ValueError("The marker scan positive control failed. No file in the update-test folder has the marker.")


def directory_files(directory):
    """Return every regular file below a folder, for example the published app before packing."""
    return sorted(path for path in Path(directory).rglob("*") if path.is_file() and not path.is_symlink())


def zip_artifacts(directory):
    """Return the packages to scan: the .nupkg, the portable .zip and the Linux .AppImage."""
    return sorted(path for path in Path(directory).iterdir()
                  if path.is_file() and path.suffix.lower() in (".nupkg", ".zip", ".appimage"))


if __name__ == "__main__":
    found = [name for name in sys.argv[1:] if contains_marker(name)]
    print("\n".join(found))
    sys.exit(1 if found else 0)
