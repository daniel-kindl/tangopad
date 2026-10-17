# ADR 0001: Reserve domain and application assemblies

Status: Accepted

Date: 2026-10-17

## Context

The editor needs independent domain, application, presentation, format, and
persistence decisions. Bootstrap scope excludes document behavior and speculative APIs.

## Alternatives

Candidate A creates App, Application, and Core assemblies. Candidate B creates
only App and defers all other production assemblies. Both candidates defer
persistence, Markdown, and their dependencies. Separate design reviews compared
enforceable isolation, speculative APIs, maintenance cost, and readiness for incremental work.

A five-module initial layout was also considered. Empty infrastructure and format
assemblies would add maintenance without an implementation decision to isolate.

## Decision

Use Candidate A with the dependency graph in [architecture](../architecture.md).
The two empty assemblies establish compiler and test boundaries without placeholder
types. This satisfies the explicit separation requirement at a cost of two small project files.
An independent comparison selected the same base after considering Candidate B.

Adopt Candidate B's evaluated MSBuild checks, strict project inventory, deferred
packages, and single cross-platform verification entry point. Keep architecture
tests independent of production assemblies. Test repository policy through its
actual MSBuild consumer rather than through XML text alone.

## Consequences

The bootstrap has no usable domain API. Feature work must introduce real types
and behavior tests. Empty Core and Application assemblies reserve ownership only.
Infrastructure, Markdown, and additional test projects appear when concrete behavior requires them.

Package and reference checks cannot enforce semantic purity within .NET base APIs.
API-level rules must accompany future domain code. No placeholder abstraction
claims to solve persistence, parsing, rendering, or extensibility now.
