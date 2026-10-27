#!/usr/bin/env python3
"""Check reviewed dependency notices and preserve runtime notices in publish output."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
RUNTIME_FILES = {'Dotnet.LICENSE.txt': 'license.txt', 'Dotnet.NOTICES.txt': 'third-party-notices.txt'}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def leaf(name):
    if not isinstance(name, str) or not name or Path(name).name != name or '/' in name or '\\' in name:
        raise ValueError('Notice filenames must be plain filenames.')
    return name


def validate(root):
    policy = json.loads((root / 'eng/notices.json').read_text(encoding='utf-8'))
    if policy['schemaVersion'] != 1:
        raise ValueError('The notice inventory schema is unsupported.')
    graph = json.loads((root / 'src/Editor.App/packages.lock.json').read_text(encoding='utf-8'))
    dependencies = {}
    for framework in graph['dependencies'].values():
        for name, entry in framework.items():
            if entry['type'].lower() != 'project':
                key = name.lower()
                if key in dependencies and dependencies[key] != entry['resolved']:
                    raise ValueError(f'The application resolves multiple versions of {name}.')
                dependencies[key] = entry['resolved']
    covered = {}
    referenced = set(policy['additionalFiles'])
    for group in policy['groups'].values():
        if not group['files'] and not group.get('exclusion'):
            raise ValueError('A notice group requires files or an explicit exclusion.')
        referenced.update(group['files'])
        for package, version in group['packages'].items():
            key = package.lower()
            if key in covered:
                raise ValueError(f'Duplicate notice coverage for {package}.')
            covered[key] = version
    if dependencies != covered:
        missing = sorted(dependencies.keys() - covered.keys())
        stale = sorted(key for key in covered if dependencies.get(key) != covered[key])
        raise ValueError(f'Review dependency notice coverage. Missing packages: {missing}. Unreviewed versions or stale packages: {stale}.')
    if referenced != set(policy['files']):
        raise ValueError('The notice groups and file inventory differ.')
    directory = root / 'docs/licenses'
    if {p.name for p in directory.glob('*.txt')} != referenced:
        raise ValueError('The notice directory and file inventory differ.')
    for name, entry in policy['files'].items():
        path = directory / leaf(name)
        if path.is_symlink() or digest(path) != entry['sha256']:
            raise ValueError(f'The reviewed notice changed: {name}.')
        if not entry.get('source'):
            raise ValueError(f'The notice requires a source record: {name}.')
        if entry['source'].get('kind') == 'repository' and digest(root / entry['source']['path']) != entry['sha256']:
            raise ValueError(f'The repository license and copied notice differ: {name}.')
    return policy


def check_components(root, payload, policy):
    directory = payload / 'licenses'
    for name, entry in policy['files'].items():
        path = directory / name
        if not path.is_file() or path.is_symlink() or digest(path) != entry['sha256']:
            raise ValueError(f'The publish output is missing a reviewed notice or contains changed text: {name}.')
    manifest = directory / 'notices.json'
    if not manifest.is_file() or digest(manifest) != digest(root / 'eng/notices.json'):
        raise ValueError('The publish output requires the reviewed notice source inventory.')


def prepare_payload(root, payload, runtime, assets):
    policy = validate(root)
    check_components(root, payload, policy)
    metadata = json.loads(assets.read_text(encoding='utf-8'))
    package_id = 'Microsoft.NETCore.App.Runtime.' + runtime
    downloads = [item for framework in metadata['project']['frameworks'].values()
                 for item in framework.get('downloadDependencies', []) if item['name'].lower() == package_id.lower()]
    if len(downloads) != 1:
        raise ValueError('The publish assets must identify exactly one selected .NET runtime pack.')
    match = re.fullmatch(r'\[([^,\]]+),\s*\1\]', downloads[0]['version'])
    if not match:
        raise ValueError('The runtime pack version must be pinned in the publish assets.')
    version = match[1]
    candidates = [Path(folder) / package_id.lower() / version.lower() for folder in metadata['packageFolders']]
    package = next((path for path in candidates if path.is_dir()), None)
    if package is None:
        raise ValueError('The selected .NET runtime pack is unavailable in the restored package folders.')
    source_files = {path.name.lower(): path for path in package.iterdir() if path.is_file()}
    for name, source in RUNTIME_FILES.items():
        if source not in source_files:
            raise ValueError(f'The selected .NET runtime pack is missing {source}.')
        shutil.copyfile(source_files[source], payload / 'licenses' / name)
    paths = [payload / 'licenses' / name for name in policy['files']]
    paths += [payload / 'licenses' / name for name in (*RUNTIME_FILES, 'notices.json')]
    inventory = {'schemaVersion': 1, 'runtimeIdentifier': runtime,
                 'runtimePack': {'id': package_id, 'version': version},
                 'sourceInventorySha256': digest(root / 'eng/notices.json'),
                 'files': {path.name: digest(path) for path in paths}}
    (payload / 'licenses/distribution.json').write_text(json.dumps(inventory, indent=2) + '\n', encoding='utf-8')
    verify_payload(root, payload, runtime)
    return inventory


def verify_payload(root, payload, runtime):
    policy = validate(root)
    check_components(root, payload, policy)
    directory = payload / 'licenses'
    inventory = json.loads((directory / 'distribution.json').read_text(encoding='utf-8'))
    if (inventory['schemaVersion'] != 1 or inventory['runtimeIdentifier'] != runtime
            or inventory['runtimePack']['id'] != 'Microsoft.NETCore.App.Runtime.' + runtime
            or inventory['sourceInventorySha256'] != digest(root / 'eng/notices.json')):
        raise ValueError('The distribution notice inventory does not match this build.')
    expected = set(policy['files']) | set(RUNTIME_FILES) | {'notices.json'}
    if set(inventory['files']) != expected or {p.name for p in directory.iterdir() if p.is_file()} != expected | {'distribution.json'}:
        raise ValueError('The distribution notice file inventory is incomplete or contains unexpected files.')
    for name, expected_digest in inventory['files'].items():
        path = directory / leaf(name)
        if path.is_symlink() or digest(path) != expected_digest:
            raise ValueError(f'The distributed notice changed: {name}.')
    return inventory


def verify_zip(archive_path, expected):
    with zipfile.ZipFile(archive_path) as archive:
        names = archive.namelist()
        indexes = [name for name in names if name == 'licenses/distribution.json' or name.endswith('/licenses/distribution.json')]
        if len(indexes) != 1 or len(names) != len(set(names)):
            raise ValueError('The archive requires one notice inventory and no duplicate entries.')
        prefix = indexes[0][:-len('distribution.json')]
        if json.loads(archive.read(indexes[0])) != expected:
            raise ValueError('The archived notice inventory differs from the verified publish output.')
        for name, expected_digest in expected['files'].items():
            path = prefix + leaf(name)
            if path not in names or hashlib.sha256(archive.read(path)).hexdigest() != expected_digest:
                raise ValueError(f'The archive is missing a notice or contains changed text: {name}.')


def verify_packages(root, output, runtime, expected):
    if runtime == 'linux-x64':
        images = list(output.glob('*.AppImage'))
        packages = list(output.glob('*-full.nupkg'))
        if len(images) != 1 or len(packages) != 1:
            raise ValueError('Linux notice validation requires one AppImage and one full package.')
        with zipfile.ZipFile(packages[0]) as archive:
            entries = [name for name in archive.namelist() if name.endswith('.AppImage')]
            if len(entries) != 1 or hashlib.sha256(archive.read(entries[0])).hexdigest() != digest(images[0]):
                raise ValueError('The full package and portable AppImage differ.')
        with tempfile.TemporaryDirectory(prefix='tangopad-notices-') as temporary:
            subprocess.run([str(images[0].resolve()), '--appimage-extract'], cwd=temporary,
                           check=True, stdout=subprocess.DEVNULL)
            payload = Path(temporary) / 'squashfs-root/usr/bin'
            if verify_payload(root, payload, runtime) != expected:
                raise ValueError('The AppImage notice inventory differs from the verified publish output.')
    else:
        archives = list(output.glob('*.zip'))
        packages = list(output.glob('*-full.nupkg'))
        if len(archives) != 1 or len(packages) != 1:
            raise ValueError('Notice validation requires one portable ZIP and one full package.')
        verify_zip(archives[0], expected)
        verify_zip(packages[0], expected)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--payload', type=Path, help='Check an extracted self-contained publish directory.')
    parser.add_argument('--rid', help='Expected runtime identifier for --payload.')
    args = parser.parse_args()
    if args.payload and not args.rid:
        parser.error('Use --rid with --payload.')
    try:
        policy = validate(ROOT)
        if args.payload:
            verify_payload(ROOT, args.payload, args.rid)
        print(f"PASS: {len(policy['files'])} reviewed notices cover the application dependency graph.")
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f'ERROR: {error}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
