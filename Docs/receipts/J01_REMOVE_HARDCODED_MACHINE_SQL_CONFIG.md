# J01 - Remove Hardcoded Machine SQL Config

## Purpose

Remove committed SQL configuration that assumed one developer workstation.

Committed config must describe safe defaults, not someone's workstation.

## Files Inspected

- `IronDev.Api/appsettings.json`
- `IronDev.Api/appsettings.Development.json`
- `IronDev.Api/appsettings.LocalTest.json`
- `IronDev.IntegrationTests/appsettings.Test.json`
- `IronDev.IntegrationTests.Api/appsettings.Test.json`
- `IronDev.IntegrationTests.Api/ApiTestBase.cs`
- `IronDev.IntegrationTests.Api/CorsPolicyTests.cs`
- `IronDev.IntegrationTests.Api/LocalTestEnvironmentSafetyTests.cs`
- `IronDev.IntegrationTests.Api/SecurityAuditLogBehaviorTests.cs`
- `IronDev.IntegrationTests.Api/SensitiveApiRateLimitTests.cs`
- `.gitignore`
- `Docs/local-development.md`
- `Docs/BACKEND_CONFIGURATION_DEPENDENCY_INVENTORY.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

## Hardcoded SQL Values Removed

- Removed a workstation-specific SQL server value from API development config.
- Removed a workstation-specific SQL server value from API LocalTest config.
- Removed a workstation-specific SQL server value from integration test config.
- Removed a workstation-specific SQL server value from API integration test config.
- Removed a workstation-specific SQL server value from API test-host helper overrides.
- Removed the shared SQL server placeholder from `IronDev.Api/appsettings.json`.

## Replacement Approach

- `IronDev.Api/appsettings.json` now leaves `ConnectionStrings:IronDeveloperDb` blank.
- Development, LocalTest, and test appsettings use the generic example `Server=(localdb)\MSSQLLocalDB`.
- API and integration test helpers prefer `ConnectionStrings__IronDeveloperDb` when supplied, then fall back to the generic LocalDB example.
- The replacement does not create, rebuild, migrate, seed, or validate a database.

## Local Developer Overrides

Machine-specific SQL settings belong outside committed config:

- Use `ConnectionStrings__IronDeveloperDb` for local shells, CI, and test runs.
- Use API user secrets for API-host development overrides.
- Do not commit workstation names, local usernames, SQL passwords, local admin credentials, or absolute developer paths.

The following future local override file names are ignored and must not be tracked:

- `appsettings.Development.Local.json`
- `appsettings.LocalTest.Local.json`
- `appsettings.Test.Local.json`

## Tests Added

- `IronDev.IntegrationTests/BlockJ01RemoveHardcodedMachineSqlConfigTests.cs`

The test uses `[TestCategory("ConfigBoundary")]` and verifies:

- tracked config files do not contain workstation SQL server names,
- tracked config files do not contain obvious SQL credential markers,
- tracked config files do not contain developer absolute path markers,
- shared appsettings does not carry shared SQL server truth,
- development/test config uses generic LocalDB examples only,
- local override files are ignored and not tracked,
- this receipt states the local SQL boundary.

## Validation

- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockJ01RemoveHardcodedMachineSqlConfigTests" --logger "console;verbosity=minimal"`: passed, 7/7.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BlockC11SecretScanningRegressionTests" --logger "console;verbosity=minimal"`: passed, 9/9.
- `dotnet build IronDev.slnx`: passed with existing warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Boundary Statement

Local SQL configuration is developer convenience. It is not authority, not evidence, and not a shared runtime contract.

No bootstrap, schema, Weaviate, authority, approval, critic, source-apply, or release behavior is changed.

## Review Line

Committed config must describe safe defaults, not someone's workstation.

## Killjoy Line

A hardcoded local SQL server is hidden infrastructure authority.
