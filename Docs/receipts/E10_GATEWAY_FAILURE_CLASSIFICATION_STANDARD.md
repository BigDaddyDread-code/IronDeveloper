# E10 - Gateway Failure Classification Standard

## Review Line

A failure class explains what happened. It does not authorize what happens next.

## Purpose

Block E10 adds a canonical backend-only gateway failure classification standard for mutation-adjacent gateway failures.

It classifies failure phase, failure class, mutation-boundary state, routing hint, and matched evidence references so later retry, recovery, rollback, and human-triage flows can reason from a stable vocabulary.

## Boundary

Failure classification is not retry, recovery, rollback, resume, or mutation authority.

E10 is read-only. It does not retry, resume, recover, rollback, mutate, schedule, enqueue, acquire leases, release leases, renew leases, enforce locks, call executors, call Git, call GitHub, inspect worktrees, touch source files, update operation status, write retry records, write recovery records, promote memory, satisfy policy, approve work, mark ready, request reviewers, merge, release, deploy, or continue workflow.

Routing hints are next-assessment hints only. `MayProceedToRetryAssessment` means only that a future retry-assessment slice may inspect the failure. It is not retry execution, retry authorization, or source safety.

## Explicit Denials

E10 does not grant:

- mutation execution
- retry execution
- recovery execution
- rollback execution
- resume authority
- source apply authority
- commit authority
- push authority
- pull request authority
- approval
- policy satisfaction
- validation freshness
- patch freshness
- source safety
- workflow continuation
- merge readiness
- release readiness
- deployment readiness

Failure evidence, failure receipts, gateway refs, post-state observations, concurrent-guard decisions, lease observations, idempotency metadata, and routing hints are evidence only.

## Guard Rules

- Unknown failure classes block fail-closed.
- Unknown failure phases block fail-closed.
- Unknown mutation-boundary state blocks and routes to post-state observation.
- Retry assessment routing is possible only when the mutation boundary proves `MutationNotStarted`.
- Mutation-started, partially observed, completed, or may-have-started states never route to retry assessment.
- Post-state-related failures require post-state observation evidence.
- Concurrent-guard failures require concurrent-guard decision evidence.
- Lease failures require lease observation evidence.
- Idempotency conflicts require idempotency key and fingerprint evidence.
- Authority, approval, policy, and validation failures route to fresh gate assessment only.
- Receipt and read-model failures route to receipt/read-model assessment only.
- Raw patch, raw diff, raw source, command text, provider output, credential material, private reasoning, and authority-claim text are rejected.
- Valid domain refs such as `patch-package:*`, `merge-target:*`, `release-candidate:*`, and `deploy-target:*` are not rejected merely because they name mutation domains.

## Validation

- Focused E10 validation: 95/95 passed
- E09/E10 compatibility validation: 187/187 passed
- E01-E10 corridor: 719/719 passed
- Combined A02/A05 + D01-D20 + E01-E10 corridor: 2219/2219 passed
- Governance boundary CI: passed locally, including security boundary scan
- Build: 0 errors / 4 warnings
- `git diff --check`: passed with normal LF/CRLF warnings
- `git diff --cached --check`: passed with normal LF/CRLF warnings

## Review Traps

Reject this slice if:

- failure classification becomes retry authority
- failure classification becomes recovery authority
- failure classification becomes rollback authority
- failure classification becomes resume authority
- failure classification becomes mutation authority
- routing hint becomes executor eligibility
- gateway failure evidence becomes approval
- gateway failure receipt becomes permission
- post-state observation becomes source safety approval
- concurrent-guard evidence becomes mutation permission
- lease observation becomes mutation permission
- idempotency metadata becomes replay permission
- mutation boundary unknown is treated optimistically
- partial mutation is retried
- `MayProceedToRetryAssessment` appears for any state other than `MutationNotStarted`
- E10 calls E08, E09, rollback, recovery, source apply, commit, push, pull request, merge, release, deployment, or workflow-continuation executors
- E10 stores raw patch, source, command, provider output, private material, or secret-shaped fixture text
- E10 weakens E07, E08, or E09 boundaries
- broad unsafe marker scanning rejects valid domain refs like `release-candidate:e10`

## Killjoy

Failure classification is not retry, recovery, rollback, resume, or mutation authority.
