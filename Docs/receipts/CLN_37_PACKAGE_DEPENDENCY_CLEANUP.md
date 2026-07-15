# CLN-37 Package and Dependency Cleanup Receipt

**Status:** Historical receipt

**Date:** 15 July 2026

## Outcome

Two redundant .NET integration-test references and one unused direct Tauri npm dependency were removed. NuGet, npm, Cargo, SDK, and GitHub Actions drift was audited; vulnerable, deprecated, and major-version changes were retained as explicit isolated follow-ups.

## Evidence

- `Docs/cleanup/PACKAGE_DEPENDENCY_AUDIT.md`
- `IronDev.IntegrationTests/IronDev.IntegrationTests.csproj`
- `IronDev.TauriShell/package.json`
- `IronDev.TauriShell/package-lock.json`

## Boundary

This cleanup does not claim dependency security is complete. Known upgrade findings remain open and require isolated behavior/contract validation.
