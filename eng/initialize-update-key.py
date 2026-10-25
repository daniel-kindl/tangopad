#!/usr/bin/env python3
"""Create a free update-feed signing key. Keep its private half outside the repository."""
import argparse
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    default_root = Path(os.environ.get("LOCALAPPDATA", Path.home() / ".local/share")) / "tangopad-release-signing"
    parser.add_argument("--private-key", type=Path, default=default_root / "update-private.pem")
    args = parser.parse_args()
    private = args.private_key.resolve()
    public = ROOT / "eng/release/update-public-key.pem"
    if private.is_relative_to(ROOT):
        raise ValueError("Keep the private signing key outside the repository.")
    private.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    with tempfile.TemporaryDirectory(prefix="update-key-", dir=private.parent) as temporary:
        temporary = Path(temporary)
        candidate = temporary / "private.pem"
        if not private.exists():
            descriptor = os.open(candidate, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
            os.close(descriptor)
            subprocess.run(["openssl", "genpkey", "-algorithm", "EC", "-pkeyopt", "ec_paramgen_curve:P-256", "-out", str(candidate)], check=True)
        source = private if private.exists() else candidate
        derived = subprocess.check_output(["openssl", "pkey", "-in", str(source), "-pubout"])
        if public.exists() and public.read_bytes().replace(b"\r\n", b"\n") != derived.replace(b"\r\n", b"\n"):
            raise ValueError("The existing trust root uses a different key. Plan key rotation before replacing it.")
        if not private.exists():
            os.link(candidate, private)
        if not public.exists():
            with public.open("xb") as output:
                output.write(derived)
    print(f"Public trust key: {public}")
    print(f"Private signing key: {private}")
    print("Back up the private key outside the repository. Do not publish its contents.")


if __name__ == "__main__":
    main()
