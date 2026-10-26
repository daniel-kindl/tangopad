#!/usr/bin/env python3
"""Validate curated release text and render links from verified artifact files."""

import argparse
from datetime import date
import hashlib
from pathlib import Path
import re
import sys
from urllib.parse import quote
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
REPOSITORY = 'https://github.com/daniel-kindl/tangopad'
VERSION = r'(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)'
SECTIONS = ('Summary', 'Highlights', 'Breaking changes and migration', 'Installation and updates',
            'Downloads', 'Verification and trust', 'Known limitations')
CATEGORIES = {'Added', 'Changed', 'Deprecated', 'Removed', 'Fixed', 'Security'}
ASSETS = '<!-- release-assets -->'


def validate(root, publication=False):
    version = ET.parse(root / 'Directory.Build.props').findtext('.//Version')
    if not re.fullmatch(VERSION, version or ''):
        raise ValueError('The authoritative version must use stable major.minor.patch syntax.')
    notes = (root / 'eng/release/notes.md').read_text(encoding='utf-8')
    if not notes.startswith(f'# Tangopad {version}\n'):
        raise ValueError('The release notes must match the authoritative version.')
    statuses = re.findall(r'^Status: (.+)$', notes, re.MULTILINE)
    if statuses not in (['Draft'], ['Ready for release review']):
        raise ValueError('The notes require one Draft or Ready for release review status.')
    if publication and statuses != ['Ready for release review']:
        raise ValueError('Set Status: Ready for release review after reviewing the release notes.')
    if publication and (f'{REPOSITORY}/blob/v{version}/CHANGELOG.md' not in notes or f'{REPOSITORY}/blob/dev/' in notes):
        raise ValueError('Use version-tagged documentation and changelog links in reviewed release notes.')
    headings = list(re.finditer(r'^## (.+)$', notes, re.MULTILINE))
    for required in SECTIONS:
        if required not in [match[1] for match in headings]:
            raise ValueError(f'The release notes require this section: {required}.')
    if [match[1] for match in headings] != list(SECTIONS):
        raise ValueError('Use the section names and order from the release-notes template.')
    for index, heading in enumerate(headings):
        end = headings[index + 1].start() if index + 1 < len(headings) else len(notes)
        if not notes[heading.end():end].strip():
            raise ValueError(f'The release notes section is empty: {heading[1]}.')
    if notes.count(ASSETS) != 1 or ASSETS not in notes.split('## Downloads\n', 1)[1].split('\n## ', 1)[0]:
        raise ValueError('Keep exactly one release-assets marker in Downloads.')

    history = (root / 'CHANGELOG.md').read_text(encoding='utf-8')
    entries = list(re.finditer(r'^## (.+)$', history, re.MULTILINE))
    if not entries or entries[0][1] != 'Unreleased':
        raise ValueError('The changelog must start with an Unreleased section.')
    versions = []
    dates = []
    bodies = []
    for index, entry in enumerate(entries):
        end = entries[index + 1].start() if index + 1 < len(entries) else len(history)
        body = history[entry.end():end]
        categories = list(re.finditer(r'^### (.+)$', body, re.MULTILINE))
        names = [category[1] for category in categories]
        if len(names) != len(set(names)) or any(name not in CATEGORIES for name in names):
            raise ValueError('Use each supported changelog category at most once per entry.')
        for number, category in enumerate(categories):
            stop = categories[number + 1].start() if number + 1 < len(categories) else len(body)
            if not re.search(r'^- \S', body[category.end():stop], re.MULTILINE):
                raise ValueError(f'The changelog category is empty: {category[1]}.')
        if index == 0:
            if len(entries) == 1 and not categories:
                raise ValueError('The initial Unreleased section must contain actual changes.')
            continue
        match = re.fullmatch(f'({VERSION}) - ([0-9]{{4}}-[0-9]{{2}}-[0-9]{{2}})', entry[1])
        if not match:
            raise ValueError('Use a stable version and an ISO date for each versioned changelog entry.')
        release = match[1]
        if release in versions:
            raise ValueError(f'Duplicate changelog version: {release}.')
        try:
            released = date.fromisoformat(match[2])
        except ValueError as error:
            raise ValueError('The changelog date is invalid.') from error
        if released > date.today():
            raise ValueError('The changelog date cannot be in the future.')
        if not categories:
            raise ValueError('A versioned changelog entry must contain actual changes.')
        versions.append(release)
        dates.append(released)
        bodies.append(body)
    order = [tuple(map(int, value.split('.'))) for value in versions]
    if order != sorted(order, reverse=True) or dates != sorted(dates, reverse=True):
        raise ValueError('Changelog versions and dates must be in descending order.')
    if order and order[0] > tuple(map(int, version.split('.'))):
        raise ValueError('The changelog cannot exceed the authoritative version.')
    for index, release in enumerate(versions):
        expected = (f'{REPOSITORY}/compare/v{versions[index + 1]}...v{release}'
                    if index + 1 < len(versions) else f'{REPOSITORY}/releases/tag/v{release}')
        if expected not in bodies[index]:
            raise ValueError(f'The changelog comparison or tag link is incorrect for {release}.')
    if versions and f'{REPOSITORY}/compare/v{versions[0]}...dev' not in history[entries[0].end():entries[1].start()]:
        raise ValueError('The Unreleased comparison must start at the newest release tag.')
    if publication and (not versions or versions[0] != version):
        raise ValueError(f'Prepare the {version} versioned changelog entry before publication.')
    return version


def render(root, artifacts=()):
    version = validate(root)
    notes = (root / 'eng/release/notes.md').read_text(encoding='utf-8')
    names = [path.name for path in artifacts]
    if len(names) != len(set(names)):
        raise ValueError('Duplicate asset names cannot be uploaded to one release.')
    if not artifacts:
        downloads = 'No production artifacts are supplied. This preview is not a release.'
    else:
        notes = re.sub(r'^Status: .+\n\n', '', notes, count=1, flags=re.MULTILINE)
        lines = ['Versioned release assets and their SHA-256 hashes:', '',
                 '| Artifact | SHA-256 |', '| --- | --- |']
        for path in sorted(artifacts, key=lambda item: item.name):
            url = f'{REPOSITORY}/releases/download/v{version}/{quote(path.name, safe="")}'
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            lines.append(f'| [{path.name}]({url}) | `{digest}` |')
        downloads = '\n'.join(lines)
    return notes.replace(ASSETS, downloads)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publication', action='store_true', help='Require reviewed notes and versioned history.')
    parser.add_argument('--preview', type=Path, help='Write a local preview without creating a tag or release.')
    args = parser.parse_args()
    try:
        version = validate(ROOT, publication=args.publication)
        if args.preview:
            args.preview.parent.mkdir(parents=True, exist_ok=True)
            args.preview.write_text(render(ROOT), encoding='utf-8')
        print(f'PASS: Changelog and release notes match {version}. No release was published.')
    except (OSError, ValueError, ET.ParseError) as error:
        print(f'ERROR: {error}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
