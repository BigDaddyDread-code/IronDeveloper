# DEMO-1a / DEMO-2a Receipt - API-Driven Proof Harness

## Slice

DEMO-1a creates the local alpha demo seed command and API-backed proof harness for the BookSeller baseline.

DEMO-2a is included only as the visible/startable chat-to-ticket proof because the same product path is needed to keep the demo honest.

This is real backend/API/SQL evidence through the integration host. It is not yet the long-lived local API/SQL seed that makes the running product UI come alive.

## Files Changed

- `Scripts/demo/demo-seed.ps1`
- `IronDev.IntegrationTests/Demo/DemoSeedScriptContractTests.cs`
- `IronDev.IntegrationTests.Api/Demo/DemoSeedApiDrivenTests.cs`
- `Docs/receipts/DEMO1_API_DRIVEN_DEMO_SEED.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md`
- `IronDev.IntegrationTests/Governance/SlowQuarantineCategoryContractTests.cs`

## Command

```powershell
Scripts/demo/demo-seed.ps1 -CheckOnly
Scripts/demo/demo-seed.ps1 -Seed -Project BookSeller -ModelMode Deterministic
```

## What This Proves

- `validate-book` can be driven to `Applied` through authenticated API routes backed by SQL.
- `search-by-author` can be driven to `PausedForApproval` through authenticated API routes backed by SQL.
- The seed proof uses BookSeller fixture tickets and governed skeleton-run endpoints.
- The seed proof records critic package hash, critic review ID, accepted approval ID, continuation result, apply receipt hash, and final report reference for the Applied ticket.
- The PausedForApproval ticket does not create accepted approval, continuation, or apply evidence.
- Reports are reconstructed from SQL-backed API state.
- The live chat ticket is not seeded ahead of the demo.
- DEMO-2a proves chat session/message, draft generation, draft confirmation, ticket list visibility, ticket detail, and start-governed-run to the human gate.

## Boundary

A demo seed may replay history. It may not invent authority.

Evidence is not approval.
Validation is not approval.
Critic package is not critic review.
Critic review is not approval.
Accepted approval is not policy satisfaction.
Continuation is not apply permission.
Applied seed history is not release readiness.
No live chat ticket is seeded ahead of the demo.

## Forbidden Behavior

- No direct SQL final-state insert.
- No frontend fixture rows.
- No fake build output.
- No fake test output.
- No fake critic package.
- No fake approval.
- No fake continuation.
- No fake apply receipt.
- No silent accepted approval for the PausedForApproval ticket.
- No source mutation outside the governed skeleton apply path.

## Redaction

The demo seed summary and receipt paths are redacted for user-local roots.

The receipt must not print raw secrets, tokens, connection strings, API keys, or raw user-local paths.

## Known Limits

- DEMO-1a runs through the API integration host, not a separately launched long-lived API process.
- DEMO-1b must seed a long-lived local API/SQL environment that the UI can open.
- DEMO-2a proves API-visible/startable behavior and the existing flow-shell controls; full browser click-through belongs to DEMO-3/DEMO-4.
- The seed remains deterministic-only. Live model demo posture is a later explicit decision.
- This is not release readiness, merge readiness, deployment readiness, or alpha release approval.

## Validation

Current-head local validation:

- `dotnet build IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj --nologo --verbosity minimal`: passed with existing warnings.
- `dotnet build IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --nologo --verbosity minimal`: passed with existing warnings.
- `dotnet build IronDev.slnx --nologo --verbosity minimal`: passed with existing warnings.
- `powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/demo/demo-seed.ps1 -CheckOnly -Json`: passed and redacted the repo root.
- Focused DEMO API tests (`FullyQualifiedName~DemoSeedApiDrivenTests`): 2/2 passed.
- Focused DEMO script/category tests (`DemoSeedScriptContractTests`, `IntegrationTestCategoryContractTests`, `SlowQuarantineCategoryContractTests`): 26/26 passed.
- C11 secret scan (`FullyQualifiedName~BlockC11SecretScanningRegressionTests`): 9/9 passed on final rerun.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Killjoy

A fixture is acceptable. A fake outcome is not.
