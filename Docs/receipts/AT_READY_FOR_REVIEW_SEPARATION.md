# Block AT - Ready-for-Review Separation

## Review Line

Block AT separates PR branch update evidence from ready-for-review authority. It packages ready-for-review eligibility evidence, but it does not mark ready, request reviewers, approve, merge, release, deploy, tag, publish, promote memory, or continue workflow.

## Purpose

Block AT creates a bounded ready-for-review eligibility package from PR identity, branch update evidence, validation receipts, and the phase authority receipt.

It exists because PR branch update evidence is useful input for a future ready-for-review executor, but it is not itself permission to transition the pull request.

## Boundary

PR branch update is not ready-for-review.

Ready-for-review is not reviewer request.

Reviewer request is not approval.

Approval is not merge.

Merge is not release.

Release is not deployment.

Validation evidence is not approval.

Ready-for-review package is not ready-for-review execution.

## AT1 - Read PR and Branch Evidence

AT may read PR metadata, AS branch update receipts, explicit no-update evidence, validation receipts, and phase authority receipts.

AT must verify PR identity, draft state, head branch, expected head SHA, base branch, base SHA, and stale package boundaries before producing eligibility evidence.

## AT2 - Bind AS Evidence

AS execution evidence must belong to the same repository, PR number, branch, and expected head SHA.

Executed-and-pushed AS evidence can satisfy the branch update input only when the receipt reports `Executed`, `Pushed = true`, and post-execution head equals the expected ready-for-review head.

Explicit no-update evidence is allowed only when it is supplied directly and bound to the same repository, PR number, branch, head, base branch, and base SHA. AT never silently treats missing AS evidence as no-update evidence.

## AT3 - Bind Validation Evidence

AT requires validation receipts for the expected ready-for-review head SHA.

The minimum validation families are focused current block, impacted area, fast authority invariant, build, diff-check, and phase authority.

Failed, skipped, stale, or missing validation evidence cannot produce ready-for-review eligibility.

## AT4 - Create Eligibility Package

AT may create a ready-for-review eligibility package with a verdict of `EligibleForReadyExecutor`, `Incomplete`, `Blocked`, or `Rejected`.

When eligible, the package may say a future AU executor can consume the evidence. The AT package itself still grants no mutation authority.

## AT5 - Write Receipt

AT writes a receipt that records the target PR, bound evidence, missing validation families, block reasons, and boundary statements.

The receipt is evidence only.

## AT6 - Authority Bypass Tests

AT bypass tests prove the package does not mark ready, request reviewers, resolve comments, approve, merge, auto-merge, release, deploy, tag, publish, promote memory, or continue workflow.

## CLI Boundary

Allowed read/package commands:

```text
irondev ready package
irondev ready inspect
irondev ready status
irondev ready records
```

Forbidden authority-shaped commands:

```text
irondev ready mark-ready
irondev ready request-reviewers
irondev ready approve
irondev ready resolve-comments
irondev ready merge
irondev ready auto-merge
irondev ready release
irondev ready deploy
irondev ready tag
irondev ready publish
irondev ready promote-memory
irondev ready continue
```

## Killjoy

A branch update can make review possible. It does not press the ready button.
