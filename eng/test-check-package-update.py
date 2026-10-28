#!/usr/bin/env python3
"""Check the result rules of the real package update check."""
import json
from pathlib import Path
import runpy
import tempfile
import unittest

CHECK = runpy.run_path(str(Path(__file__).with_name("check-package-update.py")))
MARK = "Tangopad.UpdateTestHook.v1"


def event(name, version, pid=1, error=None):
    return {"Event": name, "Marker": MARK, "Version": version, "ProcessId": pid, "ProcessPath": "x", "Error": error}


class PackageUpdateChecks(unittest.TestCase):
    def test_stop_editors_uses_the_windows_stop_only_on_windows(self):
        calls = []
        launch = CHECK['stop_editors'].__globals__['LAUNCH']
        original = launch['stop_installed']
        launch['stop_installed'] = calls.append
        try:
            for rid in ('linux-x64', 'osx-x64', 'osx-arm64'):
                CHECK['stop_editors'](rid, Path('root'))
            self.assertEqual([], calls)
            CHECK['stop_editors']('win-x64', Path('root'))
            self.assertEqual([Path('root')], calls)
        finally:
            launch['stop_installed'] = original

    def test_main_stops_editors_only_through_the_platform_guard(self):
        source = Path(__file__).with_name('check-package-update.py').read_text(encoding='utf-8')
        self.assertNotIn('LAUNCH["stop_installed"](root)', source.split('def main', 1)[1])

    def test_feed_has_exactly_three_names(self):
        self.assertEqual(CHECK["feed_names"]("osx-arm64", "1.0.1"),
                         ["releases.osx-arm64.json", "releases.osx-arm64.json.sig", "TangopadDesktop-1.0.1-osx-arm64-full.nupkg"])

    def test_update_needs_every_step_and_a_new_process(self):
        good = [event("start", "1.0.0"), event("downloaded", "1.0.1"), event("applying", "1.0.1"), event("restarted", "1.0.1", 2)]
        CHECK["check_updated"](good)
        for broken in (good[:3], [*good[:3], event("restarted", "1.0.0", 2)], [*good[:3], event("restarted", "1.0.1", 1)],
                       [event("start", "1.0.1"), *good[1:]]):
            with self.subTest(events=broken), self.assertRaises(RuntimeError):
                CHECK["check_updated"](broken)

    def test_bad_signature_must_stop_before_apply(self):
        self.assertEqual("bad", CHECK["check_rejected"]([event("start", "1.0.0"), event("error", "1.0.0", error="bad")]))
        for broken in ([event("start", "1.0.0")], [event("start", "1.0.0"), event("downloaded", "1.0.1"), event("error", "1.0.0")],
                       [event("start", "1.0.0"), event("no-update", "1.0.0")]):
            with self.subTest(events=broken), self.assertRaises(RuntimeError):
                CHECK["check_rejected"](broken)

    def test_report_must_show_the_new_version(self):
        CHECK["check_report"]([event("report", "1.0.1")])
        with self.assertRaises(RuntimeError):
            CHECK["check_report"]([event("report", "1.0.0")])

    def test_events_without_the_marker_are_refused(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "e.jsonl"
            path.write_text(json.dumps({**event("start", "1.0.0"), "Marker": "other"}) + "\n", encoding="utf-8")
            with self.assertRaisesRegex(RuntimeError, "update-test hook"):
                CHECK["read_events"](path)
            self.assertEqual([], CHECK["read_events"](Path(directory) / "missing.jsonl"))


    def test_run_editor_passes_a_time_limit_to_the_editor_process(self):
        recorded = []
        module = CHECK['run_editor'].__globals__
        original = module['subprocess']
        class FakeSubprocess:
            PIPE, STDOUT = original.PIPE, original.STDOUT
            @staticmethod
            def run(command, **options):
                recorded.append(options.get('timeout'))
                return original.CompletedProcess(command, 0, stdout='')
        module['subprocess'] = FakeSubprocess
        try:
            CHECK['run_editor'](Path('editor'), {})
            CHECK['run_editor'](Path('editor'), {}, timeout=5)
        finally:
            module['subprocess'] = original
        self.assertEqual(2, len(recorded))
        self.assertTrue(all(isinstance(value, (int, float)) and 0 < value <= 600 for value in recorded), recorded)
        self.assertEqual(5, recorded[1])

    def test_restart_wait_has_a_limit(self):
        self.assertTrue(0 < CHECK['RESTART_SECONDS'] <= 600, CHECK['RESTART_SECONDS'])
        with tempfile.TemporaryDirectory() as directory:
            module = CHECK['wait_for'].__globals__
            original = module['time']
            clock = [0.0]
            module['time'] = type('T', (), {'monotonic': staticmethod(lambda: clock[0]),
                                            'sleep': staticmethod(lambda seconds: clock.__setitem__(0, clock[0] + seconds))})
            try:
                self.assertEqual([], CHECK['wait_for'](Path(directory) / 'none.jsonl', 4, 10))
            finally:
                module['time'] = original
            self.assertGreaterEqual(clock[0], 10)
            self.assertLess(clock[0], 20)


if __name__ == "__main__":
    unittest.main()
