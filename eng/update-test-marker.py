#!/usr/bin/env python3
"""Find the update-test marker in a package, also inside compressed zip entries.

A .nupkg and a portable .zip are deflate archives, so a plain byte search can miss the
marker. This scan opens each zip entry, also nested archives, and searches the bytes for
the marker in UTF-8 (assembly metadata) and in UTF-16 (the IL string literal).
"""
import io
from pathlib import Path
import sys
import zipfile

MARKER = "Tangopad.UpdateTestHook.v1"
PATTERNS = (MARKER.encode("utf-8"), MARKER.encode("utf-16-le"))
MAX_DEPTH = 4
MAX_ENTRY = 2 * 1024 * 1024 * 1024


def _contains(content, depth):
    if any(pattern in content for pattern in PATTERNS):
        return True
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


def zip_artifacts(directory):
    """Return the zip-format packages in a package directory: the .nupkg and the portable .zip."""
    return sorted(path for path in Path(directory).iterdir() if path.is_file() and path.suffix.lower() in (".nupkg", ".zip"))


if __name__ == "__main__":
    found = [name for name in sys.argv[1:] if contains_marker(name)]
    print("\n".join(found))
    sys.exit(1 if found else 0)
