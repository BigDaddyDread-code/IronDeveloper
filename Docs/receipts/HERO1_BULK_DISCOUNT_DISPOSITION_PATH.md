# HERO-1 Receipt - Bulk Discount Advisory Finding Disposition Gate

## Purpose

Give the disposition gate its first real work.

Every deterministic critic fixture before this one returned a clean review, so the
finding→disposition→continuation chain the product enforces (P1-3) had never been
walked by any demo or release proof. HERO-1 makes the `bulk-discount` hero ticket
produce exactly one **advisory** critic finding and proves the full governed walk:

```text
fresh bulk-discount ticket through the product API
-> governed run with real disposable build/test evidence
-> PausedForApproval
-> critic review records ONE advisory finding (BlocksMerge = false)
-> accepted approval recorded (hash-bound)
-> continuation REFUSED: approval does not bypass an undispositioned finding
-> disposition without a reason refused
-> disposition of a phantom finding refused
-> AcceptRisk disposition with a reasoned decision recorded through the product API
-> continuation allowed, controlled apply, Applied
-> final report reconstructs review, finding, disposition, approval, apply receipt
```

## Why the finding is advisory, and real

The golden bulk-discount diff rounds the discounted branch to 2 decimal places
(away from zero) but leaves the flat branch unrounded. A human reviewer would
genuinely raise this — yet the code is correct per acceptance criterion 2
("fewer than 10 copies is priced flat, exactly as before"). Accepting the finding
therefore requires understanding the ticket, not rubber-stamping. A blocking
finding would derail the golden walk; a fake finding would be dishonest; a
trivial finding would need no judgment.

## Correction recorded: selection is not execution

While building HERO-1 it was discovered that **no CI lane executed
`DemoSeedApiDrivenTests`** — the full SQL lane only selected its categories.
The PR #724 record claimed the two-run usability probe "ran in the
full-sql-integration-ci lane"; that claim was wrong. The lane was green because
the tests were never run.

HERO-1 closes the gap: `Scripts/ci/run-full-sql-integration-ci.ps1` now has an
exact-name execution lane, "DEMO seed and HERO disposition proofs", running:

```text
DemoSeedApiDrivenTests.DemoSeed_BaselineHistory_IsApiDrivenAndSqlPersisted
DemoSeedApiDrivenTests.Demo2_ChatConfirmedTicket_IsVisibleAndStartableThroughApi
DemoSeedApiDrivenTests.Hero_BulkDiscountAdvisoryFinding_RequiresDispositionBeforeApplied
```

A contract test (`Hero_AdvisoryFindingDispositionGate_IsProvenAndExecutedInCi`)
asserts these exact names stay in the lane, so the gap cannot silently reopen.

## Files Changed

- `IronDev.IntegrationTests.Api/Demo/DemoSeedApiDrivenTests.cs`
- `IronDev.IntegrationTests/Demo/DemoSeedScriptContractTests.cs`
- `Scripts/ci/run-full-sql-integration-ci.ps1`
- `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md`
- `Docs/receipts/HERO1_BULK_DISCOUNT_DISPOSITION_PATH.md`

## Boundaries

- A finding is not a veto: the hero finding is advisory and does not block merge.
- A disposition is not approval: it removes the finding blockage only; continuation still verified its own hash-bound accepted approval.
- An accepted approval does not bypass a finding: continuation refused with the approval already live.
- A disposition requires a reason and a real finding: dismissals and phantom dispositions are refused by the product service.
- The deterministic hero critic is plumbing proof, not critic quality proof: it exercises the gate, it does not review code.
- Green CI on this slice is evidence, not release readiness.

## Known Limits

- The hero critic is deterministic; a live-model critic producing findings remains separate proof.
- The disposition here is API-driven; the UI disposition surface walking this same path is separate (DEMO-3/UI journey scope).
- The full SQL lane grows by roughly the cost of four BookSeller disposable build/test runs; timing is recorded in the lane summary artifacts.

## Validation

- `dotnet build` (both integration test projects): 0 errors.
- `DemoSeedScriptContractTests`: 17/17 passed locally, including the new hero/CI-execution contract.
- `IntegrationTestCategoryContractTests` + `SlowQuarantineCategoryContractTests`: 17/17 passed locally.
- Executed locally against real SQL (LocalDB):
  - `Hero_BulkDiscountAdvisoryFinding_RequiresDispositionBeforeApplied`: passed (41s).
  - `DemoSeed_BaselineHistory_IsApiDrivenAndSqlPersisted` (baseline + two-run usability probe): passed (57s) — the first genuine execution of the #724 probe.
  - `Demo2_ChatConfirmedTicket_IsVisibleAndStartableThroughApi`: passed (14s).
- The new CI lane costs roughly two minutes of full SQL lane time.

## Review Line

A finding the human never has to answer is decoration. This slice makes the first finding one the gate must answer.

## Killjoy Line

A disposition gate that has never seen a finding is a turnstile in an empty field. HERO-1 walks something through it.
