---
id: CONTROLLED_WORKTREE_DRY_RUN_175
project: IronDev
title: Controlled Worktree Dry-Run 175
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-24T18:00:00Z
---

# Controlled Worktree Dry-Run 175

Slice 175 adds a dry-run validation gate for future controlled worktree apply.

It validates promotion package, approval record, effective policy, explicit target path, target outside active repo, non-main branch name, promotable files, blocked files, and mutation evidence.

It does not create a worktree and does not copy files.

The command is:

```text
promotion apply worktree-dry-run --package-run-id <package-run> --approval-run-id <approval-run> --target-worktree <path> --run-id <run> --json
campaign controlled-worktree-dry-run-175 --run-id <run> --json
```

Successful dry-run returns `ReadyForReviewedWorktreeApplyImplementation`, active repository mutation count zero, isolated files changed zero, and a report under `tools/dogfood/runs/{runId}`.

This is a locked-door rehearsal only. It does not grant real repo write authority, PR creation, accepted memory mutation, ticket acceptance, or self-approval.
