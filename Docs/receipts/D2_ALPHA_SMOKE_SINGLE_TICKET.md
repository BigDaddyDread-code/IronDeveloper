# D2 Alpha Smoke Single Ticket

## Purpose

D-2a makes the BookSeller single-ticket smoke repeatable through one explicit command:

```powershell
Scripts/smoke/alpha-smoke.ps1 `
  -Project BookSeller `
  -Ticket validate-book `
  -ModelMode Deterministic `
  -RunUntil Gate
```

The command drives the deterministic BookSeller ticket through the governed skeleton path until the human gate.

## What Changed

- `Scripts/smoke/alpha-smoke.ps1` now supports the D-series command contract.
- Default/no-switch behavior is check-only and writes no smoke artifacts.
- Gate mode requires a clean source worktree and a safe output root.
- Gate mode checks the source worktree again before reporting success.
- Gate mode writes smoke artifacts outside the repository by default.
- Existing ticket/run IDs block with named reason codes instead of being silently ignored.
- Readiness mode does not advertise a skeleton run receipt.
- Live mode blocks with `LiveModelModeNotImplemented` instead of falling back to deterministic.
- `AlphaLoopSmokeTests` now stops at `PausedForApproval` and records no accepted approval, continuation, or apply.
- `AlphaSmokeScriptContractTests` locks the script contract.
- `Docs/alpha-smoke/*` documents the current path, known gaps, reason codes, and troubleshooting.

## Current Command Behavior

Check-only:

```powershell
Scripts/smoke/alpha-smoke.ps1 -CheckOnly
```

Deterministic gate:

```powershell
Scripts/smoke/alpha-smoke.ps1 -Project BookSeller -Ticket validate-book -ModelMode Deterministic -RunUntil Gate
```

Live mode:

```powershell
Scripts/smoke/alpha-smoke.ps1 -Project BookSeller -Ticket validate-book -ModelMode Live -RunUntil Gate
```

returns `LiveModelModeNotImplemented`.

## Boundary

The D-2a smoke is evidence only.

Root safety is a precondition for smoke execution. It is not evidence, approval, or execution authority.

The deterministic provider may fake model words. It must not fake approval, policy satisfaction, run completion, source apply, release, deployment, or workflow continuation.

A gate halt is not approval.

A critic package is not a critic review.

A successful smoke run is not alpha readiness.

## Known Gaps

- API-backed project/ticket persistence is not proven by this D-2a command.
- SQL persistence is not proven by this D-2a command.
- Independent critic review request is not automated here.
- Human approval recording is not implemented here.
- Workflow continuation is not requested here.
- Controlled source apply is not requested here.
- Live model mode is not implemented here.
- Three-ticket batch execution is not implemented here.

## Required Output Fields

Gate mode writes:

- `run-receipt.json`
- `alpha-smoke-result.json`
- `alpha-smoke-summary.md`
- `alpha-smoke.trx`

The receipt/result includes:

- project
- ticket
- model mode
- run-until target
- run ID
- gate state
- critic package hash
- approval target hash
- named gaps
- boundary statement

## Local Validation

Current local validation from the D-series PR worktree:

- `powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\smoke\alpha-smoke.ps1 -CheckOnly -Json`: passed
- `powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\smoke\alpha-smoke.ps1 -ModelMode Live -RunUntil Gate -Json`: blocked with `LiveModelModeNotImplemented`
- `powershell -NoProfile -ExecutionPolicy Bypass -File Scripts\smoke\alpha-smoke.ps1 -Project BookSeller -Ticket validate-book -ModelMode Deterministic -RunUntil Gate -Json`: passed; halted at `PausedForApproval`; wrote receipt under `%LOCALAPPDATA%\IronDev\alpha-smoke`
- `dotnet build IronDev.slnx --no-restore --verbosity minimal`: passed with existing warnings
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~AlphaSmokeScriptContractTests"`: 8/8 passed; includes checks for unsupported existing IDs, post-run source cleanliness enforcement, and readiness receipt semantics
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~AlphaLoopSmokeTests"`: 1/1 passed
- `Scripts\ci\run-skeleton-run-ci.ps1`: 147/147 passed
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~IntegrationTestCategoryContractTests|FullyQualifiedName~SlowQuarantineCategoryContractTests"`: 17/17 passed
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BlockC11SecretScanningRegressionTests"`: 9/9 passed

GitHub CI remains separate evidence and must run on the pushed PR head.

## Review Line

D-2a proves deterministic single-ticket smoke to the human gate. It does not create new authority.

## Killjoy

A smoke script is a witness, not an operator with authority.
