# Repository agent guidance

Read [CONTRIBUTING.md](CONTRIBUTING.md) before changing this repository.
For ownership or dependency changes, read [architecture](docs/architecture.md)
and its linked ADRs. Use [eng/architecture.json](eng/architecture.json) as the
current dependency policy.

Keep bootstrap work within infrastructure scope. Add application behavior only
when the task authorizes that behavior. Introduce domain types and interfaces
through concrete use cases.

Run `dotnet msbuild eng/verify.proj` before reporting completion. Report the
actual result and any platform checks that remain untested.

Use the writing rules in CONTRIBUTING.md for technical prose. Use strict wording
for procedures, status reports, and agent instructions. Preserve exact API names,
commands, quoted text, and legal text.

Third-party skills are optional. See [tooling research](docs/tooling-research.md#agent-skill-integration)
when selecting Matt Pocock or pstack workflows. Follow repository rules if a skill
conflicts with them. Keep CI independent of locally installed skills.
