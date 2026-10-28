# AGENTS.md

Read and follow the guidelines in dkstack.

> Read and follow https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/AGENTS.md

Start with the triage skill in that repository. It maps tasks to the relevant sections and skills.

All general rules live in dkstack. This file adds only the facts of this project.
dkstack rules win. tangopad differs only through the exceptions in
[dkstack exceptions](#dkstack-exceptions). The rules in dkstack
[workflow/safety.md](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/workflow/safety.md) have no exceptions.

## Project

- Read [CONTRIBUTING.md](CONTRIBUTING.md) before you change this repository.
- Run `dotnet msbuild eng/verify.proj` before you report completion.
  Report the actual result and each platform check that remains untested.
- For ownership or dependency changes, read [architecture](docs/architecture.md) and its linked ADRs.
  [eng/architecture.json](eng/architecture.json) is the current dependency policy.
- Keep bootstrap work within infrastructure scope. Add application behavior only
  when the task authorizes that behavior. Introduce domain types and interfaces
  through concrete use cases.
- Use strict wording for procedures, status reports, and agent instructions.
  Preserve exact API names, commands, quoted text, and legal text.

## Safety

- Agents never use the owner bypass of the ruleset. See
  [GitHub integration safeguards](CONTRIBUTING.md#github-integration-safeguards).
  All bots use the `daniel-kindl` account, which has the owner bypass,
  so only this rule stops a bot from using it.
  This is a dkstack safety rule, not an exception.
- `.github/workflows/release.yml` ("Prepare draft release") is a release workflow.
  Only Daniel starts it. A release OK never covers this workflow.

## dkstack exceptions

Each entry names the dkstack rule, the tangopad rule and the reason.

### Branch flow

- dkstack rule: use squash merge only. Merge `main` into a branch to update it. See
  [review and merge](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/workflow/review-and-merge.md).
- tangopad rule: `dev` is the default branch. Branch from `dev`, target `dev`, and merge `dev`
  into a branch to update it. A reviewed release pull request from `dev` goes into `main`
  with a merge commit.
- Reason: `main` holds stable releases. `dev` integrates reviewed changes.
- Details: [CONTRIBUTING.md](CONTRIBUTING.md#prepare-a-change) and
  [release notes workflow](docs/release-notes.md#review-and-publish-a-release).

### Diagnostic exceptions

- dkstack rule: fix the root cause. Do not add exceptions. See
  [quality gates](https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/coding/quality-gates.md).
- tangopad rule: a narrow exception for one diagnostic is allowed. It needs a rule ID, the
  smallest scope, a reason and review in the issue or pull request.
- Reason: generated source and external or platform API contracts can need a narrow exception
  when the diagnostic cannot be corrected locally. dkstack `main` adds the same rule for the next
  dkstack release. Remove this entry when dkstack `v1` has that rule.
- Details: [coding standards](docs/coding-standards.md#exceptions).

## Skills

Install the dkstack skills from the `v1` ref:

```sh
npx skills add https://github.com/daniel-kindl/dkstack/tree/v1 --list
npx skills add https://github.com/daniel-kindl/dkstack/tree/v1 --skill <name>
```

Replace `<name>` with a skill name from the
[dkstack skills index](https://github.com/daniel-kindl/dkstack/blob/v1/skills/index.md), for example `triage`.
Keep CI independent of locally installed skills.
