## Change

Describe the problem, resulting behavior, and linked issue.
State directory placement and any namespace or public API changes.

## Changelog

Use `Changelog: updated` when CHANGELOG.md records a user-visible change.
Otherwise, use `Changelog: none.` followed by the reason.

## Verification

Record the result of `dotnet msbuild eng/verify.proj` and relevant behavior checks.
State platform limits. Link an ADR if the change alters a module boundary.
