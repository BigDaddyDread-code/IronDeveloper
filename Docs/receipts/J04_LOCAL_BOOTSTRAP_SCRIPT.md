# J04 - Local Bootstrap Script

## Purpose

J04 adds a safe local bootstrap script for developer convenience.

The script gives a developer one predictable first command for checking local repo shape, tool presence, ignored local override status, and safe next actions without depending on hidden machine knowledge.

## Script Path

- `Scripts/local/bootstrap-local.ps1`

Default invocation:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\local\bootstrap-local.ps1 -CheckOnly
```

## Supported Switches

- `-CheckOnly`
- `-Prepare`
- `-CreateLocalOverride`
- `-RestoreDotNet`
- `-InstallFrontend`
- `-NonInteractive`
- `-Verbose`

`-Verbose` is provided by PowerShell advanced-function behavior.

## Default Behavior

No switches defaults to check-only mode.

Check-only mode may report:

- repo root shape
- .NET SDK command presence
- Git command presence
- frontend package status
- local override presence, ignored status, and tracked status
- J08 config summary contract availability
- J10 root safety availability or `NotEvaluated`
- SQL bootstrap as not run
- Weaviate bootstrap as not run
- next safe action

Check-only mode must not create files, create directories, restore packages, install frontend packages, start services, call SQL, start or rebuild Weaviate, write evidence, mutate source, mutate sandbox repositories, invoke governed product flows, or continue workflow.

## What The Script May Do

With `-Prepare -CreateLocalOverride`, the script may copy:

- `IronDev.Api/appsettings.Development.Local.example.json`

to:

- `IronDev.Api/appsettings.Development.Local.json`

only when the target file is absent.

It must not overwrite an existing local override.

With `-Prepare -RestoreDotNet`, the script may run:

- `dotnet restore IronDev.slnx`

With `-Prepare -InstallFrontend`, the script may run frontend package installation inside:

- `IronDev.TauriShell`

It must not start the API, start the UI, call Docker, call SQL, call Weaviate, apply source, run rollback, release, deploy, write evidence, or continue workflow.

## What The Script Must Not Do

J04 does not add:

- SQL bootstrap
- SQL rebuild
- database creation
- schema changes
- SQL migrations
- Weaviate bootstrap
- Weaviate rebuild
- Docker compose orchestration
- environment doctor command
- endpoint exposure
- startup logging
- production configuration
- source apply behavior
- rollback behavior
- approval behavior
- critic authority behavior
- workflow continuation behavior
- release behavior
- deployment behavior
- evidence writing
- sandbox repository mutation

## Redaction Behavior

The script must not print:

- raw connection strings
- API keys
- JWT keys
- token values
- authorization headers
- local override contents
- full user-local paths
- secret-shaped values

The script reports status, not raw configuration contents.

## Interaction With J08 Config Summary

J08 provides a Core-only redacted configuration summary contract.

J04 does not invoke compiled Core services directly. The script reports whether the J08 Core contract is present and leaves actual summary generation to a future host, endpoint, or developer-environment doctor command.

J04 must not fake a config summary.

## Interaction With J10 Root Safety

J10 root-safety validation is implemented as a separate Core contract.

J04 reports root-safety contract availability and keeps root safety as `NotEvaluated` unless an explicit validator result is supplied elsewhere. It does not inspect, create, clean, validate, or bless roots.

J04 must not fake root safety.

## Tests Added

Added `IronDev.IntegrationTests/BlockJ04LocalBootstrapScriptTests.cs`.

Coverage includes:

- script exists and local docs reference it
- default check-only mode does not create local override files or run restore/install/SQL/Weaviate/evidence paths
- script source avoids committed secrets and local machine names
- forbidden commands and unguarded actions are absent
- existing local override is not overwritten
- local override template is copied only with explicit prepare/create switches
- check-only output does not echo fake sensitive values or user-local paths
- non-authority language is present in script output and docs
- receipt boundary text is present
- no runtime authority surface is added

## Validation Run

Local validation to run before merge:

- J04 focused tests
- J01/J02/J03/J08 focused config-boundary tests
- G13 category contract tests
- C11 secret scan
- solution build
- `git diff --check`
- `git diff --cached --check`

GitHub CI must pass before merge.

## Boundary Statement

The local bootstrap script prepares local convenience. It is not evidence, approval, root safety proof, policy satisfaction, or permission to mutate source, SQL, Weaviate, evidence, or sandbox repositories.

## Out Of Scope

J04 does not prove:

- database connectivity
- SQL schema health
- Weaviate availability
- root safety
- config correctness
- API startup health
- frontend startup health
- source safety
- approval
- policy satisfaction
- evidence validity
- release readiness
- deployment readiness
- workflow readiness

Review line: local bootstrap helps a developer stand up. It does not bless where they stand.

Killjoy: a green setup check is still not authority.
