#!/usr/bin/env python3
"""Check the 50-character limit of the pull request title rule."""

import os
from pathlib import Path
import subprocess
import unittest

SCRIPT = Path(__file__).with_name('check-pr-title.sh')
TITLE_49 = 'docs: describe the title length rule for reviewer'
TITLE_50 = 'docs: describe the title length rule for reviewers'
TITLE_51 = 'docs: describe the title length rules for reviewers'


def run(*arguments, title=None):
    environment = os.environ.copy()
    environment.pop('TITLE', None)
    if title is not None:
        environment['TITLE'] = title
    return subprocess.run(['bash', str(SCRIPT), *arguments], env=environment, capture_output=True, text=True, check=False)


class TitleLengthChecks(unittest.TestCase):
    def test_fixture_lengths(self):
        self.assertEqual([49, 50, 51], [len(TITLE_49), len(TITLE_50), len(TITLE_51)])

    def test_check_accepts_49_and_50_characters(self):
        for title in (TITLE_49, TITLE_50):
            with self.subTest(length=len(title)):
                result = run('--check', title=title)
                self.assertEqual(0, result.returncode, result.stderr)

    def test_check_rejects_51_characters_and_states_the_limit(self):
        result = run('--check', title=TITLE_51)
        self.assertEqual(1, result.returncode)
        self.assertIn('50 characters or fewer', result.stderr)
        self.assertIn('51 characters', result.stderr)

    def test_examples_include_the_limit_cases(self):
        result = run('--examples')
        self.assertEqual(0, result.returncode, result.stderr)
        lines = result.stdout.splitlines()
        self.assertIn(f'accept\ttitle of 49 characters\t{TITLE_49}', lines)
        self.assertIn(f'accept\ttitle of 50 characters\t{TITLE_50}', lines)
        self.assertIn(f'reject\ttitle of 51 characters\t{TITLE_51}', lines)


if __name__ == '__main__':
    unittest.main()
