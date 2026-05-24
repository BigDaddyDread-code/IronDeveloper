---
id: CONTROLLED_WRITE_POLICY_SETTINGS_173
project: IronDev
title: Controlled Write Policy Settings 173
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-24T17:40:00Z
primary_retrieval_questions:
  - How does IronDev resolve controlled write policy settings?
  - Which controlled write settings are configurable?
  - Which write-path safety rules cannot be configured away?
  - What does the effective controlled write policy prove?
boundary: Policy resolution and evidence only. No branch apply, no real repository writes, no accepted memory mutation, no ticket acceptance, and no agent self-approval.
---

# Controlled Write Policy Settings 173

## Decision

IronDev now has a deterministic controlled-write policy snapshot for future write-path work.

This does not grant write authority. It proves IDA can resolve settings into an effective policy while keeping hard invariants non-configurable.

## Command

```text
promotion policy effective --project IronDev --run-id <run> --json
campaign controlled-write-policy-173 --run-id <run> --json
```

The command writes:

```text
tools/dogfood/runs/{runId}/controlled-write-policy.json
tools/dogfood/runs/{runId}/controlled-write-policy.md
```

## Settings Layers

The policy is resolved through:

```text
global defaults
  -> project settings
  -> run settings
  -> explicit human override
  -> hard invariants still win
```

Configurable settings include:

- write-path enabled flag
- permitted promotion modes
- runtime profile
- language adapter
- build command
- test command
- quality command
- allowed source file extensions
- blocked path segments
- maximum files changed
- maximum lines changed
- required reviewer roles
- required evidence types
- approval phrase
- branch naming template
- worktree root
- promotion package expiry
- build/test retry count

## Hard Invariants

The following are not settings and cannot be disabled:

- no direct writes to `main` or `master`
- no writes to the active developer working tree
- no agent self-approval
- no governance bypass
- no accepted memory mutation
- no promotion of blocked files

The smoke intentionally attempts unsafe override names such as `allowDirectMainWrite=true`. The effective policy records that these overrides were ignored.

## Why This Matters

Settings make IDA configurable.

Hard invariants keep it governed.

The effective policy snapshot is evidence for reviewers. A future write-path run must show which settings shaped the action and which safety rules remained non-configurable.
