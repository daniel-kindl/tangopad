#!/usr/bin/env python3
"""Check that a failed or stuck pack command leaves its output in the job log and the evidence folder."""

import contextlib
import io
from pathlib import Path
import runpy
import sys
import tempfile
import unittest

CHECK = runpy.run_path(str(Path(__file__).with_name('check-native-update.py')))
RUN_PACK = CHECK['run_pack']
LIMIT = CHECK['PACK_LOG_TAIL']


class PackOutputChecks(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.log = Path(temporary.name) / 'evidence' / 'vpk-pack-0.1.0.log'

    def run_pack(self, script, timeout=30):
        command = [sys.executable, '-c', script]
        printed = io.StringIO()
        error = None
        with contextlib.redirect_stdout(printed):
            try:
                RUN_PACK(command, self.log, timeout)
            except RuntimeError as caught:
                error = caught
        return command, printed.getvalue(), error

    def test_failure_writes_command_exit_code_and_output_tail(self):
        script = ("import sys; print('HE' + 'AD-' + 'x' * 40000, flush=True); print('vpk-out-' + 'end', flush=True); "
                  "print('vpk-err-' + 'end', file=sys.stderr, flush=True); sys.exit(3)")
        command, printed, error = self.run_pack(script)
        self.assertIsNotNone(error)
        self.assertIn(' '.join(command), printed)
        self.assertIn('exit code 3', printed)
        self.assertIn('vpk-out-end', printed)
        self.assertIn('vpk-err-end', printed)
        self.assertNotIn('HEAD-', printed)
        self.assertLessEqual(printed.count('x'), LIMIT)
        self.assertGreater(printed.count('x'), 0)
        saved = self.log.read_text(encoding='utf-8')
        self.assertIn('HEAD-', saved)
        self.assertIn('vpk-out-end', saved)
        self.assertIn('vpk-err-end', saved)

    def test_timeout_stops_the_command_and_writes_the_output(self):
        script = "import sys, time; print('started-' + 'pack', flush=True); print('err-' + 'pack', file=sys.stderr, flush=True); time.sleep(60)"
        command, printed, error = self.run_pack(script, timeout=2)
        self.assertIsNotNone(error)
        self.assertIn(' '.join(command), printed)
        self.assertIn('time limit of 2 s', printed)
        self.assertIn('started-pack', printed)
        self.assertIn('err-pack', printed)
        self.assertIn('started-pack', self.log.read_text(encoding='utf-8'))

    def test_success_prints_nothing_and_keeps_the_output_file(self):
        command, printed, error = self.run_pack("print('quiet-pack-' + 'output ' * 1000)")
        self.assertIsNone(error)
        self.assertEqual('', printed)
        self.assertIn('quiet-pack-output', self.log.read_text(encoding='utf-8'))


if __name__ == '__main__':
    unittest.main()
