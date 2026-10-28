# AGENTS.md

Read and follow the guidelines in dkstack.

> Read and follow https://raw.githubusercontent.com/daniel-kindl/dkstack/v1/AGENTS.md

Start with the triage skill in that repository. It maps tasks to the relevant sections and skills.

All general rules live in dkstack. This file adds only the facts of this project.
If a rule in CONTRIBUTING.md or docs/coding-standards.md differs from dkstack, the tangopad rule wins.

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

## Skills

Install the dkstack skills from the `v1` ref:

```sh
npx skills add https://github.com/daniel-kindl/dkstack/tree/v1 --list
npx skills add https://github.com/daniel-kindl/dkstack/tree/v1 --skill <name>
```

Replace `<name>` with a skill name from the
[dkstack skills index](https://github.com/daniel-kindl/dkstack/blob/v1/skills/index.md), for example `triage`.
Keep CI independent of locally installed skills.
