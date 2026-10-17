# Architecture

Tangopad uses a modular monolith. Orthogonality takes priority over code reuse.
Each module owns a distinct decision. Dependencies point toward the domain and
application contracts. The domain owns no UI, parser, renderer, or filesystem implementation.

## Current boundaries

The solid arrows below are project references. Core owns plain-text document
behavior. Application owns plain-text load and save orchestration and its persistence port.

```mermaid
flowchart LR
    App[Editor.App] --> Application[Editor.Application]
    Application --> Core[Editor.Core]
    Infrastructure[Editor.Infrastructure] --> Application
    App --> Avalonia[Avalonia desktop and Fluent theme]
    ArchitectureTests[Editor.Architecture.Tests] -. evaluates MSBuild .-> Policy[eng/architecture.json]
    ArchitectureTests --> BuildVerification[BuildVerification]
    ApplicationTests[Editor.Application.Tests] --> Application
    CoreTests[Editor.Core.Tests] --> Core
    InfrastructureTests[Editor.Infrastructure.Tests] --> Infrastructure
    SmokeTests[Editor.Desktop.SmokeTests] --> App
```

| Module | Ownership | Current implementation |
| --- | --- | --- |
| Editor.App | Presentation and composition | Avalonia startup and an empty window |
| Editor.Application | Use cases and contracts needed by those use cases | DocumentSession load and save through ITextDocumentStore |
| Editor.Core | Document rules and domain types | TextDocument identity, exact text, and clean baseline |
| Editor.Infrastructure | Local persistence | Strict UTF-8 load and save through same-directory replacement |
| Editor.Infrastructure.Tests | Real filesystem integration | Encoding, replacement, failure, cancellation, cleanup, and session state |
| Editor.Application.Tests | Application behavior | Exact load and save, destinations, errors, cancellation, and pending-save edits |
| Editor.Core.Tests | Document behavior | Identity, edits, acknowledgement, reversion, Unicode, and null input |
| Editor.Architecture.Tests | Repository dependency constraints | Solution inventory and evaluated dependency tests |
| BuildVerification | Managed build reproducibility | Creates isolated builds and compares project DLL and portable PDB hashes |
| Editor.Desktop.SmokeTests | Native startup of the blank shell | Starts the built Editor.App executable, closes its native window, and checks exit code 0 |

`Program.Main` is the single composition root. `App` performs framework lifecycle
setup only. The document service receives its persistence port directly. No service container is necessary.
Application and Core reference no NuGet packages. Production code never references tests.
Infrastructure references Application only and adds no packages. App does not yet compose the adapter.
`Editor.Desktop.SmokeTests` references `Editor.App` so the shell builds first.
The test starts that executable. It does not call shell types.

## Future boundaries

Create a module when a real use case requires it. Update the dependency policy
and ADR before adding its implementation. These are intended permissions, not existing references:

- `Editor.Formats.Markdown` may reference Application and Core. It owns Markdown format policy and parser adaptation.
- App may reference concrete adapters to compose them.
- Adapters cannot reference App or one another.
- Core references no project. Application references only Core.

Format handling identifies format-specific capabilities. Parsing converts source
text into a format representation. Rendering converts a representation into output.
Keep those responsibilities separate when implemented. Do not expose Avalonia or
Markdig types in Core or Application contracts. Add a rendering module only when
its actual consumers establish the boundary. Plain text does not need a format module yet.

## Enforced constraints

[eng/architecture.json](../eng/architecture.json) is the authoritative current
project and direct-package graph. Architecture tests evaluate MSBuild in Debug
and Release. They compare exact project and package references with the policy.
They reject direct assembly references and framework references beyond the .NET base framework.
They also require the solution and policy to contain every project under `src`, `tests`, and `eng`.
The exact acyclic policy excludes cycles and production references to tests.

`BuildVerification` is an engineering tool with no package or production project references.
Architecture tests reference the tool to test its comparison behavior.

These checks include imported and conditional MSBuild items for the evaluated
configurations on the host OS. The CI matrix evaluates all three host systems.
They do not inspect arbitrary runtime-specific configurations.
API-level tests inspect the compiled Core and Application assemblies.
Core may reference only `System.Runtime`.
Application may reference only `Editor.Core` and `System.Runtime`.
That check rejects Avalonia, rendering libraries, and external format parsers.
The namespace check permits only the allowlists in ADR 0004 and ADR 0005.
That check rejects `System.IO`, including filesystem types from the base runtime.
The native-import check rejects methods that import native code.
The type-definition check requires inner modules to define types in their own namespaces.
See [ADR 0004](adr/0004-plain-text-document.md) and
[ADR 0005](adr/0005-document-persistence-use-cases.md) for the allowlists and their limits.
Review semantic ownership alongside automated dependency checks.

## Design policy

Keep one authoritative rule in its owning module. Do not extract a shared
abstraction because two code blocks look similar. Prefer a smaller public API
that hides a concrete implementation decision.

Use patterns only for an observed need. Strategy can separate interchangeable
format policies. Adapter can isolate a parser or filesystem library. Commands,
observers, factories, decorators, and DI containers remain deferred until their
cost solves a demonstrated problem. The bootstrap introduces none of these.

A new requirement can reveal a wrong boundary. Change the design and migrate
callers together. Avoid permanent compatibility layers inside this monolith.

See [ADR 0001](adr/0001-module-boundaries.md) for the alternatives and decision.
See [ADR 0004](adr/0004-plain-text-document.md) for document use cases and invariants.
See [ADR 0005](adr/0005-document-persistence-use-cases.md) for persistence contracts and save concurrency limits.
See [ADR 0006](adr/0006-local-text-persistence.md) for UTF-8, replacement, metadata, and platform limits.
