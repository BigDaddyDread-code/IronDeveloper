# REL-3 SQL/API Persisted Alpha Smoke

## Purpose

Prove the deterministic BookSeller alpha smoke can reach `Applied` through authenticated API routes backed by SQL persistence.

## Files Changed

- `IronDev.IntegrationTests.Api/AlphaSmokeApiPersistenceTests.cs`
- `IronDev.IntegrationTests.Api/ApiTestBase.cs`
- `IronDev.IntegrationTests/Smoke/AlphaSmokeScriptContractTests.cs`
- `IronDev.IntegrationTests/Governance/SlowQuarantineCategoryContractTests.cs`
- `IronDev.Infrastructure/Services/TicketService.cs`
- `Scripts/ci/run-full-sql-integration-ci.ps1`
- `Scripts/smoke/alpha-smoke.ps1`
- `Docs/alpha-smoke/README.md`
- `Docs/alpha-smoke/book-seller-single-ticket.md`
- `Docs/alpha-smoke/reason-codes.md`
- `Docs/release/v0.1-local-alpha/READINESS_INVENTORY.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md`
- `Docs/receipts/REL3_SQL_API_PERSISTED_ALPHA_SMOKE.md`

## What REL-3 Proves

- The smoke can create the BookSeller project through an authenticated API request.
- The smoke can create the `validate-book` ticket through an authenticated API request.
- The smoke can start the skeleton run through an authenticated API request.
- The smoke can reconstruct the halted run report through an authenticated API request.
- The smoke can record deterministic critic review evidence through the critic-review API route.
- The smoke can create and read back a hash-bound accepted approval through the accepted-approval API backed by SQL.
- The smoke can request continuation through the API and consume live SQL-backed accepted approval evidence.
- The smoke can request controlled apply through the API and reach `Applied`.
- SQL contains the run row, run event trail, and accepted approval row for the smoke path.
- The final report reconstructs the applied loop and references an on-disk apply-copy receipt.
- The SQL ticket insert path binds `BlockedByTicketIds` instead of failing on a missing Dapper parameter.

## Script Contract

`Scripts/smoke/alpha-smoke.ps1` now has a persisted applied mode:

```powershell
Scripts/smoke/alpha-smoke.ps1 `
  -Project BookSeller `
  -Ticket validate-book `
  -ModelMode Deterministic `
  -RunUntil Applied `
  -RequireExistingAcceptedApproval
```

In this mode:

- `SqlCheck` reports `SqlAvailable`.
- `ApiCheck` reports `ApiAvailable`.
- `TicketPersist` reports `TicketPersisted`.
- `ApprovalCheck` reports `AcceptedApprovalPersisted`.
- The script runs `IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj`.
- The script selects `AlphaSmokeApiPersistenceTests.Rel3_OneTicket_ReachesApplied_ThroughSqlBackedApi`.

## CI Contract

`Scripts/ci/run-full-sql-integration-ci.ps1` now includes an explicit lane:

```text
REL-3 SQL API alpha smoke
```

The lane executes the exact REL-3 API smoke test against the CI SQL database. This is execution proof for the named release smoke only. It is not broad execution proof for every `RequiresRealDatabase` or `LongRunning` test.

## Boundary

SQL/API persistence is evidence only.

Persisted ticket, run, event, approval, report, and receipt rows do not grant approval, policy satisfaction, source mutation authority, commit authority, push authority, merge readiness, release readiness, deployment readiness, or workflow continuation authority.

The accepted approval created by the smoke is continuation input only. Controlled apply remains a separate governed request.

## Does Not Prove

- Live model behavior.
- Product UI approval recording.
- Fresh-machine dogfood from clone.
- Restart survival after process restart.
- User-selected real source target apply.
- Commit, push, PR creation, merge, release, or deployment.
- Batch completion.
- External alpha readiness.

## Validation

Expected validation for this slice:

- Build `IronDev.IntegrationTests.Api`.
- Build `IronDev.IntegrationTests`.
- Focused REL-3 API smoke test.
- Alpha-smoke script contract tests.
- Integration category and slow/quarantine category contract tests.
- C11 secret scan.
- `git diff --check`.

Final validation results should be recorded in the PR body once run.

## Review Line

Persistence proves the trail survives the product path. It does not grant approval or authority.

## Killjoy

A row in SQL is a witness. It is not permission.
