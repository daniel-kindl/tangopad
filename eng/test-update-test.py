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

    def test_release_gate_runs_first_with_read_only_actions_access(self):
        job = self.job('release.yml', 'gate')
        self.assertNotIn('needs:', job)
        self.assertIn("if: github.ref == 'refs/heads/main'", job)
        self.assertRegex(job, r'(?m)^    permissions:\n      actions: read\n      contents: read\n')
        self.assertIn('python3 eng/check-release-gate.py --repo "${{ github.repository }}" --sha "${{ github.sha }}"', job)
        self.assertIn('GH_TOKEN: ${{ github.token }}', job)
        self.assertNotIn('continue-on-error', job)

    def test_every_main_commit_gets_a_real_update_run_for_the_gate(self):
        text = (ROOT / '.github/workflows/packages.yml').read_text(encoding='utf-8')
        trigger = re.search(r'(?m)^  push:\n((?:    .*\n)*)', text.split('\njobs:\n', 1)[0])
        self.assertIsNotNone(trigger, 'packages.yml must run on a push to main.')
        self.assertEqual('    branches: [main]\n', trigger.group(1), 'The push trigger must have no path filter.')
        self.assertIn('WORKFLOW = ".github/workflows/packages.yml"', (ENG / 'check-release-gate.py').read_text(encoding='utf-8'))

    def test_every_release_job_depends_on_the_gate(self):
        text = (ROOT / '.github/workflows/release.yml').read_text(encoding='utf-8')
        names = re.findall(r'(?m)^  ([a-z-]+):\n', text.split('\njobs:\n', 1)[1])
        self.assertIn('gate', names)
        needs = {}
        for name in names:
            found = re.search(r'(?m)^    needs: (\[[^\]]*\]|[a-z-]+)$', self.job('release.yml', name))
            needs[name] = set(re.findall(r'[a-z-]+', found.group(1))) if found else set()
        self.assertIn('gate', needs['package'])

        def reaches_gate(name, seen=()):
            return name == 'gate' or any(reaches_gate(parent, seen + (name,)) for parent in needs[name] if parent not in seen)
        for name in names:
            self.assertTrue(reaches_gate(name), f'{name} does not depend on the gate')

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


APPIMAGE_HEAD = b'\x7fELF\x02\x01\x01\x00AI\x02' + b'\x00' * 53


class AppImageScan(unittest.TestCase):
    """The Linux package keeps the app in a compressed AppImage. The scan extracts it and fails closed."""

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.module = scanner()
        self.globals = self.module['contains_marker'].__globals__
        self.globals['platform_system'] = lambda: 'Linux'
        self.globals['sudo_available'] = lambda: True
        self.calls = []

    def fake(self, files=None, error=None):
        def extract(image, destination, timeout):
            self.calls.append((Path(image), Path(destination), timeout))
            if error is not None:
                raise error
            for name, content in (files or {}).items():
                path = Path(destination) / 'squashfs-root' / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(content)
        self.globals['run_extractor'] = extract

    def appimage(self, name='TangopadDesktop-linux-x64.AppImage'):
        path = self.directory / name
        path.write_bytes(APPIMAGE_HEAD + os.urandom(256))
        return path

    def test_positive_control_extracts_a_test_appimage_and_finds_the_marker(self):
        self.fake({'usr/bin/Editor.App': assembly(MARKER, 'utf-16-le')})
        image = self.appimage()
        self.module['require_present'](image)
        self.assertEqual(1, len(self.calls))
        self.assertNotEqual(image.parent, self.calls[0][0].parent, 'The scan extracts a copy in a fresh directory.')
        self.assertGreater(self.calls[0][2], 0)

    def test_production_appimage_without_the_marker_passes(self):
        self.fake({'usr/bin/Editor.App': assembly('Tangopad.UpdateTestHook', 'utf-16-le'), 'AppRun': b'run'})
        self.module['require_absent']([self.appimage()])
        self.assertEqual(1, len(self.calls))

    def test_production_appimage_with_the_marker_fails(self):
        self.fake({'usr/bin/Editor.App': assembly(MARKER, 'utf-8')})
        with self.assertRaisesRegex(ValueError, 'update-test marker'):
            self.module['require_absent']([self.appimage()])

    def test_appimage_inside_a_nupkg_is_extracted(self):
        self.fake({'usr/bin/Editor.App': assembly(MARKER, 'utf-16-le')})
        package = self.directory / 'TangopadDesktop-1.0.0-linux-x64-full.nupkg'
        package.write_bytes(zipped({'lib/app/TangopadDesktop.AppImage': APPIMAGE_HEAD + os.urandom(64)}))
        with self.assertRaisesRegex(ValueError, 'update-test marker'):
            self.module['require_absent']([package])
        self.assertEqual(1, len(self.calls))

    def test_renamed_appimage_is_found_by_its_magic(self):
        self.fake({'usr/bin/Editor.App': assembly(MARKER, 'utf-16-le')})
        self.assertTrue(self.module['contains_marker'](self.appimage('renamed.bin')))

    def test_fails_closed_on_a_host_that_is_not_linux(self):
        self.fake({'usr/bin/Editor.App': b'clean'})
        for system in ('Darwin', 'Windows'):
            self.globals['platform_system'] = lambda system=system: system
            with self.subTest(system=system), self.assertRaisesRegex(ValueError, 'only on Linux'):
                self.module['require_absent']([self.appimage()])
        self.assertEqual([], self.calls)

    def test_fails_closed_when_extraction_fails(self):
        self.fake(error=subprocess.CalledProcessError(1, ['extract']))
        with self.assertRaisesRegex(ValueError, 'extraction failed'):
            self.module['require_absent']([self.appimage()])

    def test_fails_closed_when_extraction_cannot_start(self):
        self.fake(error=FileNotFoundError('unshare'))
        with self.assertRaisesRegex(ValueError, 'extraction failed'):
            self.module['require_absent']([self.appimage()])

    def test_fails_closed_on_a_timeout(self):
        self.fake(error=subprocess.TimeoutExpired(['extract'], 1))
        with self.assertRaisesRegex(ValueError, 'extraction failed'):
            self.module['require_absent']([self.appimage()])

    def test_fails_closed_when_extraction_yields_no_files(self):
        for files in ({}, None):
            self.fake(files)
            with self.subTest(files=files), self.assertRaisesRegex(ValueError, 'no files'):
                self.module['require_absent']([self.appimage()])

    def test_each_extraction_uses_a_fresh_directory_that_is_removed(self):
        self.fake({'AppRun': b'run'})
        self.module['require_absent']([self.appimage(), self.appimage('second.AppImage')])
        destinations = [call[1] for call in self.calls]
        self.assertEqual(2, len(set(destinations)))
        self.assertFalse(any(path.exists() for path in destinations))

    def test_extractor_command_is_the_exact_privilege_dropping_sandbox(self):
        image = self.appimage()
        self.assertEqual(
            ['timeout', '--signal=KILL', '300', 'sudo', '-n', 'unshare', '--net', '--',
             'setpriv', '--reuid=1234', '--regid=5678', '--clear-groups', '--no-new-privs',
             '--inh-caps=-all', '--bounding-set=-all', '--', str(image), '--appimage-extract'],
            self.module['extractor_command'](image, 1234, 5678, 300))

    def test_real_extractor_uses_the_script_uid_and_gid_and_does_not_run_the_app(self):
        recorded = {}

        class FakeSubprocess:
            CalledProcessError = subprocess.CalledProcessError
            TimeoutExpired = subprocess.TimeoutExpired
            DEVNULL = subprocess.DEVNULL
            PIPE = subprocess.PIPE

            @staticmethod
            def run(command, **options):
                recorded.update(command=command, **options)
                return subprocess.CompletedProcess(command, 0)

        self.globals['subprocess'] = FakeSubprocess
        image = self.appimage()
        self.module['run_extractor'](image, self.directory, 30)
        self.assertEqual(self.module['extractor_command'](image, os.getuid(), os.getgid(), 30), recorded['command'])
        self.assertGreater(recorded['timeout'], 30)
        self.assertTrue(recorded['check'])
        self.assertEqual(str(self.directory), str(recorded['cwd']))
        self.assertNotIn('APPIMAGE_EXTRACT_AND_RUN', recorded['env'])
        self.assertFalse({'http_proxy', 'https_proxy', 'HTTP_PROXY', 'HTTPS_PROXY'} & set(recorded['env']))

    def test_missing_sudo_fails_closed_and_does_not_extract(self):
        self.fake({'AppRun': b'run'})
        self.globals['sudo_available'] = lambda: False
        with self.assertRaisesRegex(ValueError, 'sudo -n'):
            self.module['require_absent']([self.appimage()])
        self.assertEqual([], self.calls)

    def test_sudo_check_is_non_interactive(self):
        recorded = []
        self.globals['shutil'] = type('S', (), {'which': staticmethod(lambda name: '/usr/bin/sudo'),
                                                'rmtree': staticmethod(shutil.rmtree)})
        class FakeSubprocess:
            DEVNULL = subprocess.DEVNULL
            @staticmethod
            def run(command, **options):
                recorded.append(command)
                return subprocess.CompletedProcess(command, 1)
        self.globals['subprocess'] = FakeSubprocess
        self.assertFalse(self.module['sudo_available']())
        self.assertEqual([['sudo', '-n', 'true']], recorded)
        self.globals['shutil'] = type('S', (), {'which': staticmethod(lambda name: None)})
        self.assertFalse(self.module['sudo_available']())

    def test_sudo_check_has_a_time_limit_and_fails_closed_on_timeout(self):
        recorded = []
        self.globals['shutil'] = type('S', (), {'which': staticmethod(lambda name: '/usr/bin/sudo'),
                                                'rmtree': staticmethod(shutil.rmtree)})
        class FakeSubprocess:
            DEVNULL = subprocess.DEVNULL
            TimeoutExpired = subprocess.TimeoutExpired
            @staticmethod
            def run(command, **options):
                recorded.append(options.get('timeout'))
                raise subprocess.TimeoutExpired(command, options.get('timeout'))
        self.globals['subprocess'] = FakeSubprocess
        self.assertFalse(self.module['sudo_available']())
        self.assertEqual([30], recorded)

    def test_extracted_files_not_owned_by_the_runner_uid_fail_closed(self):
        self.fake({'AppRun': b'run'})
        self.globals['runner_uid'] = lambda: os.getuid() + 1
        with self.assertRaisesRegex(ValueError, 'owned by the runner user'):
            self.module['require_absent']([self.appimage()])

    @unittest.skipUnless(sys.platform.startswith('linux') and shutil.which('setpriv'), 'needs Linux')
    def test_real_extraction_of_a_file_that_is_not_an_appimage_fails_closed(self):
        del self.globals['sudo_available']
        self.module = scanner()
        image = self.appimage()
        image.chmod(0o755)
        with self.assertRaisesRegex(ValueError, 'extraction failed|sudo -n'):
            self.module['require_absent']([image])


class PackageReleaseScans(unittest.TestCase):
    def test_package_release_scans_the_published_folder_before_packing(self):
        source = (ENG / 'package-release.py').read_text(encoding='utf-8')
        packing = source.index('pack_args = ')
        for call in ('["require_present_in"](prepack["directory_files"](publish))',
                     '["require_absent"](prepack["directory_files"](publish))'):
            self.assertLess(source.index(call), packing, call)

    def test_directory_helpers(self):
        module = scanner()
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            (folder / 'sub').mkdir()
            (folder / 'sub/Editor.App').write_bytes(assembly(MARKER, 'utf-16-le'))
            (folder / 'other').write_bytes(b'clean')
            self.assertEqual(2, len(module['directory_files'](folder)))
            module['require_present_in'](module['directory_files'](folder))
            with self.assertRaisesRegex(ValueError, 'update-test marker'):
                module['require_absent'](module['directory_files'](folder))
            (folder / 'sub/Editor.App').unlink()
            with self.assertRaisesRegex(ValueError, 'positive control'):
                module['require_present_in'](module['directory_files'](folder))
            with self.assertRaisesRegex(ValueError, 'positive control'):
                module['require_present_in']([])

    def test_zip_artifacts_include_the_appimage(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            for name in ('a.nupkg', 'b-Portable.zip', 'TangopadDesktop-linux-x64.AppImage', 'notes.json'):
                (folder / name).write_bytes(b'x')
            self.assertEqual(['TangopadDesktop-linux-x64.AppImage', 'a.nupkg', 'b-Portable.zip'],
                             [path.name for path in scanner()['zip_artifacts'](folder)])


if __name__ == '__main__':
    unittest.main()
