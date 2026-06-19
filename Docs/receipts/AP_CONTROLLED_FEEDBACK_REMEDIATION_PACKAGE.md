# AP Controlled Feedback Remediation Package

## Review Line

Block AP packages review and CI feedback into bounded remediation evidence. It does not propose patches, apply source changes, update PR branches, approve, mark ready, request reviewers, merge, release, deploy, or continue workflow.

## Boundary

Feedback evidence is not accepted remediation.

AP may collect, preserve, classify, group, and package feedback evidence. AP must not generate patch content, apply source changes, mutate a PR branch, commit, push, mark feedback resolved, mark a PR ready, request reviewers, approve, merge, release, deploy, or continue workflow.

It does not propose patches.
It does not apply source changes.
It does not update PR branches.
It does not approve.
It does not mark ready.
It does not request reviewers.
It does not merge.
It does not release.
It does not deploy.
It does not continue workflow.

The package boundary records:

- EvidenceOnly = true
- CanProposePatch = false
- CanApplySource = false
- CanUpdatePullRequest = false
- CanCommit = false
- CanPush = false
- CanApprove = false
- CanMarkReadyForReview = false
- CanRequestReviewers = false
- CanMerge = false
- CanRelease = false
- CanDeploy = false
- CanContinueWorkflow = false

## AP Map

AP1 feedback source identity.

Each feedback item preserves source kind, source URL, source id, author, timestamps, commit SHA, file path, line, thread id, and bounded raw excerpt. AP supports GitHub review comments, review threads, issue comments, check runs, workflow runs, local validation receipts, and manual operator notes.

AP2 feedback classification.

Feedback is classified into actionable code, test, docs, or governance changes; environment failures; validation harness failures; stale feedback; resolved feedback; duplicates; non-actionable comments; human decisions; out-of-scope findings; authority risks; or unknown findings.

AP3 remediation candidates.

Remediation candidates group related feedback item ids and record disposition, rationale, affected areas, likely files, risk level, authority risk, suggested validation lanes, human-decision needs, and blocked reason. Candidate dispositions are bounded to Remediate, DoNotRemediate, NeedsClarification, Blocked, Duplicate, Stale, and OutOfScope.

AP4 staleness and duplicate handling.

Feedback tied to a non-current commit is stale and cannot drive automatic remediation. Feedback without enough commit binding requires a human decision. Duplicate comments are grouped instead of multiplying remediation authority.

AP5 evidence-only package receipt.

AP writes a feedback remediation package and receipt only. The package is not a patch proposal, source-apply approval, PR update package, branch update, approval, ready-for-review transition, reviewer request, merge, release, deploy, or workflow continuation.

AP6 authority bypass tests.

Focused AP tests prove forbidden CLI verbs fail closed and that AP package evidence cannot propose patches, apply source changes, update PRs, approve, mark ready, request reviewers, merge, release, deploy, or continue workflow.

## Killjoy

A review comment is not a fix plan. A CI failure is not automatically a product defect.
