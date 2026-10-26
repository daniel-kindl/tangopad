#!/usr/bin/env python3
"""Check repository Markdown links and product image metadata without network access."""

import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess
import sys
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
LINK = re.compile(r'(!?)\[([^\]\n]*)\]\((<[^>]+>|[^\s)]+)(?:\s+"[^"]*")?\)')


def prose(text):
    """Exclude fenced code examples from link and heading checks."""
    lines = []
    fence = None
    for line in text.splitlines():
        marker = re.match(r'^\s{0,3}(`{3,}|~{3,})', line)
        if marker:
            token = marker[1]
            if fence is None:
                fence = token
            elif token[0] == fence[0] and len(token) >= len(fence):
                fence = None
            continue
        if fence is None:
            lines.append(line)
    return '\n'.join(lines)


def heading_slugs(headings):
    result = []
    for heading in headings:
        label = re.sub(r'<[^>]*>', '', heading).lower()
        slug = re.sub(r'[^\w\- ]', '', label).replace(' ', '-')
        base = slug
        count = 0
        while slug in result:
            count += 1
            slug = f'{base}-{count}'
        result.append(slug)
    return result


def heading_ids(headings):
    return set(heading_slugs(headings))


def anchors(text):
    headings = [match[1] for line in prose(text).splitlines()
                if (match := re.match(r'^#{1,6}\s+(.+?)(?:\s+#+)?$', line))]
    return heading_ids(headings)


def check_links(root, path, links, anchor_lookup=None):
    errors = []
    for image, label, destination in links:
        url = urlsplit(destination)
        if image and not label.strip():
            errors.append(f'{path.relative_to(root)}: The image requires alt text.')
        if url.scheme or url.netloc:
            continue
        target = (path.parent / unquote(url.path)).resolve() if url.path else path
        if not target.is_relative_to(root.resolve()) or not target.exists():
            errors.append(f'{path.relative_to(root)}: Missing local target: {destination}')
        elif url.fragment and target.suffix.lower() == '.md':
            ids = (anchor_lookup[target] if anchor_lookup is not None and target in anchor_lookup
                   else anchors(target.read_text(encoding='utf-8')))
            if unquote(url.fragment) not in ids:
                errors.append(f'{path.relative_to(root)}: Missing heading: {destination}')
    return errors


def png_size(path):
    data = path.read_bytes()
    if len(data) < 24 or data[:8] != b'\x89PNG\r\n\x1a\n' or data[12:16] != b'IHDR':
        raise ValueError('The file has no valid PNG header.')
    return struct.unpack('>II', data[16:24])


def check(root, paths):
    errors = []
    checked = 0
    for path in paths:
        if path.suffix.lower() != '.md':
            continue
        text = prose(path.read_text(encoding='utf-8'))
        links = [(image, label, destination.strip('<>')) for image, label, destination in LINK.findall(text)]
        checked += len(links)
        errors.extend(check_links(root, path, links))
    media = root / 'docs/user/media'
    manifest = media / 'capture.json'
    if manifest.exists():
        try:
            capture = json.loads(manifest.read_text(encoding='utf-8'))
            for field in ('sourceCommit', 'sourceDiffSha256', 'executableSha256', 'assemblySha256', 'host', 'architecture', 'session'):
                if not capture.get(field):
                    errors.append(f'capture.json: Missing {field}.')
            sample = root / capture['sample']
            if not sample.resolve().is_relative_to(root.resolve()):
                raise ValueError('The sample path leaves the repository.')
            if hashlib.sha256(sample.read_bytes()).hexdigest() != capture['sampleSha256']:
                errors.append('capture.json: The sample changed. Repeat the capture.')
            records = capture['images']
            names = [record['file'] for record in records]
            if len(names) != len(set(names)) or set(names) != {p.name for p in media.glob('*.png')}:
                errors.append('capture.json: The PNG inventory does not match the files.')
            total = 0
            for record in records:
                image = media / record['file']
                if image.parent.resolve() != media.resolve():
                    raise ValueError('An image path leaves the media directory.')
                size = image.stat().st_size
                total += size
                width, height = png_size(image)
                if (width, height) != (record['width'], record['height']):
                    errors.append(f'{image.name}: Dimensions differ from the capture record.')
                if not (400 <= width <= 1600 and 250 <= height <= 1200) or size > 350 * 1024:
                    errors.append(f'{image.name}: The image exceeds the product media limits.')
                if hashlib.sha256(image.read_bytes()).hexdigest() != record['sha256']:
                    errors.append(f'{image.name}: The image changed. Repeat the capture review.')
            if total > 1024 * 1024:
                errors.append('Product images exceed the 1 MiB total limit.')
        except (OSError, ValueError, KeyError, TypeError, struct.error) as error:
            errors.append(f'capture.json: Invalid capture record: {error}')
    elif media.exists() and any(media.glob('*.png')):
        errors.append('Product images require capture.json.')
    return errors, checked


def main():
    # Include new files before their first commit. Exclude ignored artifacts.
    names = subprocess.check_output(['git', 'ls-files', '--cached', '--others', '--exclude-standard', '-z'], cwd=ROOT)
    paths = sorted({ROOT / name.decode() for name in names.split(b'\0') if name})
    errors, checked = check(ROOT, paths)
    for error in errors:
        print(f'ERROR: {error}', file=sys.stderr)
    if errors:
        return 1
    print(f'PASS: {checked} Markdown links and product media metadata. External URLs require release review.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
