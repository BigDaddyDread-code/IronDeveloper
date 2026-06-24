# E07 - Mutation Lease / Lock Contract

## Review Line

Mutation lease/lock contract defines bounded reference-only lease metadata for future mutation executors. It does not acquire, release, renew, or enforce leases; does not approve work; does not satisfy policy; does not validate freshness; does not grant authority; does not execute mutation; and does not continue workflow.

## Purpose

Block E07 defines the shared mutation lease / lock contract shape used by future mutation executors to describe lease scope, lease observation, idempotency-key binding, token references, fence references, sequence references, and expiry metadata.

This slice is contract-only. It does not introduce a lease store, lock manager, executor integration, API endpoint, CLI command, workflow runner, SQL schema, source mutation, patch mutation, validation execution, approval creation, policy satisfaction, or workflow continuation.

## Boundary

A mutation lease is a concurrency witness. It is not mutation authority.

The E07 contract may describe:

- tenant, project, operation, and correlation identity
- mutation surface kind
- mutation target reference
- idempotency key fingerprint
- lease mode
- lease owner reference
- lease token reference
- fence token reference
- sequence reference
- requested, observed, expiry, release, denial, and conflict timestamps / reasons
- redaction and record fingerprint metadata

The E07 contract may not:

- acquire a lease
- release a lease
- renew a lease
- enforce a lock
- execute source apply, commit, push, PR creation/update, ready-for-review, reviewer request, merge, release, deploy, rollback, retry, recovery, resume, memory promotion, or workflow continuation
- approve work
- satisfy policy
- validate freshness
- validate patch applicability
- validate source state
- validate execution safety
- grant executor eligibility
- expose raw patch, diff, source, command, provider output, private material, or secret material

## Contract Rules

- Lease scope must bind tenant id, project id, operation id, correlation id, mutation surface kind, mutation target reference, and idempotency-key fingerprint.
- Idempotency keys are validated for presence and safe shape, but the contract result exposes only the fingerprint.
- Source apply, commit, push, draft PR, ready-for-review, reviewer request, merge, release, deploy, rollback, recovery, memory promotion, and workflow continuation are valid surface labels only. The labels do not grant authority.
- Requested records may omit token/fence/sequence references because no lock acquisition is represented.
- Held lease observations require token, fence, sequence, observed timestamp, and expiry metadata.
- Released lease observations require release metadata.
- Denied and conflicted lease observations require reasons.
- Expired lease metadata does not authorize retry, recovery, rollback, resume, or workflow continuation.
- Contract validation success is not executor eligibility.

## Validation

- Focused E07 validation: 103/103 passed
- E01-E05 mutation persistence corridor: 371/371 passed
- E06 validation: not applicable; no E06 idempotency contract exists in this repository at the time of this slice
- D10/D16 read-model corridor: 194/194 passed
- D01-D20 operation-status corridor: 1439/1439 passed
- A02/A05 frontend-readiness read corridor: 61/61 passed
- Combined A02/A05 + D01-D20 + E01-E05 + E07 corridor: 1974/1974 passed
- Governance boundary CI: passed locally, including security boundary scan
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed with normal LF/CRLF warnings

## Review Traps

Reject this slice if:

- E07 acquires, releases, renews, or enforces a lease
- E07 adds a lock manager, store, SQL schema, API, CLI, workflow runner, or executor integration
- lease validity becomes mutation authority
- lease token, fence token, or sequence reference becomes mutation authority
- idempotency-key match becomes replay authority
- expiry metadata becomes retry / resume / recovery authority
- lease metadata becomes approval, policy satisfaction, validation freshness, patch freshness, source-state proof, or execution proof
- raw patch, diff, source, command, provider output, private material, or secret-shaped fixture text is committed

## Killjoy

The lock label is not the lock. The lock is not authority. Authority still has to show its papers at the executor boundary.
