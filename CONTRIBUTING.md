# Contributing

## Prepare a change

1. Create a GitHub Issue with a bounded outcome and acceptance criteria.
2. Assign the issue to an appropriate milestone from [the backlog](docs/backlog.md).
3. Create a short-lived `feat/<issue>-<name>`, `fix/<issue>-<name>`, or `chore/<issue>-<name>` branch from `dev`.
4. Read [architecture](docs/architecture.md) before changing module ownership or dependencies.
5. Implement the smallest change that satisfies the issue.

`main` holds stable releases. `dev` integrates reviewed changes. Target `dev` for
normal pull requests. Merge a reviewed release pull request from `dev` into `main`.
Use Semantic Versioning. Before 1.0, document breaking changes in each minor release.
The authoritative version is in `Directory.Build.props`.

Use Conventional Commits for commits and squash titles, such as
`chore(build): pin the .NET SDK`. Mark a breaking change with `!` or a
`BREAKING CHANGE:` footer. These naming rules receive human review during bootstrap.
No release automation runs yet.

## Verify a change

Run the single verification entry point from the repository root:

```sh
dotnet msbuild eng/verify.proj
```

The command stops at the first failing stage. It performs locked restore and
NuGet auditing for direct and transitive dependencies, format and analyzer
verification at info severity, Release compilation, and all tests. Build warnings
are errors. CI repeats the command on Linux, Windows, and macOS. TRX files appear
under `artifacts/test-results`.

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
behavior and platform-specific acceptance criteria. The blank bootstrap window
provides only a manual startup check.

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

Repository administrators must configure branch protection on GitHub. Require
pull requests, review, and all three `Verify` matrix checks for `main` and `dev`.
Create `dev` from the accepted bootstrap commit if the branch does not exist.
Local repository files do not apply these remote settings.
