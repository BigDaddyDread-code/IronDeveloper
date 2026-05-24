---
id: CONTROLLED_WRITE_APPROVAL_RECORD_174
project: IronDev
title: Controlled Write Approval Record 174
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-24T17:50:00Z
---

# Controlled Write Approval Record 174

Slice 174 adds a scoped approval record for the controlled write path.

The approval binds a specific promotion package, proposed change, source run, source trace, reviewer role, approval phrase, expiry, and required evidence refs.

It is valid only for:

```text
ControlledWorktreeDryRun
```

It is not valid for real repository writes.

Blocked actions remain: write main, write the active developer working tree, create pull request, auto-merge, mutate accepted memory, accept tickets, and self-approve.

The command is:

```text
promotion approval create --package-run-id <run> --run-id <approval-run> --json
campaign controlled-write-approval-174 --run-id <run> --json
```

This creates approval evidence only. It does not write files, create a PR, merge, mutate memory, accept tickets, or approve itself.
