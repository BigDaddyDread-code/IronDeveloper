# CLN-01 CI Execution Map Receipt

**Status:** Execution map complete; uncovered lanes recorded without fake coverage
**Baseline:** `main` at `a1341123`
**Date:** 12 July 2026

## Purpose

Identify what each GitHub Actions lane actually executes and distinguish execution, selection, build-only coverage, and manual qualification.

## Scope

Changed files:

- `Docs/cleanup/CI_EXECUTION_MAP.md`
- `Docs/receipts/CLN_01_CI_EXECUTION_MAP.md`

No workflow, test filter, runtime behavior, authority contract, migration, generated contract, or product surface changes in this PR.

## Evidence Read

- all six files under `.github/workflows`;
- all six workflow-owned `Scripts/ci/run-*-ci.ps1` scripts;
- the embedded `run-platform-baseline-ci.ps1` path;
- frontend npm scripts and Playwright discovery;
- CLN-00 pull-request checks, where all six lanes passed.

## Result

Confirmed:

- six pull-request workflows exist;
- unit, SkeletonRun, governance, bounded SQL, full SQL, and frontend contract lanes have explicit owners;
- all lanes produce sanitized, retained evidence;
- selection-only category commands in full SQL are already distinguished from execution;
- only SQL connectivity receives bounded retry.

Gaps recorded:

- 747 Playwright tests in 41 files have no GitHub execution lane;
- Vite production build is absent from CI;
- integration-project ownership is filter-based and not yet exhaustive;
- live LocalTest and Tauri qualification are manual gates, not CI.

## Behavior

Runtime behavior changed: **No**.

CI behavior changed: **No**.

Coverage claims changed: **Yes**. Current documentation now refuses to imply that discovered or locally runnable tests execute in GitHub Actions.

## Verification

```powershell
npx playwright test --list
```

Result: 747 tests discovered in 41 files.

An unfiltered local run exceeded ten minutes and was terminated without a result. No pass claim is made from that run; it establishes that the first GitHub lane must use an explicit current-product file inventory rather than an arbitrary timeout increase.

Repository inspection confirmed no workflow invokes `playwright`, `vite build`, or a broad unfiltered `IronDev.IntegrationTests` test command.

## Next Cleanup Slices

1. Add bounded frontend build and Playwright execution in an isolated CI PR.
2. Use CLN-08 to assign every integration suite to an owning lane before adding further broad execution.

## Boundary

Green CI is evidence. This map neither grants release approval nor treats uncovered tests as passing.
