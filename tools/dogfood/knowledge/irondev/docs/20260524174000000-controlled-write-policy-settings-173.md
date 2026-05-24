---
id: CONTROLLED_WRITE_POLICY_SETTINGS_173
project: IronDev
title: Controlled Write Policy Settings 173
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-24T17:40:00Z
---

# Controlled Write Policy Settings 173

Slice 173 adds a deterministic effective policy snapshot for future controlled write-path work.

The policy resolves:

```text
global defaults -> project settings -> run settings -> explicit human override -> hard invariants still win
```

Configurable settings include write-path enablement, permitted modes, runtime profile, language adapter, build/test/quality commands, allowed extensions, blocked path segments, file and line limits, reviewer roles, evidence requirements, approval phrase, branch naming, worktree root, expiry, and retry count.

Hard invariants are non-configurable: no direct `main` writes, no active developer working tree writes, no self-approval, no governance bypass, no accepted memory mutation, and no blocked file promotion.

The command is:

```text
promotion policy effective --project IronDev --run-id <run> --json
campaign controlled-write-policy-173 --run-id <run> --json
```

This is policy resolution and evidence only. It does not create a branch, apply files, mutate memory, accept tickets, or grant self-approval.
