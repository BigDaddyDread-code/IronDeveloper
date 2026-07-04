# J02 - Development Local Override

## Purpose

Add a safe, documented, ignored local override path for developer-specific API settings.

J01 removed hardcoded workstation SQL configuration. J03 blocks local machine assumptions from being committed again. J02 gives developers the correct place to put machine-specific development settings without editing committed shared configuration.

## Files Changed

- `IronDev.Api/Program.cs`
- `IronDev.Api/appsettings.Development.Local.example.json`
- `Docs/local-development.md`
- `Docs/BACKEND_CONFIGURATION_DEPENDENCY_INVENTORY.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `IronDev.IntegrationTests/BlockJ02DevelopmentLocalOverrideTests.cs`
- `Docs/receipts/J02_DEVELOPMENT_LOCAL_OVERRIDE.md`

`.gitignore` already protected `appsettings.Development.Local.json`; J02 verifies that protection instead of changing it.

## How Local Override Loading Works

The API host calls `Program.AddDevelopmentLocalConfiguration(builder)` immediately after creating the `WebApplicationBuilder`.

When the host environment is `Development`, the API host adds this optional JSON source:

```text
appsettings.Development.Local.json
```

The file is optional and uses `reloadOnChange: true`. If it is absent, startup behavior remains unchanged.

The API host does not load this local override for Production, Staging, LocalTest, Test, CI, or other non-Development environments.

## Config Precedence

The API host keeps this precedence:

1. `appsettings.json`
2. `appsettings.Development.json`
3. `appsettings.Development.Local.json`
4. API user secrets, when available
5. environment variables
6. command-line arguments

J02 deliberately inserts the local override before user secrets, environment variables, and command-line sources. Local convenience must not outrank CI/runtime overrides.

## Ignored Local Files

The developer-local file is:

```text
IronDev.Api/appsettings.Development.Local.json
```

It must remain ignored and untracked. The tracked example file is:

```text
IronDev.Api/appsettings.Development.Local.example.json
```

The example contains blanks/placeholders only.

## Example Guidance

Developers may copy the example locally and fill in machine-specific values on their own machine only.

The committed example must not contain real server names, local user paths, local credentials, API keys, tokens, sandbox roots, evidence roots, or personal machine identifiers.

## Tests Added

`IronDev.IntegrationTests/BlockJ02DevelopmentLocalOverrideTests.cs`

The test class uses `[TestCategory("ConfigBoundary")]` and proves:

- `appsettings.Development.Local.json` is ignored and untracked.
- Development loads the optional local override when present.
- non-Development environments do not load the local override.
- missing local override files do not fail startup configuration.
- environment variables remain higher precedence than the local file.
- docs and example content contain no machine-specific values.
- this receipt states the local override boundary.

## Validation Run

- Focused J02 ConfigBoundary tests: passed, 6/6.
- Existing J01 ConfigBoundary tests: passed, 7/7.
- Existing J03 ConfigBoundary tests: passed, 7/7.
- C11 secret scanning regression: passed, 9/9.
- C13 production environment safety regression: passed, 11/11.
- `dotnet restore IronDev.slnx`: passed with existing package warnings.
- `dotnet build IronDev.slnx --no-restore`: passed, 0 errors / 7 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Boundary Statement

appsettings.Development.Local.json is developer convenience. It is not shared configuration, not evidence, not authority, and not a runtime contract.

A local override may describe a developer's machine locally. It must never be committed, used by CI as shared truth, or treated as permission to mutate SQL, Weaviate, source, evidence, or sandbox repositories.

## Out Of Scope

- No SQL database creation.
- No SQL rebuild command.
- No SQL migration or schema change.
- No Weaviate bootstrap or rebuild command.
- No environment doctor command.
- No production secret-management redesign.
- No deployment config.
- No Docker compose change.
- No runtime config summary.
- No source apply, rollback, critic, approval, release, deployment, or workflow behavior.

## Review Line

Local overrides belong outside committed truth.

## Killjoy Line

A local override file is convenience. If it becomes shared truth, you rebuilt the original bug.
