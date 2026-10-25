# Dependency maintenance

[Dependabot configuration](../.github/dependabot.yml) assigns NuGet and GitHub
Actions to the `weekly-dependencies` multi-ecosystem group. Both entries use
`patterns: ['*']`. The group checks for updates each Monday at 06:00 UTC.
Scheduled version-update PRs target `dev`. The group uses the `chore(deps)`
commit prefix and the existing `dependencies` label.

The intended result is one PR containing eligible updates from either or both
ecosystems. A run with no eligible updates needs no PR. Unsupported manifests,
resolution failures, and separate security updates prevent an unconditional
one-PR guarantee. Check failed update jobs instead of assuming all dependencies
are current. See GitHub's [multi-ecosystem setup](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/configuring-multi-ecosystem-updates)
and [group settings](https://docs.github.com/en/code-security/concepts/supply-chain-security/multi-ecosystem-updates#configuration-merging-behavior).

## Coverage and limits

The NuGet entry starts at `/`, where `Tangopad.slnx` lists every project under
`src/`, `tests/`, and `eng/Editor.UpdateProbe/`. Direct package references use
versions from `Directory.Packages.props`. Each project commits a
`packages.lock.json` file containing its resolved direct and transitive packages.
Review all affected lock files with each central version change.

NuGet version updates maintain explicitly declared dependencies. They do not
independently upgrade every transitive package. Update the parent dependency
when a transitive package needs a fix. Dependabot can also omit downstream lock
changes with central package management and project references. See the
[reported limitation](https://github.com/dependabot/dependabot-core/issues/13950).
Locked restore must pass before merge. Never disable it to accept an update.

The Actions entry at `/` covers references in `.github/workflows/`.
Keep action references pinned to full commit SHAs when reviewing updates.
See GitHub's [options reference](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference)
for directory discovery and dependency selection rules.

The group does not coordinate arbitrary source pins or release metadata.
`global.json` requires a separate SDK update. Velopack updates need review of
`Directory.Packages.props`, `.config/dotnet-tools.json`, and
`eng/velopack/source.json`, including the source archive hash and recovery patch.
Follow [release distribution](release-distribution.md) for that coordination.
An automated package or tool update alone does not complete it.

## Security and merge policy

Security update PRs use the repository's default branch. The default branch is
currently `dev`, so both update types currently target the same branch.
If the default branch changes, scheduled version updates still target `dev`.
Security updates follow the new default branch. Group settings do not configure
security updates. See GitHub's [target-branch rules](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference#target-branch).

Keep Dependabot alerts and security updates enabled in repository settings.
Handle vulnerability alerts and separate security PRs promptly. Do not wait for
Monday or suppress an alert to preserve the single-PR preference.
NuGet auditing checks direct and transitive dependencies at every severity during
verification. A lack of GitHub alerts does not prove that transitive packages
are safe. GitHub's [dependency graph support](https://docs.github.com/en/code-security/reference/supply-chain-security/dependency-graph-supported-package-ecosystems)
describes its NuGet detection limits.

Dependency PRs require review and the existing hosted Linux, Windows, and macOS
checks. Do not enable automatic merging. Preserve architecture policy,
warnings-as-errors, locked restore, and NuGet auditing.

## Check an update

1. After merging the configuration into `dev`, open **Insights > Dependency graph > Dependabot** on GitHub.
2. Check that GitHub recognizes `weekly-dependencies` and both ecosystems without configuration errors.
3. Inspect the next scheduled run and its logs. Check that eligible NuGet and Actions updates share a PR targeting `dev`.
4. Check the PR title, `dependencies` label, central versions, action SHAs, and every affected project lock file.
5. If locked restore fails, identify the mismatch first. For an intentional package update with incomplete lock files, use the PR branch. Run `dotnet restore Tangopad.slnx --force-evaluate`.
6. Review and commit the resulting lock files with the intended version changes. Check downstream projects and `eng/Editor.UpdateProbe/` too.
7. Run `dotnet msbuild eng/verify.proj`. Require zero diagnostics and passing hosted checks on all three operating systems before merge.

Local YAML or schema validation cannot prove that the hosted updater recognizes
the group or creates a combined PR. Record those results from the hosted run.
