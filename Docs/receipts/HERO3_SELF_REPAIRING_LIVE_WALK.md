# HERO-3 Receipt - The Self-Repairing Live Walk

## Purpose

Arm bounded repair (REPAIR-1) inside the fully-real live walk (HERO-2), run it
live, and record what actually happens. The live walk now runs with
`SkeletonRepair:MaxAttempts = 1`: when the live model's code fails the real
build, the Builder repairs from the real compiler evidence — and the gate at
the end is exactly the gate.

## The captured run (2026-07-07, OpenAI/gpt-4o-mini, unscripted)

Run `4f2ebde6-d7ca-4a9e-9ac8-8101ad53961c`, ticket `bulk-discount`:

```text
attempt 1: live proposal -> real dotnet build FAILED (BuildFailed on 'dotnet build')
repair:    SelfRepairOccurred = true — the Builder repaired from the compiler
           evidence (RepairModel OpenAI/gpt-4o-mini), fresh attempt-scoped workspace
attempt 2: build green, tests green -> PausedForApproval
critic:    live review of the REPAIRED package: RequestChanges, TWO findings —
           Critical 'Missing Test Coverage for Zero-Priced Books' and
           Medium 'ArgumentOutOfRangeException Message Mismatch'
gate:      both findings dispositioned (AcceptRisk, reasoned) through product routes
approval:  hash-bound accepted approval 5f2c0c97-386f-48e3-a40c-6f43b1d004e1
           (target hash == critic package sha256 == the package referencing the
           REPAIRED proposal — the #729 evidence-binding fix, live)
apply:     continuation -> controlled apply -> Applied, LoopComplete, zero gaps
```

Every layer of the architecture fired in one unscripted live run: failure,
bounded self-repair, adversarial review of the repaired work, human-shaped
dispositions, hash-bound approval, governed apply.

## The full campaign (honest numbers, 8 live runs)

```text
OpenAI/gpt-4o      4 runs: 4 clean first-attempt passes, 0 repairs needed
OpenAI/gpt-4o-mini 3 runs: 1 clean pass
                           1 FAILED even with repair — budget exhausted, terminal
                             named state (a budget of 1 does not save every bad day)
                           1 self-repaired to Applied (the captured run above)
(one additional gpt-4o-mini/gpt-4o pre-arm failure pair is recorded in HERO-2)
```

Notable: gpt-4o has passed first-attempt on every run SINCE the ticket carried
`LinkedFilePaths` (the HERO-2 context fix) — context quality moved first-attempt
reliability more than model choice did.

## What changed in this slice

- `LiveModelHeroWalkTests` arms `SkeletonRepair:MaxAttempts = 1`.
- The live receipt records the repair story honestly: `RepairBudget`,
  `SelfRepairOccurred`, per-attempt `RepairAttempts` (failure kind, failed
  command, repair model), `InitialProposalId` vs `GateProposalId`.
- Contract pin: the live walk must keep repair armed and keep recording it.

## Boundaries

- Self-repair is proposal-shaped work, never authority: the repaired run halted
  at the same gate, its critic review was adversarial (and it WAS — Critical
  finding on the repaired code), and approval/continuation/apply were the same
  governed acts.
- One captured self-repair is proof the path works live, not model reliability.
  The budget-exhausted mini run is equally true evidence: bounded means bounded.
- Not in any CI execution lane (CI holds no model credentials); ManualLocal,
  opt-in envs, never a deterministic fallback.

## Files Changed

- `IronDev.IntegrationTests.Api/Smoke/LiveModelHeroWalkTests.cs`
- `IronDev.IntegrationTests/Demo/DemoSeedScriptContractTests.cs` (pin)
- `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md` (evidence updated)
- `Docs/receipts/HERO3_SELF_REPAIRING_LIVE_WALK.md`

## Review Line

The demo moment is real now: watch it break its own build, fix it from the
compiler's evidence, get caught by the critic anyway, and still wait for a human.

## Killjoy Line

A self-repairing loop that skipped the gate would be autonomy theatre. This one
repaired itself and then stood in line like everybody else.
