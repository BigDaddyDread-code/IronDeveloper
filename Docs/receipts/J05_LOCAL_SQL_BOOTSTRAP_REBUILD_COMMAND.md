# J05 - Local SQL Bootstrap/Rebuild Command

## Purpose

J05 adds a guarded local SQL command that can create or rebuild a developer-local IronDev database without hidden machine knowledge.

J05 is the first local developer command in Block J that may mutate local infrastructure, so the command is explicit, local-only, redacted, and guarded before any SQL is touched.

## Command Path

- `Scripts/local/sql-local.ps1`

Default invocation:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\sql-local.ps1 -CheckOnly
```

## Supported Switches

- `-CheckOnly`
- `-Create`
- `-Rebuild`
- `-ApplyLocalDevSetup`
- `-ServerInstance`
- `-DatabaseName`
- `-SetupScript`
- `-ConfirmRebuild`
- `-NonInteractive`
- `-Verbose`

`-Verbose` is provided by PowerShell advanced-function behavior.

J05 does not add SQL username, password, raw connection-string, or credential parameters.

## Default Check-Only Behavior

No switches defaults to check-only mode.

Check-only mode may report:

- repo root shape
- SQL tool availability
- SQL target classification
- database-name classification
- setup-script path classification
- create as not run
- rebuild as not run
- setup script application as not run
- next safe action

Check-only mode must not create databases, drop databases, apply setup scripts, call SQL mutation commands, write local overrides, change appsettings, write evidence, start API/UI, run source apply, run tests, run migrations, start Weaviate, or invoke governed product flows.

## Local Target Classification

Allowed local target forms include:

- `(localdb)\MSSQLLocalDB`
- `.`
- `(local)`
- `localhost`
- `localhost,<port>`
- `127.0.0.1`
- `127.0.0.1,<port>`
- `[::1]`
- `.\<local-instance>`
- `localhost\<local-instance>`

Remote, cloud, unknown, alias-like, or credential-shaped targets are rejected before SQL is touched.

Reason codes include:

- `SqlTargetLocal`
- `SqlTargetRemoteRejected`
- `SqlTargetUnknownRejected`
- `SqlTargetAzureRejected`
- `SqlTargetCredentialedRejected`
- `SqlToolMissing`

## Safe Database Name Rules

Allowed local database names must be deliberately local-looking:

- `IronDeveloper_Local`
- `IronDeveloper_Local_<suffix>`
- `IronDeveloper_Dev`
- `IronDeveloper_Dev_<suffix>`
- `IronDeveloper_Test`
- `IronDeveloper_J05_<suffix>`

Rejected names include:

- SQL system databases
- production-like names
- shared/staging/live/acceptance names
- the unsuffixed shared `IronDeveloper` name
- names containing unsafe SQL identifier characters or comment markers

Reason codes include:

- `DatabaseNameSafeLocal`
- `DatabaseNameSystemRejected`
- `DatabaseNameProductionLikeRejected`
- `DatabaseNameUnsafeCharactersRejected`
- `DatabaseNameNotLocalPatternRejected`

## Rebuild Confirmation Rule

`-Rebuild` requires the exact confirmation phrase:

```text
REBUILD <DatabaseName>
```

Example:

```text
REBUILD IronDeveloper_Local
```

Missing, partial, wrong, or mismatched confirmation fails before SQL is touched.

There is no `-Force` mode in J05.

## Setup Script Rule

The default setup script is:

- `Database/local_dev_setup.sql`

Setup script application requires `-ApplyLocalDevSetup`.

The setup script path must:

- resolve inside the repository
- be an explicit file
- not be a URL
- not escape the repository root
- stop the command if application fails

The command does not print SQL script contents.

## Redaction Behavior

The command must not print:

- raw connection strings
- passwords
- tokens
- full user-local paths
- SQL script contents
- local override contents
- server credentials

The command reports status and reason codes, not raw secrets.

## Tests Added

Added `IronDev.IntegrationTests/BlockJ05LocalSqlBootstrapCommandTests.cs`.

Coverage includes:

- script exists and docs reference it
- default check-only mode is non-mutating
- create/rebuild/setup modes are explicit
- remote, cloud, unknown, and credential-shaped SQL targets are rejected
- local SQL targets are classified local
- unsafe/system/production-like database names are rejected
- safe local database names are accepted
- rebuild requires exact confirmation phrase
- setup script paths cannot escape the repository
- output redacts fake sensitive values and user-local paths
- no authority/product-flow command surface is introduced
- no Weaviate, Docker, frontend, API, or UI start behavior is introduced
- J04 does not invoke J05 automatically
- receipt boundary text is present

## Validation Run

Local validation to run before merge:

- J05 focused tests
- J04 focused tests
- config-boundary lane
- G13 category contract tests
- C11 secret scan
- solution build
- `git diff --check`
- `git diff --cached --check`

GitHub CI must pass before merge.

## Boundary Statement

The local SQL command may create or rebuild a developer-local database. It is not evidence, approval, root safety proof, policy satisfaction, schema authority, or permission to mutate source, workflows, evidence, or shared SQL targets.

A successful SQL bootstrap means a local database was prepared. It does not mean the alpha loop has passed.

## Out Of Scope

J05 does not add:

- Weaviate bootstrap or rebuild
- environment doctor command
- alpha smoke execution
- API startup check
- frontend startup check
- production DB setup
- CI database provisioning redesign
- EF migration strategy
- SQL credential management
- Docker orchestration
- evidence writing
- source apply
- critic behavior
- approval behavior
- workflow behavior
- release behavior
- deployment behavior

Review line: Local SQL bootstrap prepares a disposable developer database. It does not prove product readiness.

Killjoy: A database rebuild command is a foot-gun unless it assumes the operator is one typo away from production.
