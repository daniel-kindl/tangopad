#!/usr/bin/env python3
"""Check release-note validation with isolated repository and artifact fixtures."""

from pathlib import Path
import runpy
import tempfile
import unittest

MODULE = runpy.run_path(str(Path(__file__).with_name('release-notes.py')))

NOTES = '''# Tangopad 0.1.0

Status: Draft

## Summary

A local text editor.

## Highlights

- Edit UTF-8 text.

## Breaking changes and migration

No prior public release exists.

## Installation and updates

Read the installation guide.

## Downloads

<!-- release-assets -->

## Verification and trust

Review authenticated package inventories.

## Known limitations

Native platform acceptance is pending.
'''


class ReleaseNotesTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / 'eng/release').mkdir(parents=True)
        (self.root / 'Directory.Build.props').write_text('<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>')
        self.history = self.root / 'CHANGELOG.md'
        self.history.write_text('# Changelog\n\n## Unreleased\n\n### Added\n\n- Edit UTF-8 text.\n')
        self.notes = self.root / 'eng/release/notes.md'
        self.notes.write_text(NOTES)

    def validate(self, publication=False):
        return MODULE['validate'](self.root, publication=publication)

    def prepare(self):
        self.history.write_text(self.history.read_text() + '\n[Unreleased comparison](https://github.com/daniel-kindl/tangopad/compare/v0.1.0...dev)\n\n## 0.1.0 - 2026-01-01\n\n### Added\n\n- Edit UTF-8 text.\n\n[Release](https://github.com/daniel-kindl/tangopad/releases/tag/v0.1.0)\n')
        self.notes.write_text(NOTES.replace('Status: Draft', 'Status: Ready for release review').replace(
            'A local text editor.', 'A local text editor. [Changelog](https://github.com/daniel-kindl/tangopad/blob/v0.1.0/CHANGELOG.md).'))

    def test_unreleased_draft_validates_without_claiming_publication(self):
        self.assertEqual('0.1.0', self.validate())

    def test_publication_rejects_draft(self):
        with self.assertRaisesRegex(ValueError, 'Ready for release review'):
            self.validate(publication=True)

    def test_ready_notes_require_versioned_history(self):
        self.prepare()
        self.history.write_text('# Changelog\n\n## Unreleased\n\n### Added\n\n- Edit UTF-8 text.\n')
        with self.assertRaisesRegex(ValueError, '0.1.0.*changelog'):
            self.validate(publication=True)

    def test_publication_rejects_mutable_documentation_links(self):
        self.prepare()
        self.notes.write_text(self.notes.read_text().replace('/blob/v0.1.0/', '/blob/dev/'))
        with self.assertRaisesRegex(ValueError, 'version-tagged'):
            self.validate(publication=True)

    def test_prepared_release_validates(self):
        self.prepare()
        self.assertEqual('0.1.0', self.validate(publication=True))

    def test_wrong_note_version_is_rejected(self):
        self.notes.write_text(NOTES.replace('Tangopad 0.1.0', 'Tangopad 0.2.0'))
        with self.assertRaisesRegex(ValueError, 'authoritative version'):
            self.validate()

    def test_missing_section_is_rejected(self):
        self.notes.write_text(NOTES.replace('## Known limitations', '## Unchecked'))
        with self.assertRaisesRegex(ValueError, 'Known limitations'):
            self.validate()

    def test_empty_changelog_category_is_rejected(self):
        self.history.write_text('# Changelog\n\n## Unreleased\n\n### Added\n')
        with self.assertRaisesRegex(ValueError, 'empty'):
            self.validate()

    def test_future_date_is_rejected(self):
        self.prepare()
        self.history.write_text(self.history.read_text().replace('2026-01-01', '9999-01-01'))
        with self.assertRaisesRegex(ValueError, 'future'):
            self.validate()

    def test_invalid_date_is_rejected(self):
        self.prepare()
        self.history.write_text(self.history.read_text().replace('2026-01-01', '2026-02-30'))
        with self.assertRaisesRegex(ValueError, 'date'):
            self.validate()

    def test_duplicate_release_is_rejected(self):
        self.prepare()
        block = self.history.read_text().split('## 0.1.0')[1]
        self.history.write_text(self.history.read_text() + '\n[Unreleased comparison](https://github.com/daniel-kindl/tangopad/compare/v0.1.0...dev)\n\n## 0.1.0' + block)
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            self.validate()

    def test_comparison_uses_adjacent_versions(self):
        self.prepare()
        old = '\n## 0.0.1 - 2025-12-01\n\n### Added\n\n- Initial behavior.\n\n[Release](https://github.com/daniel-kindl/tangopad/releases/tag/v0.0.1)\n'
        self.history.write_text(self.history.read_text() + old)
        with self.assertRaisesRegex(ValueError, 'comparison'):
            self.validate()

    def test_dry_run_preserves_draft_and_has_no_invented_assets(self):
        rendered = MODULE['render'](self.root)
        self.assertIn('Status: Draft', rendered)
        self.assertIn('No production artifacts are supplied', rendered)
        self.assertNotIn('/releases/download/', rendered)

    def test_asset_links_and_hashes_come_from_real_files(self):
        self.prepare()
        artifact = self.root / 'Tangopad-0.1.0-linux-x64.AppImage'
        artifact.write_bytes(b'abc')
        rendered = MODULE['render'](self.root, [artifact])
        self.assertIn('https://github.com/daniel-kindl/tangopad/releases/download/v0.1.0/Tangopad-0.1.0-linux-x64.AppImage', rendered)
        self.assertIn('ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad', rendered)
        self.assertNotIn('<!-- release-assets -->', rendered)
        self.assertNotIn('Status:', rendered)

    def test_duplicate_asset_names_are_rejected(self):
        artifact = self.root / 'same.zip'
        artifact.write_bytes(b'abc')
        with self.assertRaisesRegex(ValueError, 'Duplicate asset'):
            MODULE['render'](self.root, [artifact, artifact])


if __name__ == '__main__':
    unittest.main()
