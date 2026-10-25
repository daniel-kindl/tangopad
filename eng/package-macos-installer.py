#!/usr/bin/env python3
"""Build a macOS installer that permits only current-user installation."""
from pathlib import Path
import plistlib
import subprocess
import tempfile
import xml.etree.ElementTree as ET


def run(*arguments):
    subprocess.run([str(argument) for argument in arguments], check=True)


def build(output, runtime, package_id, title, version, identity=None, notary_profile=None):
    portable = list(output.glob("*.zip"))
    if len(portable) != 1:
        raise ValueError("One portable macOS bundle is required to build its installer.")
    with tempfile.TemporaryDirectory(prefix="tangopad-user-installer-") as temporary:
        temporary = Path(temporary)
        payload = temporary / "payload"
        run("ditto", "-x", "-k", portable[0], payload)
        bundles = list(payload.glob("*.app"))
        if len(bundles) != 1:
            raise ValueError("The portable archive must contain one application bundle.")
        components = temporary / "components.plist"
        run("pkgbuild", "--analyze", "--root", payload, components)
        entries = plistlib.loads(components.read_bytes())
        for entry in entries:
            entry["BundleIsRelocatable"] = False
        components.write_bytes(plistlib.dumps(entries))
        component = temporary / "application.pkg"
        run("pkgbuild", "--root", payload, "--component-plist", components,
            "--identifier", package_id, "--version", version, "--install-location", "/Applications", component)
        distribution = ET.Element("installer-gui-script", {"minSpecVersion": "2"})
        ET.SubElement(distribution, "title").text = title
        ET.SubElement(distribution, "options", {"customize": "never", "require-scripts": "false"})
        ET.SubElement(distribution, "domains", {"enable_anywhere": "false", "enable_currentUserHome": "true", "enable_localSystem": "false"})
        outline = ET.SubElement(distribution, "choices-outline")
        ET.SubElement(outline, "line", {"choice": "application"})
        choice = ET.SubElement(distribution, "choice", {"id": "application", "visible": "false"})
        ET.SubElement(choice, "pkg-ref", {"id": package_id})
        ET.SubElement(distribution, "pkg-ref", {"id": package_id, "version": version, "onConclusion": "none"}).text = "application.pkg"
        xml = temporary / "distribution.xml"
        ET.ElementTree(distribution).write(xml, encoding="utf-8", xml_declaration=True)
        installer = output / f"{package_id}-{runtime}-Setup.pkg"
        signing = ["--sign", identity] if identity else []
        run("productbuild", "--distribution", xml, "--package-path", temporary, *signing, installer)
        if notary_profile:
            run("xcrun", "notarytool", "submit", installer, "--keychain-profile", notary_profile, "--wait")
            run("xcrun", "stapler", "staple", installer)
            run("spctl", "--assess", "--type", "install", "--verbose", installer)
    return installer
