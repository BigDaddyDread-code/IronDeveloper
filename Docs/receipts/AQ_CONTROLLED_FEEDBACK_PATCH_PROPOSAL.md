# AQ Controlled Feedback Patch Proposal

## Review Line

Block AQ turns an AP remediation package into a bounded patch proposal. It does not apply source changes, commit, push, update PR branches, approve, mark ready, request reviewers, merge, release, deploy, or continue workflow.

## Boundary

Patch proposal is not source apply.

AQ may read an AP feedback remediation package, select eligible remediation candidates, produce attributed manual-review patch proposal artifacts, identify expected changed files, record validation expectations, flag unsafe proposal material, and write a proposal receipt.

It does not apply source changes.
It does not mutate workspaces.
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

## AQ Map

AQ1 AP package required.

AQ requires a `FeedbackRemediationPackage`. Missing or invalid packages are rejected. Optional PR/head checks bind the proposal to the expected pull request and head SHA; mismatches fail closed unless a future stale-aware path explicitly records that choice.

AQ2 candidate eligibility.

AQ proposes hunks only for remediation candidates with `Disposition = Remediate` and `RequiresHumanDecision = false`. Candidates requiring human decision, stale candidates, duplicate-only candidates, blocked candidates, and do-not-remediate candidates are skipped with recorded reasons.

AQ3 hunk attribution.

Every proposed file and every proposed hunk records its remediation candidate ids. Orphan hunks are not allowed.

AQ4 patch safety checks.

AQ blocks generated paths such as `obj`, `bin`, package caches, temporary NuGet configs, and binary output. AQ also rejects absolute, parent-traversal, UNC, home-relative, and environment-shaped paths before they can enter proposal evidence. Governance/authority paths require an authority-risk flag before a proposal can include them.

AQ5 evidence-only proposal receipt.

AQ writes `feedback-patch-proposal.json`, `feedback-patch-proposal-notes.md`, `feedback-patch-proposal-summary.md`, hunk records, and a receipt. The notes artifact is a manual-review proposal, not a `.diff`; AQ does not emit fake diff artifacts when it lacks trusted original source context. Proposed hunks carry `PatchApplicability = ManualReviewOnly` unless a future governed phase supplies real source context and structural patch validation. The proposal is evidence only and cannot be used as source-apply approval, source-apply execution evidence, PR update package, branch update, commit approval, merge readiness, release readiness, or workflow continuation.

AQ6 authority bypass tests.

Focused AQ tests prove forbidden CLI verbs fail closed and that AQ proposal evidence cannot apply source, mutate workspaces, commit, push, update PR branches, mark ready, request reviewers, merge, release, deploy, or continue workflow.

## Killjoy

A proposed patch is not a changed branch. It is a suggestion with line numbers.
