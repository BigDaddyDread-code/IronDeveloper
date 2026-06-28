# E06 — Idempotency Key Contract

## Purpose

E06 is a backend-only, Core-only idempotency key contract for mutation-adjacent executor requests and receipts.

It answers one question: given supplied idempotency-key evidence, may the request continue to the next authority gate, or must it stop because the key/evidence is missing, malformed, stale, duplicate, conflicting, untrusted, or unsafe?

An idempotency key prevents accidental duplicate intent. It does not authorize execution.

Same key is not same authority. Same request is not permission to run it again.

## Backfill Note

E06 is a Block E backfill slice. It closes the previously unresolved idempotency-key contract gap after E07-E18 were already built.

E07 recorded that no E06 idempotency contract existed at that time. Later E slices used idempotency metadata in leases, concurrency guards, retry assessment, gateway classification, and post-state observation. E06 now gives that metadata a canonical non-authority contract without rewriting the existing stack.

## Boundary

E06 adds no idempotency store, executor wiring, persistence, API, CLI, UI, worker, Git, GitHub, provider, retry, recovery, release, deployment, or workflow-continuation path.

E06 does not grant:

- source apply authority
- commit authority
- push authority
- pull request authority
- ready-for-review authority
- merge authority
- release authority
- deployment authority
- retry authority
- recovery authority
- rollback authority
- workflow continuation
- approval
- policy satisfaction
- validation freshness
- source safety
- worktree safety
- branch safety
- mutation authority

## Decisions

The only positive-shaped decision is `MayProceedToNextAuthorityGate`.

That decision means the idempotency key did not stop the flow. It does not authorize execution.

Completed duplicate evidence returns `DuplicateCompletedNoExecution`. It prevents repeat execution, references prior completion evidence, and does not grant replay, retry, workflow continuation, or downstream authority.

In-progress duplicate evidence blocks. Prior failed or cancelled attempts block and require separate retry, recovery, or fresh authority.

Same key with different request, authority, target, or effect fingerprint blocks.

## Required Gates Remain Required

Every decision keeps these as required:

- fresh authority
- accepted approval
- policy satisfaction
- fresh validation
- concurrent guard
- dirty worktree guard
- moved-base guard
- stale-validation guard
- branch/remote/head verification
- fresh post-state observation
- human review

Idempotency evidence is a duplicate-intent guard. It is not any of those gates.

## Raw Material and Secret Boundary

E06 stores no raw request payloads, patches, diffs, source, command text, provider responses, credentials, or private reasoning. Unsafe material is rejected and redacted from decision echo fields and fingerprints.

The unsafe scan is phrase-specific. It rejects raw-payload and authority-claim phrases without rejecting valid domain refs such as `patch-package:e06`, `release-candidate:e06`, or `workflow-continuation:e06`.

## Validation

Recorded local validation for this slice:

```powershell
E06 focused: 188/188 passed
E06 + E18 compatibility lane: 252/252 passed
E01-E18 corridor: 1630/1630 passed
A02/A05 + D01-D20 + E01-E18 confidence lane: 3130/3130 passed
C11 secret-scanning regression: 9/9 passed
Build: 0 errors / 4 warnings
git diff --check: passed
git diff --cached --check: passed before staging; rerun after staging before commit
```

CI evidence is separate evidence. CI is not approval, policy satisfaction, execution permission, merge readiness, release readiness, deployment readiness, or workflow continuation.

## Review Traps

Reject E06 if it:

- creates an idempotency store
- calls SQL or persistence
- calls GitHub
- calls Git
- calls providers
- calls executors
- runs validation
- executes mutation
- retries mutation
- recovers mutation
- continues workflow
- treats duplicate completion as replay authority
- treats a prior failed attempt as retry authority
- treats key match as authority match
- treats key match as validation freshness
- treats key match as source safety
- accepts the same key with a different request, authority, target, or effect fingerprint
- stores raw payload, patch, diff, source, command, provider response, credential, or private reasoning material

## Killjoy

A complete hardening block is not a release decision. It is only evidence that the backend knows where authority stops.
