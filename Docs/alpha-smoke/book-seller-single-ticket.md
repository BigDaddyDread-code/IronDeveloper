# BookSeller Single-Ticket Smoke

This page documents the current D-2a deterministic single-ticket path.

## Command

```powershell
Scripts/smoke/alpha-smoke.ps1 `
  -Project BookSeller `
  -Ticket validate-book `
  -ModelMode Deterministic `
  -RunUntil Gate
```

## Expected Stages

The script reports named stages:

- `RepoCheck`
- `ToolchainCheck`
- `LocalConfigCheck`
- `FixtureCheck`
- `TicketLoad`
- `RootSafetyCheck`
- `SqlCheck`
- `ApiCheck`
- `ReadinessCheck`
- `TicketPersist`
- `SkeletonRunStart`
- `RunEvidenceRefresh`
- `CriticPackageFetch`
- `CriticReviewRequest`
- `GateStateVerify`
- `ReportFetch`
- `ReceiptWrite`

Some stages are intentionally `Skipped` in D-2a. In particular, API, SQL, ticket persistence, and critic review request are named gaps because this slice proves service-level deterministic plumbing to the gate.

## Expected Gate State

The run must halt at:

```text
PausedForApproval
```

The script must not create accepted approval, request continuation, or apply source.

## Finding The Run ID

After a gate run, open:

```text
run-receipt.json
alpha-smoke-result.json
```

The `runId` field is the skeleton run ID.

## Finding The Critic Package Hash

Open `run-receipt.json` and read:

```text
criticPackageSha256
approvalTargetHash
```

Those values should match because any later human approval must bind to the critic package hash.

## Verifying Source Was Not Mutated

The smoke blocks if the source worktree is dirty before gate mode starts.

The smoke also checks the source worktree after the gate run and fails with `SourceRepoChangedUnexpectedly` if the repository changed during smoke execution.

After the run, from the repository root:

```powershell
git status --short
```

The smoke output directory should be outside the repository. No source-root artifact is expected.

## Boundary

The deterministic model may fake model words. It must not fake approval, policy satisfaction, run completion, source apply, release, deployment, or workflow continuation.
