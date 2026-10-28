#!/usr/bin/env python3
"""Check the boundary between update-test packages and production packages."""

import io
import os
from pathlib import Path
import re
import runpy
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile

ENG = Path(__file__).resolve().parent
ROOT = ENG.parent
MARKER = 'Tangopad.UpdateTestHook.v1'


def scanner():
    return runpy.run_path(str(ENG / 'update-test-marker.py'))


def zipped(entries, compression=zipfile.ZIP_DEFLATED):
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, 'w', compression) as archive:
        for name, content in entries.items():
            archive.writestr(name, content)
    return buffer.getvalue()


def assembly(text, encoding):
    return b'MZ\x90\x00' + os.urandom(64) + text.encode(encoding) + os.urandom(64)


class MarkerScan(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)

    def package(self, name, payload):
        path = self.directory / name
        path.write_bytes(payload)
        return path

    def test_finds_the_marker_in_a_deflated_nupkg(self):
        for encoding in ('utf-8', 'utf-16-le'):
            payload = zipped({'lib/app/Editor.App': assembly(MARKER * 4, encoding)})
            self.assertNotIn(MARKER.encode(encoding), payload, 'The fixture must hide the marker behind deflate.')
            package = self.package(f'test-{encoding}.nupkg', payload)
            self.assertTrue(scanner()['contains_marker'](package), encoding)
            scanner()['require_present'](package)

    def test_finds_the_marker_in_a_nested_archive(self):
        inner = zipped({'Editor.App.dll': assembly(MARKER, 'utf-16-le')})
        package = self.package('nested.zip', zipped({'payload/inner.zip': inner}))
        self.assertTrue(scanner()['contains_marker'](package))

    def test_finds_the_marker_in_raw_bytes(self):
        self.assertTrue(scanner()['contains_marker'](self.package('Editor.App', assembly(MARKER, 'utf-8'))))

    def test_rejects_a_production_nupkg_that_contains_the_marker(self):
        package = self.package('TangopadDesktop-1.0.0-linux-x64-full.nupkg',
                               zipped({'lib/app/Editor.App': assembly(MARKER, 'utf-16-le')}))
        with self.assertRaisesRegex(ValueError, 'update-test'):
            scanner()['require_absent']([package])

    def test_accepts_a_production_nupkg_without_the_marker(self):
        package = self.package('TangopadDesktop-1.0.0-linux-x64-full.nupkg',
                               zipped({'lib/app/Editor.App': assembly('Tangopad.UpdateTestHook', 'utf-16-le')}))
        self.assertFalse(scanner()['contains_marker'](package))
        scanner()['require_absent']([package])
        with self.assertRaisesRegex(ValueError, 'positive control'):
            scanner()['require_present'](package)


class PackageReleaseArguments(unittest.TestCase):
    def refused(self, *arguments, message):
        environment = {key: value for key, value in os.environ.items() if key != 'TANGOPAD_RELEASE_BUILD'}
        completed = subprocess.run([sys.executable, str(ENG / 'package-release.py'), '--rid', 'linux-x64', *arguments],
                                   cwd=ROOT, env=environment, capture_output=True, text=True, timeout=120, check=False)
        self.assertNotEqual(0, completed.returncode)
        self.assertRegex(completed.stderr, message)
        self.assertFalse((ROOT / 'artifacts/update-test/linux-x64/9.9.9').exists())

    def test_update_test_is_refused_with_production(self):
        self.refused('--production', '--update-test', message='update-test')

    def test_update_key_dir_is_refused_with_production(self):
        self.refused('--production', '--update-key-dir', tempfile.gettempdir(), message='update-test')

    def test_update_key_dir_requires_update_test(self):
        self.refused('--update-key-dir', tempfile.gettempdir(), '--test-version', '9.9.9', message='update-test')

    def test_update_test_is_refused_in_a_release_build(self):
        environment = {**os.environ, 'TANGOPAD_RELEASE_BUILD': '1'}
        completed = subprocess.run([sys.executable, str(ENG / 'package-release.py'), '--rid', 'linux-x64', '--update-test',
                                    '--test-version', '9.9.9'], cwd=ROOT, env=environment, capture_output=True, text=True,
                                   timeout=120, check=False)
        self.assertNotEqual(0, completed.returncode)
        self.assertRegex(completed.stderr, 'TANGOPAD_RELEASE_BUILD')


class Workflows(unittest.TestCase):
    def job(self, workflow, name):
        text = (ROOT / '.github/workflows' / workflow).read_text(encoding='utf-8')
        match = re.search(rf'^  {name}:\n((?:    .*\n|\s*\n)*)', text, re.MULTILINE)
        self.assertIsNotNone(match, f'{workflow} has no {name} job')
        return match.group(1)

    def test_release_package_job_sets_the_release_build_variable_at_job_level(self):
        job = self.job('release.yml', 'package')
        self.assertRegex(job, r'(?m)^    env:\n(?:      .*\n)*?      TANGOPAD_RELEASE_BUILD: "1"\n')

    def test_release_workflow_never_builds_update_test_packages(self):
        text = (ROOT / '.github/workflows/release.yml').read_text(encoding='utf-8')
        for forbidden in ('--update-test', '--update-key-dir', 'TangopadUpdateTest', 'update-test'):
            self.assertNotIn(forbidden, text)

    def test_update_test_evidence_uses_its_own_short_lived_artifact(self):
        text = (ROOT / '.github/workflows/packages.yml').read_text(encoding='utf-8')
        uploads = re.findall(r'name: (update-test-[^\n]*)\n\s+path: ([^\n]*)\n(?:\s+[a-z-]+: [^\n]*\n)*?\s+retention-days: (\d+)', text)
        self.assertEqual(1, len(uploads), 'One update-test upload is required.')
        name, path, retention = uploads[0]
        self.assertEqual('update-test-${{ matrix.rid }}', name)
        self.assertTrue(path.startswith('artifacts/update-test'), path)
        self.assertLessEqual(int(retention), 3)
        candidates = re.search(r'name: candidate-\$\{\{ matrix.rid \}\}\n\s+path: ([^\n]*)', text).group(1)
        self.assertNotIn('update-test', candidates)
        self.assertIn('artifacts/release', candidates)


@unittest.skipUnless(shutil.which('dotnet'), 'The .NET SDK is required for the MSBuild guard checks.')
class BuildGuard(unittest.TestCase):
    def guard(self, *properties, environment=None):
        env = {key: value for key, value in os.environ.items() if key != 'TANGOPAD_RELEASE_BUILD'}
        env.update(environment or {})
        return subprocess.run(['dotnet', 'msbuild', 'src/Editor.App/Editor.App.csproj', '-nologo', '-t:TangopadUpdateTestGuard',
                               *properties], cwd=ROOT, env=env, capture_output=True, text=True, timeout=300, check=False)

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.test_key = Path(self.temporary.name) / 'test-public.pem'
        subprocess.run(['openssl', 'ecparam', '-name', 'prime256v1', '-genkey', '-noout', '-out', str(self.test_key) + '.key'], check=True)
        subprocess.run(['openssl', 'ec', '-in', str(self.test_key) + '.key', '-pubout', '-out', str(self.test_key)], check=True,
                       capture_output=True)

    def test_test_build_with_a_test_key_passes_the_guard(self):
        completed = self.guard('-p:TangopadUpdateTest=true', f'-p:UpdatePublicKeyPath={self.test_key}')
        self.assertEqual(0, completed.returncode, completed.stdout)

    def test_test_build_with_the_default_key_fails(self):
        completed = self.guard('-p:TangopadUpdateTest=true')
        self.assertNotEqual(0, completed.returncode)
        self.assertIn('TANGOPAD002', completed.stdout)

    def test_test_build_with_the_production_key_fails(self):
        completed = self.guard('-p:TangopadUpdateTest=true', f'-p:UpdatePublicKeyPath={ROOT / "eng/release/update-public-key.pem"}')
        self.assertNotEqual(0, completed.returncode)
        self.assertIn('TANGOPAD002', completed.stdout)

    def test_test_build_with_a_copy_of_the_production_key_fails(self):
        copy = Path(self.temporary.name) / 'copy.pem'
        shutil.copyfile(ROOT / 'eng/release/update-public-key.pem', copy)
        completed = self.guard('-p:TangopadUpdateTest=true', f'-p:UpdatePublicKeyPath={copy}')
        self.assertNotEqual(0, completed.returncode)
        self.assertIn('TANGOPAD002', completed.stdout)

    def test_test_build_in_a_release_build_fails(self):
        completed = self.guard('-p:TangopadUpdateTest=true', f'-p:UpdatePublicKeyPath={self.test_key}',
                               environment={'TANGOPAD_RELEASE_BUILD': '1'})
        self.assertNotEqual(0, completed.returncode)
        self.assertIn('TANGOPAD001', completed.stdout)


if __name__ == '__main__':
    unittest.main()
