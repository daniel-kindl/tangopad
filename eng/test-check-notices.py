#!/usr/bin/env python3
"""Check dependency notice coverage and actual copied payload bytes."""

import hashlib
import json
from pathlib import Path
import runpy
import shutil
import tempfile
import unittest
import zipfile

CHECK = runpy.run_path(str(Path(__file__).with_name('check-notices.py')))


class NoticeChecks(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        for directory in ('eng', 'docs/licenses', 'src/Editor.App', 'publish/licenses', 'cache/microsoft.netcore.app.runtime.linux-x64/10.0.13'):
            (self.root / directory).mkdir(parents=True)
        (self.root / 'docs/licenses/A.txt').write_bytes(b'Original license text.\n')
        self.policy = {'schemaVersion': 1, 'groups': {'a': {'packages': {'A': '1.0.0'}, 'files': ['A.txt']}},
                       'additionalFiles': [], 'files': {'A.txt': {'sha256': hashlib.sha256(b'Original license text.\n').hexdigest(),
                                                               'source': {'kind': 'fixture'}}}}
        self.manifest = self.root / 'eng/notices.json'
        self.save_policy()
        self.lock = self.root / 'src/Editor.App/packages.lock.json'
        self.lock.write_text(json.dumps({'dependencies': {'net10.0': {'A': {'type': 'Direct', 'resolved': '1.0.0'}}}}))
        self.payload = self.root / 'publish'
        shutil.copyfile(self.root / 'docs/licenses/A.txt', self.payload / 'licenses/A.txt')
        package = self.root / 'cache/microsoft.netcore.app.runtime.linux-x64/10.0.13'
        (package / 'LICENSE.TXT').write_bytes(b'Runtime license.\n')
        (package / 'THIRD-PARTY-NOTICES.TXT').write_bytes(b'Runtime third-party notices.\n')
        self.assets = self.root / 'assets.json'
        self.assets.write_text(json.dumps({'packageFolders': {str(self.root / 'cache'): {}}, 'project': {'frameworks': {'net10.0': {
            'downloadDependencies': [{'name': 'Microsoft.NETCore.App.Runtime.linux-x64', 'version': '[10.0.13, 10.0.13]'}]}}}}))

    def save_policy(self):
        self.manifest.write_text(json.dumps(self.policy))
        shutil.copyfile(self.manifest, self.root / 'publish/licenses/notices.json')

    def prepare(self):
        return CHECK['prepare_payload'](self.root, self.payload, 'linux-x64', self.assets)

    def test_reviewed_graph_and_text_validate(self):
        self.assertEqual({'A': '1.0.0'}, CHECK['validate'](self.root)['groups']['a']['packages'])

    def test_new_dependency_requires_review(self):
        data = json.loads(self.lock.read_text())
        data['dependencies']['net10.0']['B'] = {'type': 'Transitive', 'resolved': '2.0.0'}
        self.lock.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, 'Missing packages'):
            CHECK['validate'](self.root)

    def test_version_change_requires_review(self):
        self.lock.write_text(self.lock.read_text().replace('1.0.0', '1.0.1'))
        with self.assertRaisesRegex(ValueError, 'Unreviewed versions'):
            CHECK['validate'](self.root)

    def test_changed_legal_text_is_rejected(self):
        (self.root / 'docs/licenses/A.txt').write_bytes(b'Changed text.\n')
        with self.assertRaisesRegex(ValueError, 'reviewed notice changed'):
            CHECK['validate'](self.root)

    def test_missing_publish_notice_is_rejected(self):
        (self.payload / 'licenses/A.txt').unlink()
        with self.assertRaisesRegex(ValueError, 'publish output is missing'):
            self.prepare()

    def test_runtime_notices_are_copied_verbatim(self):
        self.prepare()
        self.assertEqual(b'Runtime license.\n', (self.payload / 'licenses/Dotnet.LICENSE.txt').read_bytes())
        self.assertEqual(b'Runtime third-party notices.\n', (self.payload / 'licenses/Dotnet.NOTICES.txt').read_bytes())
        self.assertEqual('10.0.13', CHECK['verify_payload'](self.root, self.payload, 'linux-x64')['runtimePack']['version'])

    def test_missing_runtime_notice_is_rejected(self):
        (self.root / 'cache/microsoft.netcore.app.runtime.linux-x64/10.0.13/THIRD-PARTY-NOTICES.TXT').unlink()
        with self.assertRaisesRegex(ValueError, 'runtime pack is missing'):
            self.prepare()

    def test_changed_distributed_notice_is_rejected(self):
        self.prepare()
        (self.payload / 'licenses/Dotnet.NOTICES.txt').write_bytes(b'Changed notices.')
        with self.assertRaisesRegex(ValueError, 'distributed notice changed'):
            CHECK['verify_payload'](self.root, self.payload, 'linux-x64')

    def test_removing_runtime_inventory_entries_is_rejected(self):
        self.prepare()
        path = self.payload / 'licenses/distribution.json'
        data = json.loads(path.read_text())
        data['files'].pop('Dotnet.NOTICES.txt')
        path.write_text(json.dumps(data))
        with self.assertRaisesRegex(ValueError, 'inventory is incomplete'):
            CHECK['verify_payload'](self.root, self.payload, 'linux-x64')

    def test_wrong_runtime_is_rejected(self):
        self.prepare()
        with self.assertRaisesRegex(ValueError, 'does not match'):
            CHECK['verify_payload'](self.root, self.payload, 'osx-arm64')

    def archive(self, missing=None):
        expected = self.prepare()
        path = self.root / 'bundle.zip'
        with zipfile.ZipFile(path, 'w') as archive:
            for file in (self.payload / 'licenses').iterdir():
                if file.name != missing:
                    archive.write(file, 'Tangopad.app/Contents/MacOS/licenses/' + file.name)
        return path, expected

    def test_mac_bundle_archive_preserves_notice_bytes(self):
        archive, expected = self.archive()
        CHECK['verify_zip'](archive, expected)
        with zipfile.ZipFile(archive) as source:
            self.assertEqual(b'Original license text.\n', source.read('Tangopad.app/Contents/MacOS/licenses/A.txt'))

    def test_archive_missing_a_notice_is_rejected(self):
        archive, expected = self.archive(missing='Dotnet.NOTICES.txt')
        with self.assertRaisesRegex(ValueError, 'archive is missing'):
            CHECK['verify_zip'](archive, expected)


if __name__ == '__main__':
    unittest.main()
