# IronDev CI Execution Map

**Status:** Canonical executable CI map
**Verified against:** CLN-07 documentation contract lane
**Last verified:** 13 July 2026

This map describes commands GitHub Actions actually executes. Workflow names, test discovery, category listing, local scripts, and manual evidence do not count as execution unless a runner invokes them.

## Shared Contract

All eight pull-request workflows:

- run from checked-out repository content;
- emit evidence beneath `artifacts/ci/{lane}`;
- run `Scripts/ci/test-ci-evidence-artifact-safety.ps1` even when the primary lane fails;
- upload sanitized evidence for 14 days only when artifact safety passes;
- cancel an older run for the same pull-request ref;
- report evidence, not approval, release readiness, deployment readiness, or execution authority.

No workflow is triggered by a push to `main`. Every workflow supports `workflow_dispatch`; pull-request triggers are listed below.

## Workflow Summary

| Workflow | Runner | Pull-request target | Owning script | Executed surface | SQL | Evidence root |
| --- | --- | --- | --- | --- | --- | --- |
| `fast-unit-ci` | Windows | `main`, legacy governance rollup | `run-fast-unit-ci.ps1` | Entire `IronDev.UnitTests` project, no filter | No | `artifacts/ci/fast-unit` |
| `skeleton-run-ci` | Windows | `main`, legacy governance rollup | `run-skeleton-run-ci.ps1` | `IronDev.IntegrationTests` where `TestCategory=SkeletonRun` | No | `artifacts/ci/skeleton-run` |
| `governance-boundary-ci` | Windows | `main` | `run-governance-boundary-ci.ps1` | Named governance/security/static suites across integration and API projects | No | `artifacts/ci/governance-boundary` |
| `sql-integration-ci` | Ubuntu + SQL Server 2022 | `main` | `run-sql-integration-ci.ps1` | Connectivity smoke and seven SQL governance-store suites | Yes | `artifacts/ci/sql-integration` |
| `full-sql-integration-ci` | Ubuntu + SQL Server 2022 | `main`, legacy governance/CI branches | `run-full-sql-integration-ci.ps1` | Clean migration/API baseline plus named SQL, release, demo, repair, revision, category, catalog, and secret-scan proofs | Yes | `artifacts/ci/full-sql-integration` |
| `frontend-contract-ci` | Windows | `main` | `run-frontend-contract-ci.ps1` | TypeScript type-check and two OpenAPI/generated-client drift checks | No | `artifacts/ci/frontend-contract` |
| `frontend-behavior-ci` | Windows | `main` | `run-frontend-behavior-ci.ps1` | Production Vite build and 158 current-product Playwright tests in 18 explicit files | No | `artifacts/ci/frontend-behavior` |
| `documentation-contract-ci` | Windows | `main` | `run-documentation-contract-ci.ps1` | Documentation inventory, links, identities, status banners, terminology, product routes, and canonical references | No | `artifacts/ci/documentation-contract` |

## Lane Detail

### `fast-unit-ci`

Execution:

```powershell
dotnet restore IronDev.UnitTests/IronDev.UnitTests.csproj
dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore
dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build
```

The lane fails when the test count is zero. It produces a TRX file, test-count data, timing data, the lane summary, and the shared evidence summary. It has no external service or seed dependency.

### `skeleton-run-ci`

Execution:

```powershell
dotnet restore IronDev.IntegrationTests/IronDev.IntegrationTests.csproj
dotnet build IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-restore
dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "TestCategory=SkeletonRun"
```

The selected tests use in-memory stores and do not inherit the SQL integration base. The script fails when zero tests are selected and records total, passed, and failed counts. CLN-00 established a baseline of 180 executed and 180 passed.

### `governance-boundary-ci`

The workflow checks out full Git history, restores and builds `IronDev.slnx`, then executes these filter groups:

| Group | Project | Filter ownership |
| --- | --- | --- |
| B-series authority profiles | `IronDev.IntegrationTests` | Exact `BlockB01`, `B03` through `B12` class-name filters |
| Compatibility authority | `IronDev.IntegrationTests` | Exact `BlockBQ` through `BlockBU` class-name filters |
| Security boundaries | `IronDev.IntegrationTests` | C11-C16, J09, provisioning root, and work-tree probe filters |
| Static boundaries | `IronDev.IntegrationTests` | `TestCategory=StaticBoundary` |
| Migration contract | `IronDev.IntegrationTests` | `ApplyMigrationsScriptContractTests` |
| Demo seed contract | `IronDev.IntegrationTests` | `DemoSeedScriptContractTests` |
| API boundaries | `IronDev.IntegrationTests.Api` | Operational readiness, debugging, runs, continuation, and environment-isolation class filters |
| CLI boundaries | `IronDev.IntegrationTests` | `TestCategory=ApiCliContract` or `ApiCliReleaseGate` |

Each group executes separately and produces its own TRX artifact. Full-history checkout is load-bearing because changeset-shape guards compare `origin/main...HEAD`.

### `sql-integration-ci`

The workflow starts SQL Server 2022 and creates an `IronDev_CI_*` database identity. It restores and builds the solution before running:

1. `BlockC02SqlServerConnectivitySmokeTests`, with bounded startup retry.
2. `AcceptedApprovalSqlStoreTests`.
3. `PolicySatisfactionSqlStoreTests`.
4. `ApplyDryRunStoreTests`.
5. `DryRunReceiptStoreTests`.
6. `PatchArtifactStoreTests`.
7. `WorkflowTransitionRecordStoreTests`.
8. `ToolRequestStoreTests`.
9. `ProjectCanonMemoryLifecycleSqlTests`.

Only the connectivity probe retries. A failed store test is not reclassified as SQL startup noise.

### `full-sql-integration-ci`

The workflow starts a uniquely named SQL Server 2022 database, then restores and builds the solution.

Selection-only evidence:

```text
TestCategory=RequiresRealDatabase
TestCategory=LongRunning
TestCategory~RealDatabase
TestCategory~Store
```

These four commands list matching tests. They do **not** execute those complete categories.

Executed evidence:

1. SQL connectivity, with bounded service-start retry.
2. `run-platform-baseline-ci.ps1 -SkipFrontend`:
   - clean database migration verification;
   - in-process `EndpointContractTests` excluding `ProcessExecution`;
   - `ApiTestBaseCatalogGuardContractTests`.
3. The seven SQL governance-store suites from `sql-integration-ci`.
4. Seven named real-database smoke suites.
5. REL-3 one-ticket SQL/API applied smoke.
6. REL-5 chat-confirmed-ticket governed-run smoke.
7. DEMO baseline, chat-ticket, HERO disposition, bounded repair, and finding-driven revision proofs.
8. Integration category and slow-quarantine contract tests.
9. Destructive-catalog guard contract.
10. C11 secret-scan compatibility.

Artifacts distinguish selection counts from executed lane records and include execution-gap, timing, lane, and test-count summaries.

### `frontend-contract-ci`

The workflow pins Node `24.16.0` and npm `11.13.0`. The script executes:

```powershell
npm ci
npx tsc --noEmit
npx openapi-typescript openapi/irondev-api.openapi.json -o <temporary-client>
tools/contracts/update-openapi-contract.ps1 -Check
git diff --exit-code -- IronDev.TauriShell/openapi/irondev-api.openapi.json IronDev.TauriShell/src/api/generated/ironDevApiTypes.ts
```

The first OpenAPI check compares a temporary generated TypeScript client with the committed client. The second regenerates from the live API startup path and rejects dirty Swagger or client output.

This workflow does not run Vite production build, Playwright, or live LocalTest.

### `frontend-behavior-ci`

The workflow pins Node `24.16.0` and npm `11.13.0`, installs locked dependencies and Chromium, runs `npm run build`, then lists and executes 18 explicitly owned current-product Playwright files with four workers.

The lane fails when:

- production bundling fails;
- no tests are selected;
- selected and executed counts differ;
- any selected test fails;
- required JUnit evidence is absent;
- the evidence artifact safety scan fails.

The explicit inventory covers entry, routing, setup, Board, Workshop, Work Item, Documents, Tools, Members, Governance, Audit through the flow-shell contract, agent profiles, AI connections, and product identity. It does not claim ownership of the remaining historical/component Playwright files.

### `documentation-contract-ci`

The workflow runs the repository PowerShell contract directly, with no database, Node, browser, or .NET dependency:

```powershell
.\Scripts\ci\run-documentation-contract-ci.ps1
```

The lane requires exact equality between Markdown files and inventory rows, validates every local Markdown target, rejects duplicate Canonical/Supporting H1 identities, checks direct status banners for non-current documents, rejects unambiguous deprecated terminology in canonical/user-facing sources, validates canonical product-route references against the current route contract, and checks documentation entry-point links. It emits a JSON report, runs the shared artifact-safety scan even on failure, and retains sanitized evidence for 14 days.

## Retry and Quarantine Truth

| Behavior | Current truth |
| --- | --- |
| Automatic workflow retry | None. |
| Test retry | SQL connectivity probes only, to allow the service to become reachable. |
| Playwright retry | None. The bounded frontend behavior lane uses four workers and no retry. |
| `LongRunning` category | Listed in full SQL selection evidence; not executed as a complete category. |
| `RequiresRealDatabase` category | Listed in full SQL selection evidence; not executed as a complete category. |
| Quarantine contract | `SlowQuarantineCategoryContractTests` executes and validates category discipline. |
| Hidden skip/ignore lane | No workflow explicitly executes a quarantined suite or reports skipped tests as passing coverage. |

## Coverage Gaps

### GAP-CI-01: Frontend behavior has no GitHub execution lane - resolved by CLN-01A

Classification: `MissingCiLane`, resolved for the current-product suite.

`npx playwright test --list` currently discovers 747 tests in 41 files. No GitHub workflow installs Chromium or invokes Playwright. These tests include current Board, Workshop, Work Item, Library, Audit, settings, authority-firewall, refusal, empty, failure, and responsive-state contracts.

A local unfiltered run was terminated after exceeding ten minutes. That result is timing evidence, not a pass or failure count, and rules out adding all 747 tests to an existing lane with an arbitrary larger timeout.

Resolution: `frontend-behavior-ci` executes 158 tests in 18 explicit current-product files and the production Vite build. CLN-08 must assign the remaining historical/component suites. Live LocalTest remains a separate manual/operational proof and is not replaced with mocks.

### GAP-CI-02: Integration-project ownership is filter-based, not exhaustive

Classification: `Unknown` pending CLN-08.

No workflow executes all of `IronDev.IntegrationTests` without a filter. Tests outside SkeletonRun, governance exact-name/category filters, SQL exact-name filters, and full-SQL named proofs are not proven merely because the project builds or a category listing finds them.

Required follow-up: CLN-08 must inventory every integration suite against an owning lane, then create narrow lane additions for load-bearing uncovered suites. This map does not call unexecuted tests green.

### GAP-CI-03: Frontend production bundling is not a CI command - resolved by CLN-01A

Classification: `MissingCiLane`, resolved.

`frontend-behavior-ci` executes `npm run build` before browser tests. TypeScript and OpenAPI drift remain independently owned by `frontend-contract-ci`.

### GAP-CI-04: Live LocalTest and Tauri desktop remain human-operated

Classification: intentional manual gate, not CI coverage.

The live LocalTest Playwright smoke and non-author Tauri walk require a supported running environment and are release/PR evidence. They are not silently counted as GitHub execution.

## Ownership Decision

| Surface | Current owner | Follow-up |
| --- | --- | --- |
| Unit tests | `fast-unit-ci` | None from CLN-01. |
| Skeleton governed loop | `skeleton-run-ci` | None after CLN-00. |
| Governance/static/API/CLI boundary groups | `governance-boundary-ci` | CLN-08 verifies exact suite completeness. |
| Bounded SQL stores | `sql-integration-ci` | None from CLN-01. |
| Migration and named release/database proofs | `full-sql-integration-ci` | CLN-08 resolves unowned category members. |
| TypeScript/OpenAPI contract | `frontend-contract-ci` | None from CLN-01A. |
| Current-product frontend behavior and bundle | `frontend-behavior-ci` | CLN-08 assigns remaining historical/component files. |
| Documentation truth and links | `documentation-contract-ci` | CLN-04 through CLN-07 establish a zero-defect baseline. |
| Live LocalTest | Manual test contract | Preserve as real-stack evidence. |
| Tauri desktop | Non-author qualification | Preserve as human release gate. |

## Review Line

Selection proves that a test name matches a filter. Only a completed test command proves execution.

## Killjoy Line

A test suite that exists only on a developer laptop is documentation with executable syntax, not CI protection.
