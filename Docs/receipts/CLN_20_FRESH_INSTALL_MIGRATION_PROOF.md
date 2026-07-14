# CLN-20 Fresh-Install Migration Proof Receipt

**Recorded:** 14 July 2026

## Purpose

Prove that the controlled migration path produces a usable IronDev installation from an empty, isolated test database.

## Proof Path

`Database/verify-fresh-install.ps1`:

1. creates a uniquely named test database through the existing clean-database verifier;
2. builds the base schema, applies the ordered migration manifest twice, and runs the migration verifier;
3. applies the bounded LocalTest seed;
4. builds and starts the real API process against that database;
5. logs in with the seeded user and selects the seeded tenant;
6. loads the seeded project through the tenant-scoped API; and
7. reads the canonical project Board as the core smoke.

The script removes its isolated database unless a containing CI lane explicitly owns cleanup with `-KeepDatabase`. Database deletion remains fenced to names ending in `_Test` or beginning with `IronDev_CI_`.

## CI Ownership

`Scripts/ci/run-platform-baseline-ci.ps1` invokes the fresh-install proof before its in-process API and frontend contract checks. The full SQL integration lane already owns that platform baseline.

## Tests Executed

- `Database/verify-fresh-install.ps1` — passed against a newly created LocalDB database; the isolated database was removed after the proof.
- `dotnet build IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-restore -m:1 -p:BuildProjectReferences=false -p:UseSharedCompilation=false -v:minimal` — passed with pre-existing analyzer and package warnings.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --no-restore --filter FullyQualifiedName~PlatformBaselineContractTests` — passed, 6 tests.

## Behaviour Preserved

- The ordered migration manifest remains the only migration path.
- SQL remains durable product truth.
- Tenant selection remains required before product data can be read.
- The LocalTest seed remains test-only and refuses non-test databases.

## Authority Boundary

A successful fresh installation is operational evidence. It does not approve a release, grant workflow authority, promote memory, or authorize source apply.
