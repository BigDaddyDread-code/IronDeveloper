# E11 - Post-State Observation Standard

## Review Line

Post-state observation is evidence. It is not source safety.

## Purpose

Block E11 adds a backend-only, contract-only post-state observation standard for mutation-adjacent attempts.

It records, by reference, what state was observed after an attempt, how complete and trustworthy the observation is, and which next assessment surface may inspect it.

It does not observe anything itself.

## Boundary

Observed state is not permission to act on it.

E11 is read-only. It does not call executors, call Git, call GitHub, inspect source, inspect worktrees, retry, resume, recover, rollback, commit, push, open pull requests, merge, release, deploy, write operation status, write receipts, write read models, acquire locks, release locks, renew locks, enforce locks, promote memory, satisfy policy, approve work, or continue workflow.

Boundary signals are next-assessment hints only. They are not executor eligibility.

## Explicit Denials

E11 does not grant:

- source safety
- retry execution
- recovery execution
- rollback execution
- resume authority
- mutation authority
- source apply authority
- commit authority
- push authority
- pull request authority
- approval
- policy satisfaction
- validation freshness
- patch freshness
- workflow continuation
- merge readiness
- release readiness
- deployment readiness

Observation subject, method, transition, completeness, trust level, receipts, read-model refs, provider-state refs, and fingerprints are evidence only.

## Guard Rules

- Unknown subject, method, transition, completeness, or trust level blocks fail-closed.
- Stale and expired observations block.
- No-change observations can support retry assessment only when complete, fresh, trusted, and fingerprint-consistent.
- No-change observation is not retry authority.
- Expected-change observation is not source safety.
- Partial, divergent, provider-unknown, unavailable, and failed observations never support retry assessment.
- Self-reported observations require corroboration and never support retry assessment.
- Read-model-backed observation requires a read-model state ref and is still evidence only.
- Receipt-backed observation requires a receipt ref and is still evidence only.
- Synthetic test observations are valid only from test-labelled sources and never support retry assessment.
- Raw patch, raw diff, raw source, command text, provider output, credential material, private reasoning, and authority-claim text are rejected.
- Valid domain refs such as `patch-package:*`, `merge-target:*`, `release-candidate:*`, and `deploy-target:*` are not rejected merely because they name mutation domains.

## Validation

- Focused E11 validation: 93/93 passed
- E10/E11 compatibility validation: 188/188 passed
- E01-E11 corridor: 812/812 passed
- Combined A02/A05 + D01-D20 + E01-E11 corridor: 2312/2312 passed
- Governance boundary CI: passed locally, including security boundary scan
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed with normal LF/CRLF warnings

## Review Traps

Reject this slice if:

- observation result says `SourceSafe`
- observation result says `SafeToRetry`
- observation result says `CanContinue`
- no-change observation becomes retry authority
- expected-change observation becomes source safety
- post-state observation becomes recovery authority
- provider accepted outcome unknown routes to retry
- provider rejected after mutation started routes to retry
- partial or divergent state routes to retry
- self-reported observation supports retry assessment
- read-model-backed observation becomes operation truth
- receipt-backed observation becomes mutation authority
- stale or expired observation is accepted
- post-state observation calls E10, E09, E08, or any mutation service
- post-state observation reaches source apply, commit, push, pull request, rollback, recovery, merge, release, deployment, or workflow continuation executors
- post-state observation writes operation status
- post-state observation stores raw provider output, patch, diff, source, command text, commit body, pull request body, or private material
- broad unsafe marker scanning rejects valid refs like `release-candidate:e11`

## Killjoy

Observed state is not permission to act on it.
