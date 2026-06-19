# Phase 1 Close Feedback Loop

## Review Line

Phase 1 closes the feedback loop. It does not approve, mark ready, request reviewers, merge, release, deploy, tag, publish, promote memory, or continue workflow.

## Boundary

Phase 1 moves feedback through four controlled authority slices:

- AP packages review and CI feedback into bounded remediation evidence.
- AQ turns eligible AP remediation candidates into bounded manual-review patch proposal evidence.
- AR packages a proposed PR branch update with PR identity, expected branch/head state, validation evidence, source-apply posture, and rollback posture.
- AS executes an eligible AR PR update package against the expected PR branch and writes an execution receipt.

Authority chain:

```text
Feedback is not accepted remediation.
Accepted remediation is not patch proposal.
Patch proposal is not source apply.
Source apply is not PR update package.
PR update package is not PR branch mutation.
PR branch update is not ready-for-review.
Ready-for-review is not reviewer request.
Reviewer request is not merge readiness.
Merge readiness is not release readiness.
Validation evidence is not approval.
Rollback plan is not rollback execution.
Runner stays dumb.
No self-approval.
No hidden mutation.
```

## Phase Map

### AP - Controlled Feedback Remediation Package

AP packages feedback and preserves source identity, classification, staleness, duplicate grouping, remediation disposition, likely files, risk, authority risk, validation expectations, and human-decision posture.

AP evidence cannot propose patches, apply source, update PR branches, approve, mark ready, request reviewers, merge, release, deploy, or continue workflow.

### AQ - Controlled Feedback Patch Proposal

AQ requires an AP remediation package and creates manual-review patch proposal evidence for eligible remediation candidates.

AQ evidence cannot apply source, mutate workspaces, commit, push, update PR branches, approve, mark ready, request reviewers, merge, release, deploy, or continue workflow.

### AR - Controlled PR Update Package

AR requires an AQ patch proposal and packages PR identity, expected branch/head state, expected changed files, source-apply posture, validation evidence, branch update constraints, and rollback posture.

AR evidence cannot apply patches, mutate source, stage, commit, push, update PR branches, mark ready, request reviewers, approve, merge, release, deploy, or continue workflow.

### AS - Controlled PR Branch Update Executor

AS requires an eligible AR package, re-verifies package eligibility, validates branch/head/file/diff state, commits only the package-declared file set, pushes only to the package target remote and branch, re-observes after commit and push, and writes an execution receipt.

AS execution does not approve, mark ready, request reviewers, resolve review threads, merge, release, deploy, tag, publish, promote memory, or continue workflow.

## Review Traps

Reject Phase 1 if:

- feedback becomes accepted remediation
- AQ proposal becomes source apply authority
- AQ proposal becomes PR update authority
- AR package mutates source or branch
- AS executes without an eligible AR package
- AS skips branch, head, file, or diff validation
- AS marks ready, requests reviewers, resolves comments, approves, merges, releases, deploys, tags, publishes, promotes memory, or continues workflow
- validation evidence is treated as approval
- rollback plan is treated as rollback execution

## Validation

The phase boundary is covered by `Phase1CloseFeedbackLoopAuthorityTests`.

The combined AP/AQ/AR/AS boundary lane remains useful, but it is not a substitute for the explicit phase cross-boundary authority lane.

## Killjoy

Closing feedback is not permission to ship. Phase 1 moves work through the feedback loop without collapsing the locks.
