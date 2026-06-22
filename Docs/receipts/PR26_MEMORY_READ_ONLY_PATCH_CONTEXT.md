# PR26 Memory Read-Only Patch Context

## Purpose

PR26 adds read-only memory context for patch proposal runs.

Memory may improve proposal quality by supplying prior failure hints, project conventions, previous patterns, and sanitized engineering heuristics.

Memory may improve the proposal. It may not approve the action.

Review line:

> Memory may improve the proposal. It may not approve the action.

Killjoy:

> Memory can whisper advice. It cannot touch the lock.

## Allowed Memory Context

Allowed memory context types:

- prior failure hints
- known project conventions
- relevant previous patterns
- sanitized engineering heuristics

Project-local memory may describe project-specific patterns for the same repository.

Portable engineering memory may describe sanitized lessons only.

## Rejected Memory Context

Rejected memory context includes:

- memory approval text
- memory policy satisfaction text
- memory source apply authority text
- memory rollback authority text
- memory commit, push, or PR authority text
- memory workflow continuation text
- memory self-promotion text
- cross-repo authority transfer
- unsanitized cross-project facts
- client, schema, ticket, incident, approval, release, or deployment details from another project
- unaccepted memory candidates
- unsanitized memory candidates

If memory source classification is unsafe, PR26 fails closed by rejecting the memory source.

## Boundary Flags

`MemoryContextBoundary.AdvisoryOnly` is:

```text
ReadOnly = true
ContextOnly = true
CanApprove = false
CanSatisfyPolicy = false
CanAuthorizeSourceApply = false
CanAuthorizeRollback = false
CanAuthorizeCommit = false
CanAuthorizePush = false
CanAuthorizePullRequest = false
CanPromoteMemory = false
CanContinueWorkflow = false
CanTransferCrossRepoAuthority = false
```

## Read-Only Proof

PR26 adds a pure memory context builder and readout helper.

It does not:

- read or write durable memory stores
- promote memory
- create approval
- satisfy policy
- execute source apply
- execute rollback
- stage or commit
- push
- create or update PRs
- mark ready for review
- request reviewers
- merge
- release
- deploy
- continue workflow
- call providers
- run Git, GitHub, shell, or frontend paths

## Authority Proof

Memory hint is not approval.

Memory hint is not policy satisfaction.

Memory hint is not source apply authority.

Memory hint is not rollback authority.

Memory hint is not commit authority.

Memory hint is not push authority.

Memory hint is not PR authority.

Memory hint is not memory promotion authority.

Memory hint is not workflow continuation.

Cross-project memory can carry sanitized lessons, not project truth or authority.

Prior success is not current approval.

Prior validation is not current validation.

Prior approval is not current authority.

## Patch Loop Proof

If memory context appears in patch output, it is labeled:

```text
Memory context: advisory only
```

The required warning is present:

```text
Memory hints do not approve, satisfy policy, authorize source apply, promote memory, or continue workflow.
```

Patch-loop eligibility does not change when memory context is present.

Patch-loop boundary flags do not change when memory context is present.

Review-summary output separates:

- task intent
- proposal evidence
- validation evidence
- memory context
- missing authority
- forbidden actions

Memory context does not appear inside approval or authority fields.

## Validation

- Focused PR26: 51/51.
- PR25 focused lane: 38/38.
- PR24 focused lane: 44/44.
- PR23 focused lane: 41/41.
- PR22 focused lane: 30/30.
- PR21 focused lane: 44/44.
- PR20 focused lane: 31/31.
- CA focused lane: 16/16.
- BJ through PR26 authority corridor: 700/700.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.
- `git diff --check HEAD~1 HEAD`: passed.
