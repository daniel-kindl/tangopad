#!/usr/bin/env python3
"""Check the package selection and result rules of the real package launch check."""

import json
from pathlib import Path
import runpy
import tempfile
import contextlib
import unittest

CHECK = runpy.run_path(str(Path(__file__).with_name('check-package-launch.py')))


class FakeSubprocess:
    """Record the child processes that the check starts."""
    TimeoutExpired = TimeoutError

    def __init__(self, dominfo='CurrentUserHomeDirectory', listings=()):
        self.calls = []
        self.dominfo = dominfo
        self.listings = list(listings)

    def check_output(self, command, **options):
        self.calls.append((command, options))
        return self.dominfo

    def run(self, command, **options):
        self.calls.append((command, options))
        stdout = self.listings.pop(0) if 'ConvertTo-Json' in ' '.join(map(str, command)) and self.listings else ''
        return type('Completed', (), {'returncode': 0, 'stdout': stdout, 'stderr': ''})()


@contextlib.contextmanager
def patched(name, value):
    """Replace one global of the check while a test runs."""
    names = CHECK['install'].__globals__
    old = names[name]
    names[name] = value
    try:
        yield value
    finally:
        names[name] = old


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

    def test_selects_the_macos_setup_package(self):
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

    def test_macos_domains_require_the_current_user_home_only(self):
        CHECK['check_macos_domains']('Domain CurrentUserHomeDirectory: enabled')
        for domains in ('Domain LocalSystem: enabled', 'Domain LocalSystem\nDomain CurrentUserHomeDirectory', ''):
            with self.assertRaisesRegex(RuntimeError, 'current user'):
                CHECK['check_macos_domains'](domains)

    def test_macos_install_refuses_an_existing_application(self):
        bundle = Path(self.temporary.name) / 'Tangopad.app'
        CHECK['refuse_existing_bundle'](bundle)
        bundle.mkdir()
        with self.assertRaisesRegex(RuntimeError, 'existing Tangopad'):
            CHECK['refuse_existing_bundle'](bundle)

    def test_macos_install_checks_the_guards_before_it_installs(self):
        home = Path(self.temporary.name)
        fake = FakeSubprocess(dominfo='LocalSystem CurrentUserHomeDirectory')
        with patched('subprocess', fake), contextlib.ExitStack() as resources:
            with self.assertRaisesRegex(RuntimeError, 'current user'):
                CHECK['install']('osx-arm64', Path('a.pkg'), resources, home, home=home)
            (home / 'Applications/Tangopad.app').mkdir()
            with self.assertRaisesRegex(RuntimeError, 'existing Tangopad'):
                CHECK['install']('osx-arm64', Path('a.pkg'), resources, home, home=home)
        self.assertEqual([['installer', '-pkg', 'a.pkg', '-dominfo']], [call[0] for call in fake.calls])

    def test_child_processes_have_a_time_limit(self):
        home = Path(self.temporary.name)
        fake = FakeSubprocess(listings=['[]'] * 20)
        root = home / 'installed'
        (root / 'current').mkdir(parents=True)
        (root / 'current/Editor.App.exe').write_bytes(b'')
        clock = iter(range(1000)).__next__
        with patched('subprocess', fake), patched('time', type('T', (), {'monotonic': clock, 'sleep': lambda _: None})), contextlib.ExitStack() as resources:
            CHECK['install']('osx-x64', Path('a.pkg'), resources, home, home=home)
            CHECK['install']('win-x64', Path('Setup.exe'), resources, home, home=home)
            CHECK['run_smoke'](['dotnet', 'test'], {})
            CHECK['source_commit']()
        self.assertIn(['git', 'rev-parse', 'HEAD'], [call[0] for call in fake.calls])
        for command, options in fake.calls:
            self.assertGreater(options.get('timeout', 0), 0, command)
            self.assertLessEqual(options['timeout'], 900, command)

    def test_windows_install_waits_for_setup_and_the_editor_before_it_stops(self):
        work = Path(self.temporary.name)
        events = []
        fake = FakeSubprocess(listings=['[]'])
        def wait(executable, processes, *_):
            events.append(('wait', executable, [call[0][0] for call in fake.calls]))
            return set()
        with patched('subprocess', fake), patched('wait_until_settled', wait), contextlib.ExitStack() as resources:
            CHECK['install']('win-x64', Path('Setup.exe'), resources, work)
        self.assertEqual([('wait', work / 'installed/current/Editor.App.exe', ['Setup.exe'])], events)
        self.assertEqual(['Setup.exe', 'powershell'], [call[0][0] for call in fake.calls])

    def test_process_listing_does_not_fail_when_no_editor_runs(self):
        # Get-Process with a name and no match writes an error record, so PowerShell exits with code 1.
        fake = FakeSubprocess(listings=[''])
        with patched('subprocess', fake):
            self.assertEqual([], CHECK['list_editor_processes'](Path('C:/installed')))
        command = fake.calls[0][0][-1]
        self.assertNotIn('Get-Process Editor.App', command)
        self.assertIn("Where-Object { $_.ProcessName -eq 'Editor.App' }", command)

    def test_editor_processes_selects_only_processes_below_the_root(self):
        root = Path("C:/Temp/it's/installed")
        listing = json.dumps([{'Id': 1, 'Path': "C:\\Temp\\it's\\installed\\current\\Editor.App.exe"},
                              {'Id': 2, 'Path': 'C:\\Program Files\\Tangopad\\Editor.App.exe'},
                              {'Id': 3, 'Path': "c:\\temp\\IT'S\\installed-other\\Editor.App.exe"},
                              {'Id': 4, 'Path': None}])
        self.assertEqual([1], CHECK['editor_processes'](root, listing))
        self.assertEqual([5], CHECK['editor_processes'](root, json.dumps({'Id': 5, 'Path': "C:\\Temp\\it's\\installed\\Editor.App.exe"})))
        self.assertEqual([], CHECK['editor_processes'](root, ''))

    def test_wait_returns_when_the_editor_is_quiet_after_setup(self):
        executable = Path(self.temporary.name) / 'Editor.App.exe'
        executable.write_bytes(b'')
        times = iter([0, 0, 1, 2, 3, 4, 5])
        listings = iter([[], [7], [7], [7, 8], [7, 8], [7, 8]])
        self.assertEqual({7, 8}, CHECK['wait_until_settled'](executable, lambda: next(listings), lambda: next(times), lambda _: None))

    def test_wait_waits_for_the_installed_executable(self):
        executable = Path(self.temporary.name) / 'Editor.App.exe'
        times = iter(range(100))
        with self.assertRaisesRegex(RuntimeError, 'did not settle'):
            CHECK['wait_until_settled'](executable, lambda: [], lambda: next(times), lambda _: None)

    def test_wait_fails_when_editors_keep_starting(self):
        executable = Path(self.temporary.name) / 'Editor.App.exe'
        executable.write_bytes(b'')
        times = iter(range(100))
        counter = iter(range(100))
        with self.assertRaisesRegex(RuntimeError, 'did not settle'):
            CHECK['wait_until_settled'](executable, lambda: [next(counter)], lambda: next(times), lambda _: None)

    def test_stop_installed_fails_when_an_editor_stays_running(self):
        root = Path('C:/installed')
        running = json.dumps({'Id': 9, 'Path': 'C:\\installed\\current\\Editor.App.exe'})
        with patched('subprocess', FakeSubprocess(listings=[running, '[]'])):
            CHECK['stop_installed'](root)
        fake = FakeSubprocess(listings=[running, running])
        with patched('subprocess', fake):
            with self.assertRaisesRegex(RuntimeError, 'still running'):
                CHECK['stop_installed'](root)
        self.assertIn(['powershell', '-NoProfile', '-Command', 'Stop-Process -Force -Id 9'], [call[0] for call in fake.calls])


if __name__ == '__main__':
    unittest.main()
