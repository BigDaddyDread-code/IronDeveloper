# BookSeller Single-Ticket Smoke

This page documents the current deterministic single-ticket path.

## Gate Command

```powershell
Scripts/smoke/alpha-smoke.ps1 `
  -Project BookSeller `
  -Ticket validate-book `
  -ModelMode Deterministic `
  -RunUntil Gate
```

## Applied Command

```powershell
Scripts/smoke/alpha-smoke.ps1 `
  -Project BookSeller `
  -Ticket validate-book `
  -ModelMode Deterministic `
  -RunUntil Applied `
  -RecordHumanApproval `
  -ApprovalPhrase "I approve continuation for run <runId> package <hash>"
```

The placeholder phrase is intentional. The smoke binds `<runId>` and `<hash>` to the generated run ID and critic package hash after the gate produces them. Without `-RecordHumanApproval`, applied mode blocks with `AcceptedApprovalRequired`.

## SQL/API Persisted Applied Command

```powershell
Scripts/smoke/alpha-smoke.ps1 `
  -Project BookSeller `
  -Ticket validate-book `
  -ModelMode Deterministic `
  -RunUntil Applied `
  -RequireExistingAcceptedApproval
```

This REL-3 mode uses the authenticated API test host and SQL-backed stores. The switch name is historical from the smoke contract: the path proves an accepted approval exists in SQL before continuation is consumed, but the test creates that approval through the governed accepted-approval API inside the deterministic smoke run.

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
- `ApprovalCheck` for applied mode
- `ContinuationRequest` for applied mode
- `ApplyRequest` for applied mode
- `ReportFetch`
- `ReceiptWrite`

Some stages are intentionally `Skipped` in gate mode. In particular, API, SQL, ticket persistence, and critic review request are named gaps because gate mode proves service-level deterministic plumbing to the halt.

REL-2 applied mode still names API, SQL, and ticket persistence as gaps. It records deterministic critic-review and human-approval evidence inside the service-level smoke only.

REL-3 persisted mode expects `SqlCheck`, `ApiCheck`, `TicketPersist`, and `ApprovalCheck` to pass with SQL/API-specific reason codes. It records deterministic critic-review evidence, creates accepted approval through the API, consumes that approval through continuation, applies through the API, and verifies SQL rows.

## Expected Gate State

The run must halt at:

```text
PausedForApproval
```

Applied mode must finish at:

```text
Applied
```

That final state is allowed only after the smoke records critic review evidence, records a hash-bound accepted approval, requests continuation, and then requests the controlled copy-only apply spine.

Gate mode must not create accepted approval, request continuation, or apply source. REL-2 applied mode creates accepted approval only when `-RecordHumanApproval` and the exact approval phrase are supplied. REL-3 persisted mode creates accepted approval only through the accepted-approval API while `-RequireExistingAcceptedApproval` is explicitly supplied.

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

## Finding The Apply Receipt

After an applied run, open `run-receipt.json` and read:

```text
applyReceiptPath
applyReceiptSha256
acceptedApprovalId
loopComplete
```

The `apply-copy.json` receipt is part of the workspace evidence chain. It is not commit, push, release, or deployment authority.

## Verifying Source Was Not Mutated

The smoke blocks if the IronDev source worktree is dirty before mutation-shaped mode starts.

The smoke also checks the source worktree after the gate run and fails with `SourceRepoChangedUnexpectedly` if the repository changed during smoke execution.

After the run, from the repository root:

```powershell
git status --short
```

The smoke output directory should be outside the repository. No source-root artifact is expected.

## Boundary

The deterministic model may fake model words. It must not fake approval, policy satisfaction, release, deployment, commit, or push.

Applied mode records a deterministic human approval only when explicitly requested. Approval is continuation input; apply remains a separate governed act.
