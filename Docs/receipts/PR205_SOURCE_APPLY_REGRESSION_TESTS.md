# PR205 - Source Apply Regression Tests

## Review line

PR205 locks the launch cage. It does not launch anything new.

## Purpose

PR205 adds source-apply regression tests only.

The suite proves the PR199-PR204 source-apply chain remains bounded, evidence-driven, receipt-backed, and authority-separated.

## Boundary

PR205 does not add source apply behavior.

PR205 does not mutate source outside tests.

PR205 does not add SQL.

PR205 does not add API.

PR205 does not add CLI.

PR205 does not add UI.

PR205 does not add runtime execution.

PR205 does not execute rollback.

PR205 does not continue workflow.

PR205 does not approve release.

PR205 does not infer release readiness.

PR205 does not call agents, models, or tools.

PR205 does not promote memory.

PR205 does not activate retrieval.

PR205 does not call git.

PR205 does not run processes.

## Source apply chain

The regression suite covers the source apply chain:

```text
accepted approval record
-> policy satisfaction record
-> controlled dry-run
-> patch artifact
-> rollback support receipt
-> source apply gate evaluation
-> source apply request
-> source apply executor dry-run
-> source apply dry-run receipt
-> source apply dry-run receipt read API
-> controlled real source apply
-> real source apply receipt
```

## Hard boundaries

Source apply gate satisfaction is not source apply.

SourceApplyRequest is not source apply.

Source apply dry-run is not source apply.

SourceApplyDryRunReceipt is not source apply.

SourceApplyDryRunReceipt read API is not source apply.

Controlled source apply is source mutation.

SourceApplyReceipt is mutation evidence.

SourceApplyReceipt is not workflow continuation.

SourceApplyReceipt is not release readiness.

SourceApplyReceipt is not rollback execution.

SourceApplyReceipt is not git commit, push, merge, branch creation, or PR creation.

## What the tests prove

- Evidence mismatch rejects before mutation.
- Unsafe paths reject before mutation.
- Current file hash mismatch rejects before mutation.
- Approved content hash mismatch rejects before mutation.
- Unsupported or duplicate file operations reject before mutation.
- SourceApplyReceipt remains mutation evidence only.
- Partial apply receipts preserve full file-plan evidence.
- SQL receipt storage remains append-only.
- SQL rejects raw/private material and authority claims.
- No new API, CLI, UI, runtime, workflow, release, rollback, git, agent, model, memory, or retrieval path is introduced.

## Correct interpretation

PR205 is a regression lock.

It does not make source apply broader.

It does not make source apply safer by adding new authority.

It checks that source apply remains narrow, explicit, evidence-bound, and diagnosable.
