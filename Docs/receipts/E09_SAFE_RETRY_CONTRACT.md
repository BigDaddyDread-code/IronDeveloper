# E09 - Safe Retry Contract

## Review Line

A failed attempt may be explainable. It is not permission to try again.

## Purpose

Block E09 adds a backend-only, contract-only safe retry assessment for failed mutation attempts.

It answers only whether a failed mutation attempt is safe enough to be considered for a new retry request at the next independent authority gate.

## Boundary

Retry classification is not retry authority.

E09 is read-only. It does not retry, resume, recover, rollback, mutate, schedule, enqueue, acquire leases, release leases, renew leases, enforce locks, call executors, call Git, call GitHub, inspect worktrees, touch source files, update operation status, write retry records, promote memory, satisfy policy, or continue workflow.

E09 may return `RetryRequestMayProceedToAuthorityGate` only when:

- the prior attempt is terminal `Failed`
- the failure class is known and pre-mutation
- mutation boundary state proves `NotStarted`
- failure receipt, terminal outcome, and post-state observation refs are present
- retry lineage and retry budget are bounded
- idempotency metadata is consistent
- the current concurrent guard decision is not blocking

That decision still requires fresh authority, fresh validation, fresh concurrent guard evidence, and fresh post-state observation before any retry can be requested or executed.

## Explicit Denials

E09 does not grant:

- retry execution
- automatic retry
- resume authority
- recovery authority
- rollback authority
- mutation authority
- approval
- policy satisfaction
- validation freshness
- patch freshness
- source safety
- workflow continuation
- merge readiness
- release readiness
- deployment readiness

Failure receipts, idempotency metadata, retry budgets, concurrent guard metadata, and post-state observations are evidence only.

## Guard Rules

- `Succeeded`, `Cancelled`, `Interrupted`, `Requested`, `InProgress`, and `Unknown` prior outcomes block retry consideration.
- `Interrupted` is recovery evidence, not retry evidence.
- `Unknown`, `Started`, `PartiallyObserved`, or `Completed` mutation boundary states block.
- Unknown side effects are not retry-safe.
- Blocked current concurrent guard states block E09.
- E08 not blocking is not retry authority.
- Same idempotency key and same fingerprint may proceed only as lineage metadata.
- Same idempotency key and different fingerprint blocks.
- Different idempotency key is not new authority.
- Retry budget is a limiter, not permission.
- Truncated retry lineage blocks fail-closed.
- Raw patch, raw diff, raw source, command text, provider output, credential material, private reasoning, and authority-claim text are rejected.
- Valid domain refs such as `patch-package:*`, `merge-target:*`, `release-candidate:*`, and `deploy-target:*` are not rejected merely because they name mutation domains.

## Validation

- Focused E09 validation: 88/88 passed
- E08 compatibility validation: 146/146 passed
- E01-E09 corridor: 620/620 passed
- Combined A02/A05 + D01-D20 + E01-E09 corridor: 2120/2120 passed
- Governance boundary CI: passed locally, including security boundary scan
- Build: 0 errors / 2 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed with normal LF/CRLF warnings

## Review Traps

Reject this slice if:

- decision vocabulary says `AllowedToRetry`
- decision vocabulary says `RetryAuthorized`
- `SafeRetryCandidateForNextGate` becomes executor permission
- same idempotency key becomes replay permission
- different idempotency key becomes new authority
- failed attempt becomes retry authority
- retry budget becomes retry authority
- current guard not blocking becomes retry authority
- failure receipt becomes retry authority
- post-state observation becomes retry authority
- mutation boundary unknown is treated optimistically
- partial mutation is retried
- interrupted attempt is treated as failed retry
- E09 calls E08 service directly
- E09 acquires, releases, renews, or enforces a lock
- E09 writes operation status
- E09 writes retry records
- E09 schedules retry
- E09 calls source apply, commit, push, pull request, rollback, merge, release, deployment, or workflow continuation executors
- E09 stores raw patch, source, command, provider output, private material, or secret-shaped fixture text
- E09 weakens E08 or E07 boundaries
- broad unsafe marker scanning rejects valid domain refs like `release-candidate:e09`

## Killjoy

Retry classification is not retry authority.
