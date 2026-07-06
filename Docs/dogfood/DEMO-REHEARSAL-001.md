# DEMO-REHEARSAL-001

## Executive Verdict

Verdict: `Blocked`

Reason: this artifact defines the non-author rehearsal record and records the current blocker honestly. A fresh non-author operator has not yet executed the full 12-minute demo path from a clean checkout.

## Commit And Environment

- Commit SHA: `e8885a544fd26f6a55382f15328b7807221713ae`
- Branch: `main`
- OS/shell: `NotCapturedNonAuthorRunMissing`
- .NET version: `NotCapturedNonAuthorRunMissing`
- Node/npm version: `NotCapturedNonAuthorRunMissing`
- Git version: `NotCapturedNonAuthorRunMissing`
- SQL status: `NotRun`
- Weaviate status: `NotRequiredForDeterministicDemoPath`
- Root safety status: `NotRun`
- Model mode: `DeterministicOnlyLocalAlphaPreview`

## Required Demo Commands

1. `git rev-parse HEAD`
2. `Scripts/local/doctor-local.ps1 -CheckOnly -Json`
3. `Scripts/demo/start-v0.1-demo.ps1 -CheckOnly -NoStart -Json`
4. `Scripts/demo/demo-seed.ps1 -CheckOnly -Json`
5. `Scripts/demo/start-v0.1-demo.ps1`
6. Open the local UI URL printed by the startup script.
7. Walk the BookSeller ticket/run/report path and record backend evidence refs.

## Product Evidence Fields

- API start command: `Scripts/demo/start-v0.1-demo.ps1`
- UI start command: `Scripts/demo/start-v0.1-demo.ps1`
- Project ID: `NotCapturedNonAuthorRunMissing`
- Seeded ticket IDs: `NotCapturedNonAuthorRunMissing`
- Live ticket ID: `NotCapturedNonAuthorRunMissing`
- Run ID: `NotCapturedNonAuthorRunMissing`
- Critic package hash: `NotCapturedNonAuthorRunMissing`
- Approval ID: `NotCapturedNonAuthorRunMissing`
- Continuation result: `NotCapturedNonAuthorRunMissing`
- Apply receipt path/hash: `NotCapturedNonAuthorRunMissing`
- Final report path: `NotCapturedNonAuthorRunMissing`

## Blockers

- `NonAuthorOperatorMissing`
- `FreshCheckoutRehearsalNotRun`
- `DogfoodGateNotExecuted`

## Manual Fixes

- Assign a non-author operator.
- Start from a clean checkout or explicitly documented clean local state.
- Run the commands above exactly as written.
- Replace every `NotCapturedNonAuthorRunMissing` value with real observed evidence.

## Repeatability Verdict

`Blocked`

The current record proves the rehearsal fields and failure vocabulary are present. It does not prove the demo is repeatable.

## Boundary

This rehearsal transcript is evidence only. It is not approval, policy satisfaction, workflow continuation, source apply authority, release readiness, deployment readiness, live-model proof, or permission to publish.

## Review Line

One non-author rehearsal can prove repeatability. A missing rehearsal can only prove what remains missing.

## Killjoy

If the demo needs the author in the room, it is not yet a demo.
