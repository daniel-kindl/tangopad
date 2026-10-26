#!/usr/bin/env python3
"""Check rendered-document validation with literal HTML and HTTP result fixtures."""

from pathlib import Path
import runpy
import tempfile
import unittest

REVIEW = runpy.run_path(str(Path(__file__).with_name('review-docs.py')))


class RenderedDocumentationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.start = self.root / 'README.md'
        self.next = self.root / 'next.md'
        self.start.write_text('# Start\n')
        self.next.write_text('# A linked heading\n')

    def check(self, html, destination='<h1>A linked heading</h1>'):
        return REVIEW['check_rendered'](self.root, {self.start: html, self.next: destination})

    def test_rendered_reference_link_checks_its_destination(self):
        errors, _, _ = self.check('<h1>Start</h1><p><a href="missing.md">Guide</a></p>')
        self.assertEqual(['README.md: Missing local target: missing.md'], errors)

    def test_heading_label_excludes_link_destination(self):
        errors, _, _ = self.check('<h1>Start</h1><a href="next.md#a-linked-heading">Next</a>',
                                  '<h1>A <a href="README.md">linked</a> heading</h1>')
        self.assertEqual([], errors)

    def test_entity_and_nested_heading_text(self):
        errors, _, _ = self.check('<h1>Start</h1><a href="next.md#save--close">Next</a>',
                                  '<h1>Save &amp; <em>close</em></h1>')
        self.assertEqual([], errors)

    def test_missing_heading_is_reported(self):
        errors, _, _ = self.check('<h1>Start</h1><a href="next.md#absent">Next</a>')
        self.assertEqual(['README.md: Missing heading: next.md#absent'], errors)

    def test_heading_collision_gets_a_unique_anchor(self):
        errors, _, _ = self.check('<h1>Start</h1><a href="next.md#topic-1-1">Next</a>',
                                  '<h1>Topic</h1><h2>Topic</h2><h2>Topic-1</h2>')
        self.assertEqual([], errors)

    def test_preview_navigation_opens_rendered_pages(self):
        text = '<h1>Start</h1><a href="next.md#a-linked-heading">Next</a>'
        output = self.root / 'preview'
        preview = REVIEW['preview'](self.root, self.start, text, output, {self.next})
        self.assertIn('id="start"', preview)
        self.assertIn(f'href="{(output / "next.html").as_uri()}#a-linked-heading"', preview)

    def test_preview_preserves_literal_links_in_code_examples(self):
        text = '<h1>Start</h1><pre><code>&lt;a href="missing.md"&gt;Sample&lt;/a&gt;</code></pre>'
        preview = REVIEW['preview'](self.root, self.start, text, self.root / 'preview', set())
        self.assertIn('&lt;a href="missing.md"&gt;Sample&lt;/a&gt;', preview)

    def test_blank_image_description_is_reported(self):
        (self.root / 'image.png').touch()
        errors, _, _ = self.check('<h1>Start</h1><img src="image.png" alt=" ">')
        self.assertEqual(['README.md: The image requires alt text.'], errors)

    def test_code_samples_do_not_create_links(self):
        errors, _, _ = self.check('<h1>Start</h1><pre><code>&lt;a href="missing.md"&gt;Sample&lt;/a&gt;</code></pre>')
        self.assertEqual([], errors)

    def test_remote_links_are_deduplicated(self):
        errors, urls, _ = self.check('<h1>Start</h1><a href="https://example.com/">One</a><a href="https://example.com/">Two</a>')
        self.assertEqual([], errors)
        self.assertEqual(['https://example.com/'], urls)

    def test_missing_page_title_is_reported(self):
        errors, _, _ = self.check('<h2>Start</h2>')
        self.assertEqual(['README.md: Expected one rendered level-one heading.'], errors)

    def test_heading_level_skip_is_reported(self):
        errors, _, _ = self.check('<h1>Start</h1><h3>Details</h3>')
        self.assertEqual(['README.md: Heading level jumps from 1 to 3.'], errors)

    def test_http_not_found_is_unavailable(self):
        self.assertEqual('unavailable', REVIEW['http_outcome'](404, 'https://example.com/', 'https://example.com/'))

    def test_rate_limit_requires_review(self):
        self.assertEqual('review', REVIEW['http_outcome'](429, 'https://example.com/', 'https://example.com/'))

    def test_login_redirect_requires_review(self):
        self.assertEqual('review', REVIEW['http_outcome'](200, 'https://github.com/example/repo', 'https://github.com/login?return_to=example'))

    def test_successful_redirect_is_recorded_as_ok(self):
        self.assertEqual('ok', REVIEW['http_outcome'](200, 'http://example.com/', 'https://example.com/'))


if __name__ == '__main__':
    unittest.main()
