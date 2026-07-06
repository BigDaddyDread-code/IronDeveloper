# Receipt - Repair Attempts Surfaced in the Flow UI

## Purpose

Close the last honesty gap REPAIR-1 left: the backend records bounded repair
attempts and distinguishes the failed initial proposal from the gate proposal,
but a viewer watching a self-repairing run in the UI saw evidence they could
not explain. The flow UI now tells the repair story wherever the run is read.

## What the UI now shows

```text
Build stage:
  - Proposal line labels the gate proposal explicitly when repair occurred:
    "Gate proposal (repaired) prop-...-repair-2 ..."
  - Bounded repair panel: every attempt with what failed ("repaired after
    BuildFailed on 'dotnet build'"), the repair proposal id, and the repair
    model; a named warning when repair evidence is missing from disk.
  - "Initial proposal prop-... failed and is preserved as history — the
    proposal under review is prop-...-repair-2."
  - Boundary stated in place: "A repair attempt is a new proposal, not
    authority — the human gate is unchanged."

Review stage:
  - "This run self-repaired once before reaching review — the critic reviewed
    the repaired proposal (...), and the gate below is unchanged."

No repair -> NO repair chrome at all. The boring good case earns no noise.
```

## Types

`SkeletonRunRepairAttemptTrace` added to `src/api/types.ts`; `SkeletonRunReport`
gains `repairAttempts` (required — the backend always sends it) and
`initialProposal` (populated only when repair replaced the original), with the
same doc language as the backend model: Proposal is FINAL/CURRENT, the one the
gate binds to.

## Validation

- `tsc --noEmit`: clean (this is the frontend-contract CI gate).
- Playwright `skeleton-run-stages.spec.ts`: **12/12 locally**, including two new
  tests — "a self-repaired run says so honestly in build and review, and the
  gate is unchanged" (asserts the repaired-gate-proposal label, attempt panel,
  history line, boundary text, AND that the human gate stays locked) and
  "a run with no repair renders no repair chrome at all".
- CI-executed contract pin: `FlowUi_SurfacesRepairAttemptsHonestly` in
  `DemoSeedScriptContractTests` (governance-boundary lane) pins the types, the
  boundary strings, and the no-noise guard, since Playwright does not run in CI.

## Boundaries

- Run visibility is not run authority: the panel records history and grants
  nothing; the approval gate rendering is untouched and the repaired-run test
  asserts it stays locked.
- No state stronger than the backend reports: everything rendered comes from
  the report's durable-event reconstruction.

## Files Changed

- `IronDev.TauriShell/src/api/types.ts`
- `IronDev.TauriShell/src/flow/workitem/RepairAttemptsPanel.tsx` (new)
- `IronDev.TauriShell/src/flow/workitem/BuildStage.tsx`
- `IronDev.TauriShell/src/flow/workitem/ReviewStage.tsx`
- `IronDev.TauriShell/tests/skeleton-run-stages.spec.ts`
- `IronDev.IntegrationTests/Demo/DemoSeedScriptContractTests.cs`
- `Docs/receipts/FLOW_REPAIR_ATTEMPTS_UI.md`

## Review Line

The backend learned to survive its own bad day in #729; the UI now admits the
bad day happened. Honesty about the mess is the product.

## Killjoy Line

A repair panel that only ever renders for the demo is decoration. This one
renders from durable events and disappears when there is nothing to confess.
