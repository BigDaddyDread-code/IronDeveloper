# PR206 ? Rollback Executor Receipt

Review line: PR206 pulls the emergency brake. It does not declare the crash cleaned up.

## Purpose

PR206 adds the first narrow controlled rollback executor and durable rollback execution receipt store.

This PR may mutate source only inside the approved workspace root after full rollback preflight passes.

The rollback executor is for emergency reversal of a previously recorded source apply attempt. It is not a general source editing system, not workflow continuation, and not release readiness.

## Boundary

This PR does:

- validate rollback plan, rollback support receipt, source apply request, source apply receipt, and patch artifact evidence before mutation
- preflight every rollback file operation before mutation starts
- allow only restore modified file, delete created file, recreate deleted file, rename back, and noop operations
- restrict file writes/deletes/renames to the approved workspace root
- reject unsafe paths, hidden/private reasoning markers, authority claims, approval claims, policy-satisfaction claims, workflow-continuation claims, release-readiness claims, memory-promotion claims, retrieval-activation claims, and git-operation claims
- write a durable append-only rollback execution receipt after a successful or partial rollback attempt

This PR does not:

This PR does not continue workflow.

This PR does not approve release.

- continue workflow
- approve release
- satisfy approval
- satisfy policy
- execute a workflow step
- dispatch agents
- invoke models
- invoke tools
- promote memory
- activate retrieval
- call git
- commit changes
- push changes
- open pull requests
- add API, CLI, UI, scheduler, orchestrator, hosted service, or background worker behavior

## Receipt meaning

A rollback execution receipt means a controlled rollback attempt was recorded.

It does not mean the crash is cleaned up.

It does not mean release is approved.

It does not mean policy is satisfied.

It does not mean workflow may continue.

Human review remains required after rollback execution.

## Known limitation

`ObservedCleanWorktreeHashAfterApply` remains source-apply receipt evidence, not independent live worktree discovery. PR206 compensates by requiring live per-file preflight checks before mutation.
