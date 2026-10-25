"""Prepare an isolated packaging tool with the pinned Windows recovery helper."""
import hashlib
import json
from pathlib import Path
import runpy
import shutil
import subprocess

ROOT = Path(__file__).resolve().parent.parent


def prepare(work, runtime, force_rebuild=False):
    subprocess.run(["dotnet", "tool", "restore"], cwd=ROOT, check=True)
    if runtime != "win-x64":
        return ["dotnet", "tool", "run", "vpk", "--"]
    policy = json.loads((ROOT / "eng/velopack/source.json").read_bytes())
    patch = ROOT / "eng/velopack/windows-recovery.patch"
    output = ROOT / "artifacts/native-helper/win-x64"
    binary = output / "update_x64.exe"
    metadata = output / "native-helper.win-x64.json"
    valid = False
    if binary.exists() and metadata.exists():
        provenance = json.loads(metadata.read_bytes())
        valid = all(provenance.get(key) == value for key, value in policy.items())
        valid = valid and provenance.get("patch_sha256") == hashlib.sha256(patch.read_bytes()).hexdigest()
        valid = valid and provenance.get("binary_sha256") == hashlib.sha256(binary.read_bytes()).hexdigest()
    if not valid or force_rebuild:
        runpy.run_path(str(ROOT / "eng/build-windows-updater.py"))["build"](output)
    tools = work / "tools"
    version = json.loads((ROOT / ".config/dotnet-tools.json").read_bytes())["tools"]["vpk"]["version"]
    subprocess.run(["dotnet", "tool", "install", "vpk", "--version", version, "--tool-path", str(tools)], cwd=ROOT, check=True)
    candidates = list(tools.rglob("update_x64.exe"))
    if len(candidates) != 1:
        raise RuntimeError("The isolated Velopack tool does not contain one Windows x64 helper.")
    replacement = candidates[0].with_suffix(".replacement")
    shutil.copyfile(binary, replacement)
    replacement.replace(candidates[0])
    return [str(tools / "vpk.exe")]
