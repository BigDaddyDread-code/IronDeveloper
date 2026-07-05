# SkeletonRun CI Lane Receipt

## Purpose

D-1.1 moves the core governed-loop test category (`SkeletonRun`) from local-only
validation into a gated CI lane. The `SkeletonRun` category exercises the
P0–P2 governed loop end to end — proposal → blind test authoring → disposable
workspace build/test → critic package → human gate → continuation → apply. Until
this lane, that category ran only on a developer's machine and was never gated by
CI.

The core governed loop is not gated if `SkeletonRun` only runs on one developer's
machine.

## Files changed

- `.github/workflows/skeleton-run-ci.yml` (new lane)
- `Scripts/ci/run-skeleton-run-ci.ps1` (new lane script)
- `Docs/receipts/SKELETON_RUN_CI_LANE.md` (this receipt)

No product, Core, Infrastructure, API, CLI, SQL, UI, fixture, or test file is
changed. No existing lane is modified.

## Command

```powershell
dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj `
  --filter "TestCategory=SkeletonRun" `
  --logger "trx;LogFileName=skeleton-run.trx"
```

The lane is DB-free by construction: every `SkeletonRun` test wires its own
in-memory stores and none inherit `IntegrationTestBase`, so no SQL server is
required to run it.

## Selection and execution proof

Selection proof is not execution proof. This lane reads the TRX counters and
**fails if zero tests are selected** — a green lane means the SkeletonRun tests
were selected AND ran AND passed, not merely that a filter listed them.

- Selected / total: **129**
- Passed: **129**
- Failed: **0**
- Local run status: Passed
- Commit SHA: recorded per run in the lane summary (`$GITHUB_SHA`); this receipt
  is authored on branch `ci/skeleton-run-selection-lane`.

## Zero-selection guard

The lane fails closed on an empty selection two ways:
1. If `dotnet test` produces no TRX, `Get-TrxCounters` throws.
2. If the TRX reports `total <= 0`, the script throws
   "selected zero SkeletonRun tests."

## Boundary statement

This lane gates the `SkeletonRun` category in CI. It is execution evidence, not
approval, policy satisfaction, merge readiness, release readiness, deployment
readiness, or source apply authority. Green CI is evidence, not permission.

## Known gaps

- The lane runs the SkeletonRun category as it exists today (in-memory,
  service-level). It does not exercise the SQL/API persistence path or the live
  API surface — those are gated by the SQL/full-SQL lanes and by the forthcoming
  D-2a alpha smoke.
- The lane does not run `LongRunning` alpha-smoke tests (build/test shell-outs);
  those remain on the full-SQL selection lane.
