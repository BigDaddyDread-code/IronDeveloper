# PR204 Source Apply Narrow Real Apply Path

## Purpose

PR204 adds the first narrow real source apply path.

This PR turns the key in a locked room. It does not drive away.

## Boundary

A `SourceApplyDryRunReceipt` is rehearsal evidence.

A `SourceApplyReceipt` is real mutation evidence.

Neither receipt is release approval, workflow continuation, policy satisfaction, rollback execution, memory promotion, retrieval activation, or permission to perform another apply.

## Required evidence chain

A controlled real source apply must be bound to all of the following evidence:

- accepted approval record
- policy satisfaction record
- controlled dry-run
- patch artifact
- rollback support receipt
- source apply gate evaluation
- source apply request
- source apply dry-run receipt
- live workspace preflight
- controlled source apply request
- real source apply receipt

The executor rejects before mutation if any evidence id, hash, scope, source baseline, workspace boundary, branch, clean worktree hash, patch artifact, rollback support receipt, dry-run receipt, or file precondition does not match.

## Approved content seam

Patch artifacts currently record file hashes and normalized diffs. They do not carry full after-content payloads.

For PR204 the real apply request therefore includes explicit approved content entries for create and modify operations. The executor may use those entries only when:

- the path matches the source apply operation
- the content hash matches the operation `AfterContentHash`
- the content hash matches the approved patch artifact file change
- the dry-run file result matched the same operation and patch artifact change hash

The executor must not use source apply request text, model output, dry-run receipt text, normalized diff text, or any raw prompt/completion/tool output as file content.

## Runtime behaviour

The executor performs all validation and file preflight before the first write.

Supported operations are:

- `CreateFile`: target must be absent, approved after-content hash must match, then create the file
- `ModifyFile`: target must exist, current hash must match the approved before hash, approved after-content hash must match, then replace the content
- `DeleteFile`: target must exist, current hash must match the approved before hash, then delete the file
- `RenameFile`: previous path must exist, target must be absent, current hash must match the approved before hash, then move the file
- `Noop`: no file mutation

If validation or preflight fails, no mutation occurs and no source apply receipt is stored.

If mutation starts and a later write fails, a partial source apply receipt is stored with `PartialApplyOccurred = true` and `ApplySucceeded = false`.

## Non-goals

PR204 does not add:

- API
- CLI
- UI
- scheduler
- workflow continuation
- release readiness
- rollback execution
- git commit, push, merge, branch, or pull request creation
- agent execution
- model execution
- tool execution
- memory promotion
- retrieval activation
- source apply read API for real receipts

## Review line

PR204 turns the key in a locked room. It does not drive away.
