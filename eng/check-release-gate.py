#!/usr/bin/env python3
"""Release gate: refuse a release unless the real-update test passed on all 4 targets for this commit.

The gate reads the GitHub Actions API with the workflow token (actions: read). It looks for a
completed, successful run of the Package candidates workflow (packages.yml) in this repository
for the same commit. In that run, each of the 4 package jobs and its update step must be green.
The gate fails closed when no such run exists or when the lookup fails.
"""
import argparse
import json
import re
import subprocess
import sys

WORKFLOW = ".github/workflows/packages.yml"
TARGETS = ("win-x64", "linux-x64", "osx-x64", "osx-arm64")
UPDATE_STEP = "Update the real package through the editor"


def gh_api(endpoint):
    completed = subprocess.run(["gh", "api", "-H", "Accept: application/vnd.github+json", endpoint],
                               capture_output=True, text=True, timeout=60, check=True)
    return json.loads(completed.stdout)


def _missing_targets(jobs):
    missing = []
    for rid in TARGETS:
        good = [job for job in jobs if job.get("name") == f"Package ({rid})" and job.get("conclusion") == "success"
                and any(step.get("name") == UPDATE_STEP and step.get("conclusion") == "success"
                        for step in job.get("steps") or [])]
        if not good:
            missing.append(rid)
    return missing


def check(repo, sha, api=gh_api):
    """Return the id of a full green run for the commit, or raise ValueError."""
    if not re.fullmatch(r"[0-9a-f]{40}", sha or ""):
        raise ValueError("The release gate needs the full commit SHA.")
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repo or ""):
        raise ValueError("The release gate needs the owner/name repository.")
    problems = []
    try:
        runs = api(f"repos/{repo}/actions/workflows/packages.yml/runs?head_sha={sha}&status=success&per_page=100")
        candidates = [run for run in runs.get("workflow_runs") or []
                      if run.get("head_sha") == sha and run.get("status") == "completed"
                      and run.get("conclusion") == "success" and run.get("path") == WORKFLOW
                      and (run.get("head_repository") or {}).get("full_name") == repo]
        for run in candidates:
            jobs = api(f"repos/{repo}/actions/runs/{int(run['id'])}/jobs?filter=latest&per_page=100").get("jobs") or []
            missing = _missing_targets(jobs)
            if not missing:
                return run["id"]
            problems.append(f"run {run['id']} has no green update for {', '.join(missing)}")
    except Exception as error:  # Any lookup problem must stop the release.
        raise ValueError(f"The release gate lookup failed, so the release cannot go ahead: {type(error).__name__}") from error
    detail = f" ({'; '.join(problems)})" if problems else ""
    raise ValueError(f"No successful real-update run of {WORKFLOW} with all 4 targets exists for {sha}{detail}.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", required=True)
    parser.add_argument("--sha", required=True)
    arguments = parser.parse_args()
    try:
        identifier = check(arguments.repo, arguments.sha)
    except ValueError as error:
        print(f"::error::{error}", file=sys.stderr)
        sys.exit(1)
    print(f"The release gate passed: run {identifier} updated all 4 targets for {arguments.sha}.")


if __name__ == "__main__":
    main()
