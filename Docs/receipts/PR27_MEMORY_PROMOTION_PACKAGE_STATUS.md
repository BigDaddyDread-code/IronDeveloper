# PR27 Memory Promotion Package Status

## Purpose

PR27 adds canonical governed status for memory promotion packages.

Memory promotion gets explicit authority requirements before anything can become durable memory.

No automatic promotion.

No memory self-promotion.

No workflow continuation from memory.

Review line:

> Useful memory still needs permission before becoming durable memory.

Killjoy:

> A good reminder is still not a memory write.

## Candidate Status Behavior

The memory promotion status mapper may report:

- candidate created
- candidate blocked
- candidate unsafe
- candidate missing authority
- candidate eligible for human decision
- candidate failed

The mapper creates canonical `GovernedOperationStatus` only.

It does not write durable memory.

It does not call a memory store.

It does not promote the candidate.

## Explicit Promotion Authority Requirements

Every promotable candidate requires:

- `accepted-memory-promotion-request`
- `memory-promotion-authority`
- `memory-safety-review`
- `memory-scope-decision`

Portable engineering memory also requires:

- `portable-memory-sanitization-review`
- `cross-project-confidentiality-check`

Project-local memory also requires:

- `project-local-memory-scope-confirmation`

## Blocked-State Examples

Candidate-created status without explicit promotion authority is `Blocked`.

Unsafe memory content is `Blocked`.

Cross-repo authority transfer is `Blocked`.

Unsanitized portable memory is `Blocked`.

Self-promotion attempts are `Blocked`.

Eligible-for-human-decision status is not memory promotion.

## Allowed Candidate Types

Allowed candidate kinds:

- prior failure hint
- project convention
- previous pattern
- sanitized engineering heuristic

Allowed scopes:

- project-local candidate with project-local scope confirmation
- run-local candidate with explicit scope decision
- portable engineering memory with sanitization review and confidentiality check

## Rejected Candidate Types

Rejected candidate content includes:

- memory approval text
- memory policy satisfaction text
- source apply authority text
- rollback authority text
- commit, push, or PR authority text
- workflow continuation text
- release or deployment authority text
- self-promotion text
- cross-repo authority transfer
- unsanitized portable memory
- client, schema, ticket, incident, source, approval, release, or deployment facts from another project

Portable memory can carry sanitized lessons, not authority or confidential project truth.

## Boundary Flags

`MemoryPromotionBoundary.Status` is:

```text
StatusOnly = true
CandidateOnly = true
CanPromoteMemory = false
CanSelfPromote = false
CanApprove = false
CanSatisfyPolicy = false
CanAuthorizeSourceApply = false
CanContinueWorkflow = false
CanTransferCrossRepoAuthority = false
```

## Non-Authority Proof

Memory promotion package is not durable memory.

Candidate memory is not durable memory.

Memory candidate is not approval.

Memory candidate is not policy satisfaction.

Memory candidate is not source apply authority.

Memory candidate is not rollback authority.

Memory candidate is not commit authority.

Memory candidate is not push authority.

Memory candidate is not PR authority.

Memory candidate is not workflow continuation.

Memory candidate cannot promote itself.

Useful memory still needs permission before becoming durable memory.

## No-Mutation Proof

PR27 adds status mapping, formatting warnings, focused tests, and this receipt.

It does not add:

- durable memory write
- memory promotion executor
- memory store writer
- approval acceptance
- policy satisfaction
- source apply execution
- rollback execution
- commit
- push
- PR creation
- merge
- release
- deployment
- provider calls
- frontend or UI controls
- workflow continuation

## Status Readout

Status output preserves:

- blocked reasons
- missing evidence
- next safe action
- forbidden actions
- evidence refs
- receipt refs
- authority warnings

Required warnings:

```text
Memory promotion package is not durable memory.
Useful memory still needs permission before becoming durable memory.
Memory cannot approve, satisfy policy, authorize mutation, promote itself, or continue workflow.
```

## Validation

- Focused PR27: 49/49.
- PR26 focused lane: 51/51.
- PR25 focused lane: 38/38.
- PR24 focused lane: 44/44.
- PR23 focused lane: 41/41.
- PR22 focused lane: 30/30.
- PR21 focused lane: 44/44.
- PR20 focused lane: 31/31.
- CA focused lane: 16/16.
- BJ through PR27 authority corridor: 749/749.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.
- `git diff --check HEAD~1 HEAD`: passed.
