#!/usr/bin/env python3
"""Check the package selection and result rules of the real package launch check."""

import json
from pathlib import Path
import runpy
import tempfile
import unittest

CHECK = runpy.run_path(str(Path(__file__).with_name('check-package-launch.py')))
PASS = 'PASS. The shell opened a native window. The check sent WM_CLOSE. The shell exited with code 0.'


class PackageLaunchChecks(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.release = Path(self.temporary.name) / 'release'

    def candidate(self, rid='win-x64', version='1.0.0', runtime=None, files=('TangopadDesktop-win-Setup.exe',)):
        directory = self.release / rid / version
        directory.mkdir(parents=True)
        (directory / f'package.{rid}.json').write_text(json.dumps({'version': version, 'runtime': runtime or rid}))
        for name in files:
            (directory / name).write_bytes(b'package')
        return directory

    def test_selects_the_one_candidate_and_its_installer(self):
        directory = self.candidate()
        self.assertEqual(('1.0.0', directory, directory / 'TangopadDesktop-win-Setup.exe'), CHECK['find_candidate'](self.release, 'win-x64'))

    def test_selects_each_platform_installer(self):
        for rid, name in (('linux-x64', 'TangopadDesktop.AppImage'), ('osx-arm64', 'TangopadDesktop-osx-arm64-Setup.pkg')):
            directory = self.candidate(rid, files=(name, 'TangopadDesktop-1.0.0-full.nupkg'))
            self.assertEqual(directory / name, CHECK['find_candidate'](self.release, rid)[2])

    def test_rejects_missing_or_extra_candidates(self):
        with self.assertRaisesRegex(ValueError, 'one candidate'):
            CHECK['find_candidate'](self.release, 'win-x64')
        self.candidate()
        self.candidate(version='1.0.1')
        with self.assertRaisesRegex(ValueError, 'one candidate'):
            CHECK['find_candidate'](self.release, 'win-x64')

    def test_rejects_a_manifest_for_another_runtime_or_version(self):
        self.candidate(runtime='linux-x64')
        with self.assertRaisesRegex(ValueError, 'manifest'):
            CHECK['find_candidate'](self.release, 'win-x64')

    def test_rejects_a_manifest_for_another_version(self):
        directory = self.candidate()
        (directory / 'package.win-x64.json').write_text(json.dumps({'version': '1.0.1', 'runtime': 'win-x64'}))
        with self.assertRaisesRegex(ValueError, 'manifest'):
            CHECK['find_candidate'](self.release, 'win-x64')

    def test_rejects_a_macos_package_that_is_not_the_installer(self):
        self.candidate('osx-x64', files=('TangopadDesktop-osx-x64-Setup.pkg', 'TangopadDesktop-osx-x64-Portable.pkg'))
        self.assertEqual('TangopadDesktop-osx-x64-Setup.pkg', CHECK['find_candidate'](self.release, 'osx-x64')[2].name)

    def test_rejects_missing_or_extra_installers(self):
        self.candidate(files=('TangopadDesktop-win-Setup.exe', 'Other-Setup.exe'))
        with self.assertRaisesRegex(ValueError, 'one installer'):
            CHECK['find_candidate'](self.release, 'win-x64')

    def test_installed_executable_uses_the_real_application(self):
        root = Path('/root')
        self.assertEqual(root / 'current/Editor.App.exe', CHECK['installed_executable']('win-x64', root))
        self.assertEqual(root / 'Tangopad.AppImage', CHECK['installed_executable']('linux-x64', root))
        self.assertEqual(root / 'Tangopad.app/Contents/MacOS/Editor.App', CHECK['installed_executable']('osx-x64', root))
        self.assertEqual(root / 'Tangopad.app/Contents/MacOS/Editor.App', CHECK['installed_executable']('osx-arm64', root))

    def test_result_requires_one_passed_smoke_message(self):
        self.assertEqual(PASS + ' Host: x.', CHECK['smoke_result'](0, f'<Output><StdOut>{PASS} Host: x.</StdOut></Output>'))
        for code, report in ((1, PASS), (0, 'NOT TESTED: No X11 display is open.'), (0, 'FAILED. ' + PASS[6:]), (0, ''), (0, f'<StdOut>{PASS}</StdOut><Message>FAILED. The other test failed.</Message>')):
            with self.assertRaises(RuntimeError):
                CHECK['smoke_result'](code, report)

    def test_smoke_environment_selects_the_installed_executable_only(self):
        environment = CHECK['smoke_environment']({'PATH': 'x', 'TANGOPAD_SMOKE_EXECUTABLE': 'old'}, Path('/installed/app'))
        self.assertEqual({'PATH': 'x', 'TANGOPAD_SMOKE_EXECUTABLE': str(Path('/installed/app'))}, environment)


if __name__ == '__main__':
    unittest.main()
