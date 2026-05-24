---
id: CONTROLLED_WORKTREE_DRY_RUN_175
project: IronDev
title: Controlled Worktree Dry-Run 175
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-24T18:00:00Z
primary_retrieval_questions:
  - How does IronDev validate a future controlled worktree apply without writing?
  - Does controlled worktree dry-run create a worktree?
  - What evidence is required before worktree apply implementation?
  - Does dry-run keep the active repository unchanged?
boundary: Dry-run validation only. No worktree creation, no file copy, no real repository writes, no PR creation, no accepted memory mutation, no ticket acceptance, and no agent self-approval.
---

# Controlled Worktree Dry-Run 175

## Decision

IronDev now has a controlled worktree dry-run gate.

It validates the package, approval, policy, target path, target branch, file manifest, and mutation boundary before any future worktree apply implementation exists.

It does not create the worktree.

It does not copy files.

## Command

```text
promotion apply worktree-dry-run --package-run-id <package-run> --approval-run-id <approval-run> --target-worktree <path> --run-id <run> --json
campaign controlled-worktree-dry-run-175 --run-id <run> --json
```

The command writes:

```text
tools/dogfood/runs/{runId}/controlled-worktree-dry-run-report.json
tools/dogfood/runs/{runId}/controlled-worktree-dry-run-report.md
tools/dogfood/runs/{runId}/controlled-write-policy.json
```

## Validations

The dry-run proves:

- target path is explicit
- target path is outside the active repository
- target branch is not `main` or `master`
- effective policy enables only `ControlledWorktreeDryRun`
- scoped approval matches the promotion package
- files to apply come from `FilesToPromote`
- blocked files remain rejected
- active repository mutation count remains zero
- no isolated files are changed
- dry-run target path is not created

## Recommendation

If all checks pass, the dry-run returns:

```text
ReadyForReviewedWorktreeApplyImplementation
```

That recommendation is not permission to apply.

It means the next implementation slice may build the actual controlled worktree apply path using the same evidence shape.

## Why This Matters

This is the locked-door rehearsal.

IDA can now show what it would do, what it would refuse, and why the active repository stayed untouched before the system is allowed to perform the real isolated worktree operation.
