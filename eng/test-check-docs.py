#!/usr/bin/env python3
"""Check documentation validation with valid and broken repository fixtures."""

import hashlib
import json
from pathlib import Path
import runpy
import struct
import tempfile
import unittest

CHECKER = runpy.run_path(str(Path(__file__).with_name('check-docs.py')))


class DocumentationChecks(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.page = self.root / 'README.md'
        self.page.write_text('# Start\n\n[Next](next.md#repeat-1)\n')
        (self.root / 'next.md').write_text('# Repeat\n\n# Repeat\n')

    def errors(self):
        return CHECKER['check'](self.root, [self.page])[0]

    def test_valid_links_and_duplicate_heading(self):
        self.assertEqual([], self.errors())

    def test_missing_file(self):
        self.page.write_text('[Missing](missing.md)')
        self.assertEqual(['README.md: Missing local target: missing.md'], self.errors())

    def test_missing_heading(self):
        self.page.write_text('[Missing](next.md#absent)')
        self.assertEqual(['README.md: Missing heading: next.md#absent'], self.errors())

    def test_image_without_description(self):
        (self.root / 'image.png').touch()
        self.page.write_text('![](image.png)')
        self.assertEqual(['README.md: The image requires alt text.'], self.errors())

    def test_external_links_and_fenced_examples(self):
        self.page.write_text('[Web](https://example.com/path)\n```md\n[Example](absent.md)\n```\n')
        self.assertEqual([], self.errors())

    def test_repository_escape(self):
        self.page.write_text('[Outside](../outside.md)')
        self.assertEqual(['README.md: Missing local target: ../outside.md'], self.errors())

    def media_fixture(self):
        media = self.root / 'docs/user/media'
        media.mkdir(parents=True)
        image = media / 'sample.png'
        # Header-only fixture tests metadata validation. Real captures use Pillow.
        image.write_bytes(b'\x89PNG\r\n\x1a\n' + struct.pack('>I', 13) + b'IHDR' + struct.pack('>II', 1040, 760))
        sample = self.root / 'sample.md'
        sample.write_text('# Sample\n')
        record = {field: 'fixture' for field in ('sourceCommit', 'sourceDiffSha256', 'executableSha256', 'assemblySha256', 'host', 'architecture', 'session')}
        record.update(sample='sample.md', sampleSha256=hashlib.sha256(sample.read_bytes()).hexdigest(),
                      images=[{'file': 'sample.png', 'width': 1040, 'height': 760,
                               'sha256': hashlib.sha256(image.read_bytes()).hexdigest()}])
        manifest = media / 'capture.json'
        manifest.write_text(json.dumps(record))
        return image, sample, record, manifest

    def test_image_and_sample_provenance(self):
        image, sample, _, _ = self.media_fixture()
        self.assertEqual([], self.errors())
        image.write_bytes(image.read_bytes() + b'changed')
        sample.write_text('# Changed\n')
        self.assertEqual(['capture.json: The sample changed. Repeat the capture.',
                          'sample.png: The image changed. Repeat the capture review.'], self.errors())

    def test_image_dimensions(self):
        _, _, record, manifest = self.media_fixture()
        record['images'][0]['width'] = 999
        manifest.write_text(json.dumps(record))
        self.assertEqual(['sample.png: Dimensions differ from the capture record.'], self.errors())

    def test_unrecorded_image(self):
        image, _, _, _ = self.media_fixture()
        image.with_name('extra.png').write_bytes(image.read_bytes())
        self.assertEqual(['capture.json: The PNG inventory does not match the files.'], self.errors())


if __name__ == '__main__':
    unittest.main()
