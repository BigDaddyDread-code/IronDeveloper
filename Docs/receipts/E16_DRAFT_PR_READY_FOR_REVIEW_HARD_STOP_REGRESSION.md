# E16 — Draft PR Ready-for-Review Hard-Stop Regression

## Review line

Draft PR exists is not ready-for-review, merge-ready, or release-ready.

## Killjoy line

A draft PR receipt proves creation. It does not approve review, merge, release, or continuation.

## Scope

E16 is a regression hard-stop. It adds no ready-for-review action path.

This slice is regression-first plus receipt:

- `IronDev.IntegrationTests/BlockE16DraftPrReadyForReviewHardStopRegressionTests.cs`
- `Docs/receipts/E16_DRAFT_PR_READY_FOR_REVIEW_HARD_STOP_REGRESSION.md`
- `IronDev.Core/Governance/MergeReleaseSeparation.cs`

The E16 regression exposed one existing release-readiness leak: a pull request head branch ref could be supplied as `ReleaseCandidateRef` and produce release-decision readiness. The production change is limited to the existing merge/release separation contract and rejects `ReleaseCandidateRef` values that equal the PR head branch.

## Boundary

A draft PR may prove only that a draft pull request was created or observed as draft.

It explicitly does not grant or satisfy:

- ready-for-review authority
- review-request authority
- merge readiness
- release readiness
- deployment readiness
- workflow continuation
- approval
- policy satisfaction
- validation freshness
- source safety
- mutation authority
- source apply authority
- commit authority
- push authority
- pull request authority
- merge authority
- release authority
- deployment authority

## Regression proof

E16 proves that these draft PR facts remain evidence only:

- draft PR receipt exists
- draft PR URL exists
- draft PR number exists
- provider PR id exists
- draft PR creation status completed
- head/base refs exist
- validation refs exist
- receipt fingerprint exists
- read-model summary exists

The regression tests assert that draft PR evidence alone:

- cannot mark a PR ready for review
- cannot request reviewers
- cannot become merge readiness
- cannot become release readiness
- cannot become a release-candidate ref
- cannot continue workflow
- cannot satisfy approval
- cannot satisfy policy
- cannot refresh validation
- cannot prove source/worktree/branch safety
- cannot authorize source apply, commit, push, merge, release, deploy, or other mutation

## Static proof

The E16 test file statically verifies that the slice adds no:

- GitHub calls
- provider calls
- process execution
- executor wiring
- API, CLI, persistence, SQL, OpenAPI, or UI surface
- status/receipt store writes

## Validation

- Focused E16: 36/36 passed
- E15/E16 compatibility lane: 187/187 passed
- E01-E16 corridor: 1339/1339 passed
- Combined A02/A05 + D01-D20 + E01-E16 corridor: 2839/2839 passed
- Governance boundary CI local script: passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed with normal LF/CRLF warning on `IronDev.Core/Governance/MergeReleaseSeparation.cs`
- `git diff --cached --check`: passed
