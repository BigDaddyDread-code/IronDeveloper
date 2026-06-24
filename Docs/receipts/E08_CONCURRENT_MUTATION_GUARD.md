# E08 - Concurrent Mutation Guard

## Review Line

A concurrent mutation guard prevents overlap. It does not grant permission.

## Purpose

Block E08 adds a backend-only, read-only guard contract and service for detecting unsafe concurrent mutation attempts against the same governed mutation surface and target scope.

This is not a distributed lock implementation, executor preflight integration, SQL persistence layer, API endpoint, CLI command, frontend path, or workflow runner. It only evaluates bounded reference metadata from a read store and returns a deterministic guard decision.

## Boundary

No conflict found is not authority to mutate.

E08 does not acquire, release, renew, or enforce leases.

E08 may answer only:

- whether a known active mutation conflicts with the same tenant, project, mutation surface, and target
- whether a conflicting idempotency key / fingerprint exists
- whether a conflicting held lease observation exists
- whether observation evidence is stale, unknown, truncated, malformed, or unsafe
- whether the caller may proceed to the next independent authority gate

E08 may not:

- execute source apply, commit, push, PR creation/update, ready-for-review, reviewer request, merge, release, deploy, rollback, retry, recovery, resume, memory promotion, or workflow continuation
- acquire, release, renew, or enforce leases
- write lock state
- update operation state
- call Git or GitHub
- touch source files
- apply patches
- approve work
- satisfy policy
- validate evidence freshness
- validate patch freshness
- prove source safety
- grant mutation authority
- store raw patch, diff, source, command, provider output, private material, or secret material

## Guard Rules

- Same tenant, project, mutation surface, and target with another active mutation blocks as `BlockedByActiveMutation`.
- Active states are `Requested`, `InProgress`, `ObservedHeld`, `ObservedDenied`, `ObservedConflicted`, and `Unknown`.
- `ObservedDenied` is not safe progress. It may still represent a contested mutation window.
- Same idempotency key with a different fingerprint blocks as `BlockedByConflictingIdempotency`.
- A different idempotency key against active work on the same target blocks as `BlockedByActiveMutation`.
- A held lease for another owner, attempt, or fence blocks as `BlockedByConflictingLease`.
- A past expiry timestamp alone does not release a held lease. The state must explicitly be `ObservedExpired`.
- Unknown, malformed, future, stale, missing, or truncated observation evidence blocks fail-closed.
- Terminal prior states may remove the overlap block, but they do not authorize retry, resume, replay, rollback, or mutation.
- `AllowedToProceedToNextGate` means only that E08 is not the current blocker. It does not mean `AllowedToMutate`.

## Validation

- Focused E08 validation: 51/51 passed
- E07 compatibility validation: 154/154 passed
- E01-E08 corridor: 525/525 passed
- Combined A02/A05 + D01-D20 + E01-E08 corridor: 2025/2025 passed
- Governance boundary CI: passed locally, including security boundary scan
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed with normal LF/CRLF warnings

## Review Traps

Reject this slice if:

- the decision vocabulary says `AllowedToMutate`
- idempotency match becomes replay permission
- no active conflict becomes mutation permission
- expired lease timestamp becomes release proof
- terminal failed / interrupted state becomes retry or resume permission
- E08 writes to a lock or lease table
- E08 updates operation state
- E08 calls executor code
- E08 reaches Git, GitHub, source, patch, worktree, API, CLI, frontend, or worker surfaces
- E08 stores raw patch, source, output, command, payload, private material, or secret-shaped fixture text
- E08 weakens E07 lease boundaries
- E08 bypasses tenant or project isolation

## Killjoy

No conflict found is not authority to mutate.
