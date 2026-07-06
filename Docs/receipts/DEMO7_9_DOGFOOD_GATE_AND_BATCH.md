# DEMO-7 / DEMO-8 / DEMO-9 Receipt - Dogfood Gate And Batch Contracts

## Purpose

DEMO-7:

```text
Record the non-author rehearsal transcript shape and current blocker.
```

DEMO-8:

```text
Record DOGFOOD-ALPHA-LOCAL-001 as an explicit release-gate artifact.
```

DEMO-9:

```text
Represent the optional BookSeller three-ticket batch without bypassing the single-ticket gate.
```

## Files Changed

- `Docs/dogfood/DEMO-REHEARSAL-001.md`
- `Docs/release/v0.1-local-alpha/DEMO_REAL_SPEC.md`
- `Docs/release/v0.1-local-alpha/DOGFOOD-ALPHA-LOCAL-001.md`
- `Docs/release/v0.1-local-alpha/DOGFOOD-ALPHA-LOCAL-001.json`
- `Docs/release/v0.1-local-alpha/DOGFOOD-BOOKSELLER-BATCH-001.md`
- `Docs/release/v0.1-local-alpha/DOGFOOD-BOOKSELLER-BATCH-001.json`
- `IronDev.IntegrationTests/Demo/DemoDogfoodGateArtifactTests.cs`
- `Docs/receipts/DEMO7_9_DOGFOOD_GATE_AND_BATCH.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

## Verdicts

- DEMO-7 rehearsal verdict: `Blocked`
- DEMO-8 gate verdict: `EvidenceIncomplete`
- DEMO-9 batch verdict: `BatchEvidenceIncomplete`

These are not green verdicts. They deliberately preserve the current truth: the non-author fresh-checkout run and DOGFOOD-ALPHA-LOCAL-001 execution still need to happen.

## Boundaries

- No product code changed.
- No startup command executed by the artifact.
- No SQL/API state invented.
- No UI journey claimed from mocked Playwright proof.
- No release readiness claimed.
- No deployment readiness claimed.
- No release, tag, publish, upload, or deploy action.
- No shared approval, continuation, or apply authority across batch tickets.
- No fake run IDs, approval IDs, apply receipts, or final reports for parked tickets.

Evidence is not authority. A transcript is not a release gate unless it records the run.

## Validation

- DEMO-7/8/9 dogfood artifact contract tests: 26/26 passed.
- Integration category contract tests: 7/7 passed.
- C11 secret scan: 9/9 passed.
- `dotnet restore IronDev.slnx --nologo --verbosity minimal`: passed with existing warnings.
- `dotnet build IronDev.slnx --no-restore --nologo --verbosity minimal`: 0 errors / 7 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging exact DEMO-7/8/9 files.

## Next Safe Action

Assign a non-author operator to run the exact DEMO-7 rehearsal commands from a clean checkout. Replace the `EvidenceIncomplete` DOGFOOD-ALPHA-LOCAL-001 artifact only after the run produces observed command, backend, UI, receipt, and blocker evidence.

## Review Line

A dogfood gate is evidence with a verdict. It does not grant release authority by itself.

## Killjoy

An incomplete gate that says incomplete is useful. An incomplete gate that says go is a lie.
