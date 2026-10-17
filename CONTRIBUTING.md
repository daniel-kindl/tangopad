# Contributing

## Prepare a change

1. Create a GitHub Issue with a bounded outcome and acceptance criteria.
2. Assign the issue to an appropriate milestone from [the backlog](docs/backlog.md).
3. Create a short-lived `feat/<issue>-<name>`, `fix/<issue>-<name>`, or `chore/<issue>-<name>` branch from `dev`.
4. Read [architecture](docs/architecture.md) before changing module ownership or dependencies.
5. Implement the smallest change that satisfies the issue.

`main` holds stable releases. `dev` integrates reviewed changes and is the default
branch. Target `dev` for normal pull requests. Merge a reviewed release pull
request from this repository's `dev` branch into `main`.
Use Semantic Versioning. Before 1.0, document breaking changes in each minor release.
The authoritative version is in `Directory.Build.props`.

Use Conventional Commits for commits and for the squash commit subject.
The pull request title uses this form:

```text
type[optional scope][optional !]: description
```

`chore(build): pin the .NET SDK` matches the form.
The square brackets in the form mark optional parts. Do not write the brackets in a title.
These titles match the form:

- `feat: add document identity`
- `fix(parser): handle empty input`
- `feat!: change document identity`
- `fix(core)!: reject unknown encoding`

The type is one of `build`, `chore`, `ci`, `docs`, `feat`, `fix`, `perf`, `refactor`, `revert`, `style`, or `test`.
A scope is optional. Put the scope in parentheses after the type.
Use lowercase letters, digits, and hyphens in a scope.
Put `!` immediately before the colon to mark a breaking change in the title.
The description must contain visible text.
Do not add leading or trailing whitespace to the description.
The title must be one line.
A commit body can also mark a breaking change with a `BREAKING CHANGE:` footer.
The title check does not read a footer.
The check accepts a title that matches this form.
It does not judge whether the type describes the change.
Reviewers still review the change.

[eng/check-pr-title.sh](eng/check-pr-title.sh) is the title rule.
The script runs the positive and negative examples.
The script then checks one title.
Do not copy the rule into a second checker.
`dotnet msbuild eng/verify.proj` does not check pull request titles.

## Verify a change

Run the single verification entry point from the repository root:

```sh
dotnet msbuild eng/verify.proj
```

The command stops at the first failing stage. It performs locked restore and
NuGet auditing for direct and transitive dependencies, format and analyzer
verification at info severity, Release compilation, all tests, and TRX report validation.
Build warnings are errors. CI repeats the command on Linux, Windows, and macOS.
The test stage clears `artifacts/test-results` before each run.
Each test project writes `<project-name>.trx` in that directory.

Verification requires a readable report with results from each expected assembly.
Missing, empty, or overwritten reports fail verification.
CI uploads the directory for each runner, including reports from failed runs.

The test stage includes native editor startup.
The test starts the built executable, closes its native window, and requires exit code 0.
Linux needs an X11 display, or the `Xvfb` command when no display is open.
Windows and macOS need an interactive desktop session.
A missing display fails verification.
The output starts with `NOT TESTED:` and states the follow-up.
[Native startup](docs/native-startup.md) lists the host libraries and recorded results.

To apply automatic C# formatting fixes, run:

```sh
dotnet format Tangopad.slnx --severity info
```

Review the resulting diff. Run verification again after changes. Formatting does
not check Markdown prose, YAML semantics, or every possible IDE inspection.
Use the writing rules below and inspect those files directly.

For an intentional dependency update, change `Directory.Packages.props`, then run:

```sh
dotnet restore Tangopad.slnx --force-evaluate
```

Review and commit the lock files with the version change. Update
`eng/architecture.json` only when the direct dependency graph changes.
Run full verification. Do not use `--force-evaluate` to bypass an unexpected locked-restore failure.

## Write code and tests

Use the compiler, SDK analyzers, and `.editorconfig` as the enforceable C# baseline.
Use nullable reference types, explicit accessibility, file-scoped namespaces, and
clear names. Document public APIs. Keep validation at external boundaries and
preserve internal type guarantees. Use cancellation for operations that can wait.

Core owns domain rules. Application owns use cases and the contracts they need.
Keep framework and filesystem implementations in adapters. Introduce interfaces
when a real consumer needs a boundary. Add no empty interfaces, stub services,
or unused extension mechanisms. See [design policy](docs/architecture.md#design-policy).

Test behavior through its consumer. For a defect or new behavior, first write a
failing test when a cheap, reliable test target exists. Assert literal expected
results. Add Core or Application test projects with their first behavior test.
Do not add placeholder tests or test counts as goals.

Architecture tests validate repository policy and the evaluated graph. Add API
constraints when domain behavior appears. Add adapter integration tests when an
adapter exists. Native UI smoke checks and performance benchmarks need observable
behavior and platform-specific acceptance criteria. Use native startup tests for
lifecycle checks. Use headless tests for control behavior.

Use ASD-STE100 principles for technical prose. Keep terms consistent and sentences
short. State the actor and one instruction per sentence. Preserve uncertainty,
modality, technical meaning, and exact API names. These rules do not claim formal
dictionary compliance. For product text, first write clear technical wording,
then revise it for natural language without changing its meaning.

## Submit a pull request

1. Link the issue and describe the resulting behavior.
2. Record verification results and platform limits.
3. Update an ADR if the change alters architecture or tooling policy.
4. Request review and wait for the required checks.

## GitHub integration safeguards

The active [Integration safeguards ruleset](https://github.com/daniel-kindl/tangopad/rules/25302628)
targets `main` and `dev`. GitHub requires a pull request, one approving review,
resolved review threads, and these checks from the GitHub Actions app:

- `Verify (ubuntu-24.04)`
- `Verify (windows-2025)`
- `Verify (macos-15)`
- `Conventional Commit title`

The PR source branch must be current with its target before merging. New reviewable commits
dismiss previous approvals. The ruleset also blocks branch deletion and force pushes.

The owner, `daniel-kindl`, can bypass the ruleset through a pull request. This
exception includes review and check requirements. It does not permit direct pushes.
Normal merges must wait for every check in the list above. The owner cannot approve their own
pull request. An independent reviewer with write access must approve it, or the
owner must explicitly use the bypass. A bypass is not evidence of review.

The release source restriction is a review convention. This ruleset does not
restrict the source branch of a pull request. Before merging into `main`, the
reviewer must confirm that the source is this repository's `dev` branch.

The [Verify workflow](.github/workflows/verify.yml) runs for pushes and pull
requests to both branches, manual dispatches, and the weekly schedule. It has no
path filters. Fork pull requests need no secrets. The workflow uses `contents: read`
and does not persist checkout credentials. Repository token permissions are
read-only, and Actions cannot create or approve pull requests. GitHub can require
approval before a fork workflow runs. Workflow approval does not replace PR review.

The [Pull request title workflow](.github/workflows/pr-title.yml) runs on pull
requests to `main` and `dev`. GitHub starts the workflow for the pull request
events `opened`, `edited`, `reopened`, `synchronize`, and `ready_for_review`.
The job name is `Conventional Commit title`. The workflow permission is `contents: read`.
It uses no secrets. It does not persist checkout credentials. Fork pull requests
receive the same read-only token. The workflow reads the title from the pull
request event file. A GitHub Actions expression does not receive the title.

The repository default squash commit title is the pull request title.
GitHub puts that title and the pull request number on the first line.
The default squash commit body is the commit messages.
The person who merges can edit the squash commit message before the merge.
The ruleset cannot lock the squash subject to the reviewed pull request title.
The person who merges must keep the reviewed pull request title as the squash subject.
The title check does not read edits on the merge form.

For [issue #3](https://github.com/daniel-kindl/tangopad/issues/3), `dev` initially
pointed to accepted bootstrap commit `282d48b0e1c5fe4d28287f06666023e41bc19e7f`.
The ruleset and default branch are remote settings. Repository files do not apply
them. Administrators can change these settings, so this document records the
configuration rather than an immutable guarantee.
