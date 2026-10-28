#!/usr/bin/env python3
"""Unit tests for the release gate. Every case without a full green update run must fail."""
import json
import os
import re
from pathlib import Path
import runpy
import subprocess
import sys
import tempfile
import unittest

ENG = Path(__file__).resolve().parent
SHA = 'a' * 40
REPO = 'daniel-kindl/tangopad'
TARGETS = ('win-x64', 'linux-x64', 'osx-x64', 'osx-arm64')


def gate():
    return runpy.run_path(str(ENG / 'check-release-gate.py'), run_name='gate')


def run(identifier=1, sha=SHA, conclusion='success', path='.github/workflows/packages.yml', repo=REPO):
    return {'id': identifier, 'head_sha': sha, 'conclusion': conclusion, 'status': 'completed', 'path': path,
            'head_repository': {'full_name': repo}}


def job(rid, conclusion='success', step='success'):
    return {'name': f'Package ({rid})', 'conclusion': conclusion,
            'steps': [{'name': 'Update the real package through the editor', 'conclusion': step}]}


def green_jobs():
    return [job(rid) for rid in TARGETS]


class Api:
    def __init__(self, runs, jobs, error=None):
        self.runs, self.jobs, self.error, self.calls = runs, jobs, error, []

    def __call__(self, endpoint):
        self.calls.append(endpoint)
        if self.error is not None:
            raise self.error
        if '/jobs' in endpoint:
            return {'jobs': self.jobs[int(endpoint.split('/runs/')[1].split('/')[0])]}
        return {'workflow_runs': self.runs}


class ReleaseGate(unittest.TestCase):
    def setUp(self):
        self.module = gate()

    def check(self, api):
        return self.module['check'](REPO, SHA, api)

    def refused(self, api, message):
        with self.assertRaisesRegex(ValueError, message):
            self.check(api)

    def test_full_green_run_for_the_same_commit_passes(self):
        api = Api([run()], {1: green_jobs()})
        self.assertEqual(1, self.check(api))
        self.assertIn(f'head_sha={SHA}', api.calls[0])
        self.assertIn('workflows/packages.yml/runs', api.calls[0])

    def test_no_run_fails(self):
        self.refused(Api([], {}), 'No successful')

    def test_failed_run_fails(self):
        self.refused(Api([run(conclusion='failure')], {1: green_jobs()}), 'No successful')

    def test_run_for_a_different_commit_fails(self):
        self.refused(Api([run(sha='b' * 40)], {1: green_jobs()}), 'No successful')

    def test_run_of_another_workflow_fails(self):
        self.refused(Api([run(path='.github/workflows/verify.yml')], {1: green_jobs()}), 'No successful')

    def test_run_from_another_repository_fails(self):
        self.refused(Api([run(repo='someone/fork')], {1: green_jobs()}), 'No successful')

    def test_partial_run_with_a_missing_target_fails(self):
        self.refused(Api([run()], {1: green_jobs()[:3]}), 'osx-arm64')

    def test_failed_target_fails(self):
        jobs = green_jobs()
        jobs[1] = job('linux-x64', conclusion='failure')
        self.refused(Api([run()], {1: jobs}), 'linux-x64')

    def test_skipped_update_step_fails(self):
        jobs = green_jobs()
        jobs[2] = job('osx-x64', step='skipped')
        self.refused(Api([run()], {1: jobs}), 'osx-x64')

    def test_api_error_fails(self):
        self.refused(Api([run()], {1: green_jobs()}, error=OSError('network')), 'lookup failed')

    def test_malformed_api_answer_fails(self):
        self.refused(Api([{'id': 1}], {1: green_jobs()}), 'No successful|lookup failed')

    def test_a_bad_sha_is_refused_before_any_lookup(self):
        api = Api([run()], {1: green_jobs()})
        with self.assertRaisesRegex(ValueError, 'commit'):
            self.module['check'](REPO, 'main', api)
        self.assertEqual([], api.calls)

    def test_an_older_green_run_is_enough_when_a_newer_run_failed(self):
        api = Api([run(2, conclusion='failure'), run(1)], {1: green_jobs(), 2: []})
        self.assertEqual(1, self.check(api))


FAKE_GH = """#!/usr/bin/env python3
import json, os, sys
endpoint = sys.argv[-1]
with open(os.environ['FAKE_GH_LOG'], 'a') as log:
    log.write(endpoint + '\\n')
answers = json.loads(os.environ['FAKE_GH_ANSWERS'])
key = 'jobs' if '/jobs' in endpoint else 'runs'
print(json.dumps(answers[key]))
"""


class ReleaseGateProcess(unittest.TestCase):
    """The gate as the release workflow runs it: a process that must exit non-zero to stop the release."""

    def gate_process(self, runs, jobs):
        directory = Path(self.enterContext(tempfile.TemporaryDirectory()))
        fake = directory / 'gh'
        fake.write_text(FAKE_GH, encoding='utf-8')
        fake.chmod(0o755)
        log = directory / 'calls.txt'
        environment = {**os.environ, 'PATH': f"{directory}{os.pathsep}{os.environ['PATH']}", 'FAKE_GH_LOG': str(log),
                       'FAKE_GH_ANSWERS': json.dumps({'runs': {'workflow_runs': runs}, 'jobs': {'jobs': jobs}})}
        completed = subprocess.run([sys.executable, str(ENG / 'check-release-gate.py'), '--repo', REPO, '--sha', SHA],
                                   env=environment, capture_output=True, text=True, timeout=60, check=False)
        return completed, log.read_text(encoding='utf-8') if log.exists() else ''

    @unittest.skipIf(os.name == 'nt', 'The fake gh is a POSIX script.')
    def test_gate_exits_non_zero_when_no_green_run_exists_for_the_packaged_commit(self):
        completed, calls = self.gate_process([run(sha='b' * 40)], green_jobs())
        self.assertNotEqual(0, completed.returncode, completed.stdout)
        self.assertIn('No successful', completed.stderr)
        self.assertIn(f'head_sha={SHA}', calls)

    @unittest.skipIf(os.name == 'nt', 'The fake gh is a POSIX script.')
    def test_gate_exits_zero_only_for_a_full_green_run_of_the_packaged_commit(self):
        completed, _ = self.gate_process([run()], green_jobs())
        self.assertEqual(0, completed.returncode, completed.stderr)


class GateMatchesPackagesWorkflow(unittest.TestCase):
    """The gate reads job and step names from packages.yml runs. They must not drift apart."""

    def workflow(self):
        return (ENG.parent / '.github' / 'workflows' / 'packages.yml').read_text(encoding='utf-8')

    def package_job(self):
        text = self.workflow()
        start = text.index('\n  package:\n')
        rest = text[start + 1:]
        following = re.search(r'\n  [A-Za-z0-9_-]+:\n', rest[1:])
        return rest if following is None else rest[:following.start() + 1]

    def test_package_job_names_match_the_gate_for_every_matrix_target(self):
        values = gate()
        block = self.package_job()
        template = re.search(r'^    name: (.+)$', block, re.M).group(1).strip()
        rids = tuple(re.findall(r'^ +rid: (\S+)$', block, re.M))
        self.assertEqual(set(rids), set(values['TARGETS']))
        self.assertEqual(len(rids), len(values['TARGETS']))
        for rid in rids:
            workflow_name = template.replace('${{ matrix.rid }}', rid)
            self.assertEqual(workflow_name, values['JOB_NAME'].format(rid=rid))

    def test_update_step_name_matches_the_gate(self):
        values = gate()
        names = re.findall(r'^      - name: (.+)$', self.package_job(), re.M)
        self.assertIn(values['UPDATE_STEP'], [name.strip() for name in names])

    def test_gate_reads_the_packages_workflow_file(self):
        self.assertEqual(gate()['WORKFLOW'], '.github/workflows/packages.yml')


if __name__ == '__main__':
    unittest.main()
