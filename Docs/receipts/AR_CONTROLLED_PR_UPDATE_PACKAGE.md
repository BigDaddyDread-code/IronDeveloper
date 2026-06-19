# AR Controlled PR Update Package

## Review Line

Block AR packages a proposed PR branch update with evidence, expected branch state, validation requirements, and rollback posture. It does not apply patches, commit, push, update PR branches, mark ready, request reviewers, merge, release, deploy, or continue workflow.

## Boundary

PR update package is not PR branch mutation.

AR may read an AQ feedback patch proposal, source-apply evidence or an explicit pending-apply posture, validation receipts, current PR identity metadata, expected branch/head state, expected changed files, expected commit text, rollback posture, branch update constraints, and write a controlled PR update package receipt.

It does not apply patches.
It does not mutate source.
It does not mutate workspaces.
It does not stage files.
It does not commit.
It does not push.
It does not update PR branches.
It does not approve.
It does not mark ready.
It does not request reviewers.
It does not merge.
It does not release.
It does not deploy.
It does not continue workflow.

## AR Map

AR1 AQ proposal required.

AR requires a `FeedbackPatchProposal`. Missing proposals are rejected. The package binds the proposal to the target PR number, expected current head SHA, and base SHA. Mismatches fail closed.

AR2 PR identity binding.

AR records repository, PR number, PR URL, PR state, draft state, target branch, expected current head SHA, base branch, base SHA, package timestamp, and package creator. Closed PRs and mismatched branch or head evidence cannot become executor-ready.

AR3 validation evidence binding.

AR records validation receipts and requires evidence for focused current block, impacted area, fast authority invariant, build, and diff-check families. Skipped required lanes, missing required families, failed receipts, or receipts tied to another SHA prevent execution eligibility. Validation receipts remain evidence, not approval.

AR4 source-apply and rollback posture.

AR records either real source-apply evidence or `SourceApplyPending = true`. Pending source apply forces `CanExecuteBranchUpdate = false`. The rollback plan records rollback availability, strategy, pre-update head SHA, expected post-update head SHA when known, a non-executable preview, and rollback risks. Rollback plan is not rollback execution.

AR5 evidence-only package receipt.

AR writes `pr-update-package.json`, `pr-update-package-receipt.json`, `pr-update-package-summary.md`, validation records, and governance events. The package is evidence only and cannot be used as source-apply approval, PR branch update execution, commit approval, merge readiness, release readiness, or workflow continuation.

AR6 authority bypass tests.

Focused AR tests prove forbidden CLI verbs fail closed and that AR package evidence cannot apply patches, mutate source, stage files, commit, push, update PR branches, mark ready, request reviewers, approve, merge, release, deploy, or continue workflow.

## Killjoy

A PR update package is not a push. It is a locked envelope saying what a push would be allowed to do.
