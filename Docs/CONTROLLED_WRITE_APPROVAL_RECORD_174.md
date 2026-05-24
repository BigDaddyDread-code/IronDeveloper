---
id: CONTROLLED_WRITE_APPROVAL_RECORD_174
project: IronDev
title: Controlled Write Approval Record 174
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-24T17:50:00Z
primary_retrieval_questions:
  - What does human approval mean for controlled worktree dry-run?
  - Can controlled write approval apply to the real repository?
  - How is a promotion package scoped to a specific approval?
  - What actions remain blocked after approval?
boundary: Approval record only. It scopes permission to one package and controlled worktree dry-run validation. It does not write files, create a PR, merge, mutate memory, accept tickets, or approve itself.
---

# Controlled Write Approval Record 174

## Decision

IronDev now has a scoped human approval record for the controlled write path.

The approval is narrow by design:

```text
ApprovedForControlledWorktreeDryRun
```

It is not approval for real repository mutation.

## Command

```text
promotion approval create --package-run-id <run> --run-id <approval-run> --json
campaign controlled-write-approval-174 --run-id <run> --json
```

The command writes:

```text
tools/dogfood/runs/{runId}/approval-record.json
tools/dogfood/runs/{runId}/approval-record.md
tools/dogfood/runs/{runId}/controlled-write-policy.json
```

## Scope

The approval record binds:

- approval id
- run id
- trace id
- project
- package id
- proposed change id
- source run id
- source trace id
- reviewer identity and role
- approval phrase
- expiry time
- required evidence refs

It is valid only for:

```text
ControlledWorktreeDryRun
```

It is explicitly invalid for:

```text
RealRepoWrite
```

## Blocked Actions

Even with approval, the following remain blocked:

- write `main`
- write the active developer working tree
- create pull request
- auto-merge
- mutate accepted memory
- accept tickets
- self-approve

## Why This Matters

Human approval must not be vague.

This record gives IDA a precise permission object: one specific promotion package may advance to one specific dry-run validation step. Anything beyond that still needs a separate reviewed gate.
