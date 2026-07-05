# Alpha Smoke Troubleshooting

Start with the reason code in `alpha-smoke-result.json` or terminal output. Do not infer success from partial output.

## Check-Only Passes But Gate Blocks

Check-only mode writes no artifacts and does not prove root safety, readiness, skeleton execution, critic package creation, or report reconstruction.

Run gate mode only from a clean worktree:

```powershell
git status --short
Scripts/smoke/alpha-smoke.ps1 -Project BookSeller -Ticket validate-book -ModelMode Deterministic -RunUntil Gate
```

## SourceRepoDirtyBeforeRun

Meaning: the repository had uncommitted changes before mutation-shaped smoke.

Next safe action:

```powershell
git status --short
```

Commit or stash the unrelated changes first.

Unsafe shortcut: running the smoke anyway and claiming source preservation.

## UnsafeRoot

Meaning: the output directory failed root-safety checks.

Next safe action: pick a dedicated directory outside the repository and outside broad roots:

```powershell
Scripts/smoke/alpha-smoke.ps1 -RunUntil Gate -OutputDirectory "$env:LOCALAPPDATA\IronDev\alpha-smoke\manual-run"
```

Unsafe shortcut: writing receipts under the source repository.

## LiveModelModeNotImplemented

Meaning: `-ModelMode Live` was requested, but D-2a implements deterministic mode only.

Next safe action: build the D-2b live-model slice.

Unsafe shortcut: silently falling back to deterministic and calling it live.

## CriticReviewRequestNotAutomated

Meaning: D-2a prepared the critic package but did not request the independent critic review.

Next safe action: implement the critic-review request slice and keep it separate from approval.

Unsafe shortcut: treating package existence as a critic review.

## GateStateUnexpected

Meaning: the run did not halt at `PausedForApproval`.

Next safe action: inspect the run receipt and TRX output. The gate state must be fixed before any continuation/apply work.

Unsafe shortcut: continuing because build/test passed.

## ReceiptWriteFailed

Meaning: the smoke test passed far enough to run but did not write the receipt.

Next safe action: inspect output-root safety and filesystem permissions.

Unsafe shortcut: writing a manual success receipt.

## Boundary Reminder

Smoke success is evidence only. It is not approval, policy satisfaction, continuation authority, source apply authority, release readiness, deployment readiness, or alpha readiness.
