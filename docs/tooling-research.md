# Bootstrap tooling decisions

Research date: 2026-10-17. This document records the bootstrap dependency review.
Package versions below are stable releases. NuGet package indexes resolve differences
between cached search results and current package pages.

## Selected tooling

| Component | Version | Decision and compatibility |
| --- | --- | --- |
| .NET SDK | 10.0.402 | Pin the SDK and target `net10.0`. The SDK includes C# 14 and runtime 10.0.13. |
| Avalonia.Desktop | 12.1.4 | Provides the desktop platform backends and references Avalonia. Includes a `net10.0` target. |
| Avalonia.Themes.Fluent | 12.1.4 | Supplies the theme for the blank bootstrap window. Keep all Avalonia packages on one version. |
| xunit.v3 | 4.0.2 | Supports .NET 8 and later. The package name remains `xunit.v3` at version 4.0.2. |

Microsoft lists SDK 10.0.402 for Linux, Windows, and macOS. Its release date is
2026-10-13. [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
provides the SDK, runtime, and language versions.

The Avalonia package metadata lists desktop backends and framework targets.
Sources: [Avalonia.Desktop 12.1.4](https://www.nuget.org/packages/Avalonia.Desktop/12.1.4)
and [Avalonia.Themes.Fluent 12.1.4](https://www.nuget.org/packages/Avalonia.Themes.Fluent/12.1.4).

The bootstrap selects Microsoft Testing Platform (MTP). Current `xunit.v3`
includes MTP v2 support transitively. Test projects use `OutputType` `Exe` and
`UseMicrosoftTestingPlatformRunner` `true`. The `global.json` field `test.runner`
selects `Microsoft.Testing.Platform` for `dotnet test`.
Sources: [xUnit MTP integration](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform)
and [xunit.v3 4.0.2](https://www.nuget.org/packages/xunit.v3/4.0.2).

Restore and build succeeded with the selected dependency versions during bootstrap.
The first test invocation exposed a runner incompatibility. Its transitive
`Microsoft.Testing.Platform.MSBuild` 2.5.1 rejected the VSTest target under .NET 10.
Microsoft documents removal of that legacy execution path for MTP v2 on .NET 10.
Native MTP mode follows the supported configuration.
Source: [Microsoft test runner compatibility](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-with-dotnet-test).

The repository therefore does not reference `Microsoft.NET.Test.Sdk` or
`xunit.runner.visualstudio`. Test reports use xUnit's built-in `--report-xunit-trx`
option. This option requires no separate TRX extension package.
Source: [xUnit MTP report options](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform).
Full verification results belong to the bootstrap completion report.

NuGet indexes independently list these versions:
[Avalonia.Desktop index](https://api.nuget.org/v3-flatcontainer/avalonia.desktop/index.json),
[Fluent index](https://api.nuget.org/v3-flatcontainer/avalonia.themes.fluent/index.json),
and [xUnit index](https://api.nuget.org/v3-flatcontainer/xunit.v3/index.json).

## Deferred dependencies

| Component | Current stable version | Reason to defer |
| --- | --- | --- |
| Avalonia.AvaloniaEdit | 12.0.0 | Required when text editing starts. No editing control belongs in the bootstrap. |
| Markdig | 1.4.1 | Required when Markdown parsing starts. No parser belongs in the bootstrap. |

[Avalonia.AvaloniaEdit 12.0.0](https://www.nuget.org/packages/Avalonia.AvaloniaEdit/12.0.0)
includes `net8.0` and `net10.0` targets. It requires Avalonia 12.0.0 or later.
The selected Avalonia version satisfies that declared dependency.
[Markdig 1.4.1](https://www.nuget.org/packages/Markdig/1.4.1) includes a `net10.0`
target without package dependencies for that target. Neither compatibility claim
proves future editor behavior or Markdown rendering. Verify those behaviors when
contributors add the corresponding feature.

Defer DI containers, MVVM toolkits, plugin discovery, architecture test libraries,
and coverage collectors until a concrete requirement needs them. SDK analyzers,
`dotnet format`, and xUnit suffice for the initial verification infrastructure.

## Agent skill integration

[Matt Pocock's skills](https://github.com/mattpocock/skills) cover domain terminology,
ADRs, architectural review, research, and TDD. These practices fit the project.
Its setup skill selects the issue tracker, labels, and document locations. Use
GitHub Issues and the repository's existing documentation paths if contributors enable the skill.
The upstream README warns that multiple installation methods duplicate skills.

[Poteto's pstack](https://github.com/backnotprop/pstack) provides optional engineering
workflows. [`/how`](https://github.com/cursor/plugins/blob/main/pstack/skills/how/SKILL.md)
explains current code and module ownership.
[`/why`](https://github.com/cursor/plugins/blob/main/pstack/skills/why/SKILL.md)
investigates design reasons through source history and other available evidence.
ADRs provide useful evidence for `/why` before substantial history exists.
These skills use agent orchestration and environment-specific tool settings.
Review those settings before use in a different agent environment.

Keep third-party skills optional. Do not copy their instruction sets into this
repository or require them for CI. Repository `AGENTS.md`, architecture documents,
and executable checks remain authoritative for project rules. Contributors can
use installed skills when those skills follow the project rules. This avoids
competing TDD, review, and documentation instructions.

Use ASD-STE100 principles for technical prose: short sentences, explicit actors,
consistent terms, and one instruction per sentence. The
[official ASD-STE100 site](https://www.asd-ste100.org/) owns the standard.
These writing practices do not establish formal compliance with its dictionary.
Preserve exact API names, commands, quotations, and legal text.

## Platform verification limits

[Avalonia's platform policy](https://docs.avaloniaui.net/docs/supported-platforms)
defines supported desktop systems and Linux native dependencies. A successful
build does not prove native window behavior on every operating system. CI must
build and test on Linux, Windows, and macOS. A blank-window startup check can verify
the available local desktop environment. Packaging, signing, installers, and
broader native UI checks remain future work.
