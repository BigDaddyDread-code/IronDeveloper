# E18 - Downstream Authority Attempt Detection

## Review line

A receipt can describe what happened. It cannot authorize what happens next.

## Killjoy line

Receipts are witnesses, not permission slips.

## Scope

E18 is a downstream-authority hard-stop regression. It adds no execution, approval, merge, release, deployment, or workflow-continuation path.

This slice adds:

- `IronDev.IntegrationTests/BlockE18DownstreamAuthorityAttemptDetectionTests.cs`
- `Docs/receipts/E18_DOWNSTREAM_AUTHORITY_ATTEMPT_DETECTION.md`

No production code was changed.

## Boundary

Receipt, evidence package, guard decision, operation status, and read-model output may describe what happened or what is still required.

They explicitly do not grant or satisfy:

- source apply authority
- commit authority
- push authority
- pull request authority
- ready-for-review authority
- review-request authority
- merge authority
- release authority
- deployment authority
- rollback authority
- retry authority
- recovery authority
- workflow continuation
- approval
- policy satisfaction
- validation freshness
- source safety
- worktree safety
- branch safety
- memory promotion authority
- mutation authority

## Regression proof

E18 covers these receipt/evidence/status/read-model families:

- source apply receipt
- commit package and commit receipt
- push receipt
- draft PR receipt
- PR branch update receipt
- ready-for-review package and execution receipt
- rollback receipt
- retry classification evidence
- recovery evidence
- validation result receipt/package
- post-state observation
- dirty worktree guard decision
- moved-base guard decision
- stale-validation guard decision
- branch/remote/head verification decision
- merge-readiness evidence package
- release-readiness evidence package
- operation status and read-model summaries

The test-only detector returns findings only. Findings are not authority.

## Validation

- Focused E18: 64/64 passed
- E17/E18 compatibility lane: 103/103 passed
- E01-E18 corridor: 1442/1442 passed
- Combined A02/A05 + D01-D20 + E01-E18 corridor: 2942/2942 passed
- Governance boundary CI local script: passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed with normal LF/CRLF warning
