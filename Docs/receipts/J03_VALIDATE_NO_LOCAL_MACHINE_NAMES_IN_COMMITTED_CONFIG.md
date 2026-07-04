# J03 Validate No Local Machine Names In Committed Config

## Purpose

J03 adds a repository hygiene guard that prevents developer-local machine names, absolute user paths, local SQL instance assumptions, tracked local override files, and credential-shaped local config from entering committed tracked files.

Machine-local assumptions must fail before review, not after another developer's machine breaks.

## Files And Classes Scanned

The guard uses `git ls-files -z` so it scans tracked files only.

The scanner covers text-like committed files including:

- `.json`, `.jsonc`, `.config`, `.props`, `.targets`
- `.csproj`, `.sln`, `.slnx`
- `.ps1`, `.psm1`, `.sh`, `.cmd`, `.bat`
- `.yml`, `.yaml`, `.env.example`
- `.md`, `.ts`, `.tsx`, `.cs`
- `.gitignore`

Generated or output folders are excluded:

- `bin/`, `obj/`, `node_modules/`, `dist/`, `build/`, `out/`
- `coverage/`, `TestResults/`, `artifacts/`, `.git/`
- `tools/dogfood/proofs/`, because it is generated proof output and not shared source configuration.
- `tools/dogfood/knowledge/`, because it is a generated knowledge mirror of docs metadata and is not shared source configuration.

The scanner does not scan SQL schema or migration scripts. J03 is a config hygiene guard, not a SQL migration/schema slice.

## Marker Categories Enforced

The tests reject:

- Windows workstation markers such as desktop/laptop host prefixes, personal workstation prefixes, and known old host fragments.
- Machine-specific SQL instance assumptions such as named workstation SQL instances and workstation-bound `Server=` / `Data Source=` values.
- Developer absolute user paths for Windows, macOS, Linux, and `file:///` Windows path forms.
- Credential-shaped local config assignments such as password, token, API key, secret, and `sa` connection-string user markers.

`(localdb)\MSSQLLocalDB` remains allowed as a portable local example.

## Exception Policy

Exceptions must be:

- path-specific
- marker-specific
- reasoned in the test source
- denied for real workstation names
- denied for real developer absolute paths
- denied for machine-specific SQL instance assumptions

The only current exception is the existing GitHub Actions SQL Server service connection string in `.github/workflows/sql-integration-ci.yml`. It is a fake, run-scoped CI service credential used by the SQL integration lane. C11 remains the secret-scanning authority.

A sample may show a placeholder. It may not preserve a real machine.

## Local Override Files Protected

The test fails if any tracked file ends with:

- `appsettings.Development.Local.json`
- `appsettings.LocalTest.Local.json`
- `appsettings.Test.Local.json`
- `.env`
- `.env.local`

## Tests Added

`IronDev.IntegrationTests/BlockJ03NoLocalMachineNamesInCommittedConfigTests.cs`

The test class adds:

- `J03_TrackedRepositoryFiles_DoNotContainLocalMachineNames`
- `J03_TrackedRepositoryFiles_DoNotContainDeveloperAbsolutePaths`
- `J03_TrackedRepositoryFiles_DoNotContainMachineSpecificSqlInstances`
- `J03_LocalOverrideFilesAreNotTracked`
- `J03_DocsUsePlaceholdersNotRealMachines`
- `J03_ReceiptStatesConfigBoundary`
- `J03_ScannerDoesNotPassVacuously`

## Validation Run

- Focused J03 ConfigBoundary tests: passed, 7/7.
- Existing J01 ConfigBoundary tests: passed, 7/7.
- C11 secret scanning regression: passed, 9/9.
- `dotnet restore IronDev.slnx`: passed with existing package warnings.
- `dotnet build IronDev.slnx --no-restore`: passed, 0 errors / 5 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Boundary Statement

Local machine names, local paths, and local SQL instances are developer-local facts. They are not shared configuration, not evidence, not authority, and not a runtime contract.

Generic examples are allowed only when they are portable or placeholder-based. Real developer machines are never allowed as examples.

## Out Of Scope

- No runtime behavior changes.
- No bootstrap behavior changes.
- No schema or SQL migration changes.
- No SQL bootstrap or SQL rebuild behavior.
- No Weaviate bootstrap or rebuild behavior.
- No environment doctor command.
- No evidence/workspace/sandbox root safety changes.
- No secret-management redesign.
- No Docker compose changes.
- No CI credential wiring redesign.
- No production deployment config.
- No authority, approval, critic, source-apply, rollback, release, deploy, or workflow behavior changes.
