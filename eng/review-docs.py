#!/usr/bin/env python3
"""Render documentation through GitHub and record local-link and optional HTTP checks."""

import argparse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
import hashlib
from html import escape, unescape
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import runpy
import subprocess
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import unquote, urlsplit
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]
CHECKER = runpy.run_path(str(ROOT / 'eng/check-docs.py'))
DEFAULTS = ['README.md', 'CHANGELOG.md', 'CONTRIBUTING.md', 'docs/development.md',
            'docs/release-notes.md', 'docs/release-distribution.md', 'docs/product-documentation.md', 'docs/documentation-qa.md']


class Page(HTMLParser):
    def __init__(self, text):
        super().__init__(convert_charrefs=True)
        self.links = []
        self.headings = []
        self.ids = set()
        self.heading = None
        self.feed(text)

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if attrs.get('id'):
            self.ids.add(attrs['id'])
        if re.fullmatch(r'h[1-6]', tag):
            self.heading = [int(tag[1]), '']
            self.headings.append(self.heading)
        if tag == 'a' and 'href' in attrs:
            self.links.append((False, '', attrs['href']))
        if tag == 'img':
            self.links.append((True, attrs.get('alt', ''), attrs.get('src', '')))

    def handle_data(self, data):
        if self.heading is not None:
            self.heading[1] += data

    def handle_endtag(self, tag):
        if re.fullmatch(r'h[1-6]', tag):
            self.heading = None


def check_rendered(root, rendered):
    pages = {path: Page(text) for path, text in rendered.items()}
    lookup = {path: CHECKER['heading_ids']([label for _, label in page.headings]) | page.ids
              for path, page in pages.items()}
    errors = []
    urls = set()
    count = 0
    for path, page in pages.items():
        if sum(level == 1 for level, _ in page.headings) != 1:
            errors.append(f'{path.relative_to(root)}: Expected one rendered level-one heading.')
        previous = 0
        for level, _ in page.headings:
            if previous and level > previous + 1:
                errors.append(f'{path.relative_to(root)}: Heading level jumps from {previous} to {level}.')
            previous = level
        errors.extend(CHECKER['check_links'](root, path, page.links, lookup))
        count += len(page.links)
        urls.update(url for _, _, url in page.links if urlsplit(url).scheme in ('http', 'https'))
    return errors, sorted(urls), count


def http_outcome(status, original, final):
    if urlsplit(final).hostname == 'github.com' and urlsplit(final).path in ('/login', '/session', '/sessions/two-factor'):
        return 'review'
    if status in (404, 410):
        return 'unavailable'
    return 'ok' if 200 <= status < 300 else 'review'


def check_http(url):
    request = Request(url, headers={'User-Agent': 'Tangopad-documentation-review/1.0'})
    try:
        with urlopen(request, timeout=15) as response:
            return {'url': url, 'finalUrl': response.url, 'status': response.status,
                    'result': http_outcome(response.status, url, response.url)}
    except HTTPError as error:
        return {'url': url, 'finalUrl': error.url, 'status': error.code,
                'result': http_outcome(error.code, url, error.url)}
    except (URLError, TimeoutError, OSError) as error:
        return {'url': url, 'result': 'review', 'error': str(error)}


def render(path):
    source = path.read_bytes()
    payload = json.dumps({'text': source.decode('utf-8'), 'mode': 'gfm', 'context': 'daniel-kindl/tangopad'})
    result = subprocess.run(['gh', 'api', 'markdown', '--method', 'POST', '--input', '-'],
                            input=payload, text=True, capture_output=True, timeout=60)
    if result.returncode:
        raise RuntimeError(f'GitHub rendering failed for {path.name}: {result.stderr.strip()}')
    return path, hashlib.sha256(source).hexdigest(), result.stdout


class PreviewHTML(HTMLParser):
    def __init__(self, rendered, destination):
        super().__init__(convert_charrefs=False)
        self.parts = []
        self.destination = destination
        self.ids = iter(CHECKER['heading_slugs']([label for _, label in Page(rendered).headings]))
        self.feed(rendered)

    def handle_starttag(self, tag, attrs):
        text = self.get_starttag_text()
        if re.fullmatch(r'h[1-6]', tag):
            slug = next(self.ids)
            if 'id' not in dict(attrs):
                text = text[:-1] + f' id="{escape(slug, quote=True)}">'
        self.parts.append(re.sub(r'(href|src)="([^"]*)"', self.destination, text))

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)

    def handle_endtag(self, tag):
        self.parts.append(f'</{tag}>')

    def handle_data(self, text):
        self.parts.append(text)

    def handle_entityref(self, name):
        self.parts.append(f'&{name};')

    def handle_charref(self, name):
        self.parts.append(f'&#{name};')

    def handle_comment(self, text):
        self.parts.append(f'<!--{text}-->')


def preview(root, path, rendered, output, pages):

    def destination(match):
        url = urlsplit(unescape(match[2]))
        if url.scheme or url.netloc or not url.path:
            return match[0]
        target = (path.parent / unquote(url.path)).resolve()
        if target in pages:
            target = output / target.relative_to(root).with_suffix('.html')
        address = target.as_uri() + (f'#{url.fragment}' if url.fragment else '')
        return f'{match[1]}="{escape(address, quote=True)}"'

    rendered = ''.join(PreviewHTML(rendered, destination).parts)
    base = escape(path.parent.as_uri() + '/', quote=True)
    title = escape(str(path.relative_to(root)))
    return (f'<!doctype html><meta charset="utf-8"><title>{title}</title><base href="{base}">'
            '<style>body{max-width:72rem;margin:2rem auto;padding:0 1rem;font:16px/1.5 sans-serif}'
            'img{max-width:100%;height:auto}table{border-collapse:collapse}'
            'td,th{border:1px solid #aaa;padding:.4rem}pre{overflow:auto;background:#eee;padding:1rem}</style>'
            f'<p>Review copy of {title}. GitHub rendered the content. This local style is not GitHub page chrome.</p>'
            + rendered)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('pages', nargs='*', type=Path, help='Markdown paths relative to the repository root.')
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/docs-review')
    parser.add_argument('--external', action='store_true', help='Request each rendered HTTP link. Network failures require review.')
    args = parser.parse_args()
    paths = args.pages or [Path(name) for name in DEFAULTS] + sorted((ROOT / 'docs/user').glob('*.md'))
    paths = sorted({(ROOT / path).resolve() for path in paths})
    if any(not path.is_relative_to(ROOT) or path.suffix != '.md' or not path.is_file() for path in paths):
        parser.error('Select Markdown files inside the repository.')
    args.output.mkdir(parents=True, exist_ok=True)
    args.output = args.output.resolve()
    report = {'checkedAtUtc': datetime.now(timezone.utc).isoformat(),
              'sourceCommit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(),
              'sourceDirty': bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=ROOT, text=True).strip()),
              'renderer': 'GitHub Markdown API, gfm', 'pages': [], 'external': [],
              'externalRequested': args.external,
              'limits': ['No native package or release-candidate acceptance.',
                         'No visual, screen-reader, or browser-layout acceptance.',
                         'HTTP status does not establish destination accuracy or external fragment validity.',
                         'Local preview styling differs from the GitHub page.']}
    try:
        with ThreadPoolExecutor(max_workers=4) as workers:
            results = list(workers.map(render, paths))
        rendered = {path: text for path, _, text in results}
        source_hashes = {path: digest for path, digest, _ in results}
        for path, text in rendered.items():
            relative = path.relative_to(ROOT)
            destination = args.output / relative.with_suffix('.html')
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(preview(ROOT, path, text, args.output, rendered), encoding='utf-8')
            report['pages'].append({'path': str(relative), 'sourceSha256': source_hashes[path],
                                    'renderedSha256': hashlib.sha256(text.encode()).hexdigest(),
                                    'previewSha256': hashlib.sha256(destination.read_bytes()).hexdigest()})
        errors, urls, count = check_rendered(ROOT, rendered)
        report.update(errors=errors, renderedLinks=count)
        if args.external:
            with ThreadPoolExecutor(max_workers=4) as workers:
                report['external'] = list(workers.map(check_http, urls))
        incomplete = any(item['result'] != 'ok' for item in report['external'])
        report['result'] = 'INCOMPLETE' if errors or incomplete else 'STRUCTURAL CHECKS PASSED'
    except (OSError, RuntimeError, ValueError, subprocess.TimeoutExpired) as error:
        report.update(result='INCOMPLETE', errors=[str(error)])
    (args.output / 'result.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(f"{report['result']}: {len(report['pages'])} rendered pages. Report: {args.output / 'result.json'}")
    for error in report.get('errors', []):
        print(f'ERROR: {error}')
    for item in report['external']:
        if item['result'] != 'ok':
            print(f"REVIEW: {item['url']} ({item.get('status', item.get('error'))})")
    return 1 if report['result'] == 'INCOMPLETE' else 0


if __name__ == '__main__':
    sys.exit(main())
