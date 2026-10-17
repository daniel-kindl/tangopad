# Coding standards

## Verified diagnostic set

The supported toolchain is SDK 10.0.402, pinned in `global.json`, with C# 14.
`Directory.Build.props` applies nullable analysis, documentation generation,
`AnalysisLevel=10.0-all`, build-time style analysis, and warnings as errors to
production and test projects in Debug and Release. Analyzer execution is explicit.
The build commands also use `-warnaserror` to reject MSBuild task warnings.
No additional analyzer package is needed for the current checks.

The zero-diagnostic requirement covers compiler warnings, enabled SDK analyzer
warnings, and actionable information and style diagnostics checked by
`dotnet format --verify-no-changes --severity info`.
Selected style rules have explicit warning severities so builds reject violations,
including rules without automatic fixes. Hidden editor adornments, third-party IDE
extensions, and IDE-only suggestions outside this CLI set remain advisory.
The formatter cannot prove that every analyzer without a code fix emitted no information.
Build-enforced rules use warning severity to close that gap for the selected set.

### Audit at issue #32

The audit started at dev commit `586f3c5` on Linux with SDK 10.0.402.
The existing verification command passed with zero build warnings and no formatter changes.
The Windows locked-file test was skipped on Linux.
The initial command built only Release. Both configurations now build.

| Source | Inventory and treatment |
| --- | --- |
| C# compiler and nullable flow analysis | SDK compiler, nullable enabled, warnings treated as errors. CS1591 checks missing public API documentation. |
| SDK code-quality rules | `AnalysisLevel=10.0-all` selects the SDK's `analysislevel_10_all.globalconfig`. This file contains 280 warning rules and one disabled rule, CA1516. The SDK excludes CA1516, which recommends cross-platform intrinsics. There is no repository override. |
| SDK Roslyn style rules | `.editorconfig` retains the analyzer and Style category warning baseline. Explicit IDs preserve selected build rules. The formatter checks enabled diagnostics at info severity, including whitespace. |
| Package diagnostics | Avalonia, CommunityToolkit.Mvvm, and xUnit supply diagnostics with their centrally pinned packages. The same build and formatter gates cover their enabled diagnostics. |
| NuGet | Locked restore audits direct and transitive dependencies at low severity and above. |
| Repository overrides | No `NoWarn`, warning pragmas, suppression attributes, disabled nullability, or disabled analyzer entries were present in repository source. |
| SDK exclusions | Evaluated C# projects inherit exactly `NoWarn=1701;1702`. These SDK defaults exclude legacy assembly-reference unification warnings. Tests reject additional exclusions. `WarningsNotAsErrors` is empty. |
| Generated source | SDK and generator recognition controls generated-code analysis. Repository source has no general generated-file exclusion. Compiler diagnostics still apply. Do not label handwritten code as generated. |
| AXAML and resources | Compiled bindings, resource use, key uniqueness, and matching theme roles have architecture checks and headless control tests. |
| IDE-only presentation | Hints and refactorings without a CLI diagnostic are advisory. Member order and semantic ownership still require review. |

The SDK configuration file is the authoritative CA rule inventory for this version.
Its path is relative to the `Base Path` from `dotnet --info`:

```text
Sdks/Microsoft.NET.Sdk/analyzers/build/config/analysislevel_10_all.globalconfig
```

The compiler, analyzer assemblies, and their default severities change with the SDK.
An SDK update therefore requires a new baseline check.
Microsoft documents [analysis configuration and precedence](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/configuration-options)
and [build-time style analysis](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview).
Bulk settings do not replace an individual diagnostic override.

## C# conventions

| Concern | Convention and enforcement |
| --- | --- |
| Names | PascalCase types, methods, properties, and events. Interfaces start with `I`. Parameters use camelCase. Naming rules and IDE1006 enforce these choices. Existing instance fields use camelCase and static readonly fields use PascalCase. Field names remain a review convention. |
| Visibility | Explicit accessibility for non-interface members, IDE0040. Keep the public API small. |
| Layout | Four spaces, LF, final newline, file-scoped namespaces, braces, imports outside namespaces, System imports first. IDE0055, IDE0161, IDE0011, IDE0065, and the formatter enforce applicable rules. |
| Types and syntax | Explicit local types, IDE0008. Use target-typed `new` where the type is apparent, IDE0090. Prefer collection expressions when types match exactly. Methods use block bodies, IDE0022. Other syntax follows the SDK style baseline. |
| Immutability | Make fields readonly where possible, IDE0044. Other enabled SDK analyzers check immutable and readonly opportunities. Mutable document and ViewModel state remains valid. |
| Nullability | Nullable reference types stay enabled. Validate external inputs and preserve internal type guarantees. Never use `!` merely to hide a real nullable defect. |
| Async and cancellation | Pass cancellation through operations that can wait. Enabled SDK rules include CA2016 for forwarding tokens and CA2012 for ValueTask use. Async suffixes and correct synchronization-context decisions require review. |
| Unused code | Remove unused imports, IDE0005. IDE0060 checks unused parameters. Compiler and SDK rules check unused members and values where supported. Reflection and external entry points require review. |
| Documentation | Document public APIs with XML comments. Compiler diagnostics check missing API documentation and malformed comments. Tests also follow this rule. |
| Member order | Keep related members together. The SDK has no general member-order gate. No extra analyzer is added solely for ordering. |

## Presentation and ownership

[Architecture](architecture.md#file-placement) defines paths and namespaces.
The evaluated graph check enforces `eng/architecture.json` in Debug and Release.
Compiled API checks enforce the inner-module boundaries.
Namespaces follow meaningful directories where SDK IDE0130 applies.

AXAML compiles bindings by default. A markup check rejects local disabling of
compiled bindings and explicit `ReflectionBinding` expressions.
The compiler rejects invalid compiled binding paths.
[Presentation resources](presentation-resources.md) defines the checked properties
and the deliberate code-behind exceptions for runtime messages and protocol inputs.
Architecture tests scan all AXAML recursively. They reject selected presentation
literals, static theme brushes, duplicate shared keys, and inconsistent theme roles.
Resource dictionaries contain the intended token and text definitions.

ViewModels own state and commands without Avalonia controls, brushes, or resource lookup.
Headless tests exercise real bindings and controls. ViewModel tests run without Avalonia initialization.
These checks do not prove semantic ownership for every future method.
Reviewers must check new UI dependencies and directory placement.

## Exceptions

A new exception needs a rule ID, the smallest applicable scope, a reason, and review
in the issue or pull request. Record persistent exceptions here or beside the code.
Do not add broad pragmas, blanket `NoWarn`, global analyzer disabling, or a general
formatter exclusion. Do not suppress genuine defects to make verification pass.
Prefer correcting the source. Generated source and external or platform API contracts
can justify a narrow exception when the diagnostic cannot be corrected locally.
The SDK defaults listed above are the only current warning exclusions.

## Gate evidence

`QualityGateTests` creates isolated projects with copies of the repository SDK pin,
build properties, and EditorConfig. Valid source builds in Debug and Release.
Mutations must fail with CS8603, CA2201, IDE0008, and IDE1006 respectively.
A whitespace mutation must fail the info-level formatter gate without changing the file.
The verification policy test rejects removal or conditional bypass of required targets
and changes to the mandatory formatter and build commands.

`DependencyRulesTests` checks evaluated properties for every policy project in both
configurations. Forbidden-reference fixtures use the same dependency assertions as
real projects. `AxamlResourceTests` checks invalid markup with literal expected diagnostics.
These tests exercise the platform tools and existing policy checks without duplicating
C# analysis in a custom linter. They cannot prevent deliberate replacement of the tests.

The initial property test failed in both configurations before explicit analyzer
execution was added. All gate tests must pass with the final policy.
Managed binary reproducibility verification remains retired.
