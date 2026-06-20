# AY Controlled Merge Executor

Block AY consumes an eligible AX merge decision package, re-observes the current pull request state, executes exactly the package-selected merge strategy against the expected pull request head, verifies the merged pull request state, and writes a merge execution receipt.

## Boundary

AY may perform one controlled mutation:

```text
merge the expected PR head into the expected base branch
```

AY does not approve, submit reviews, resolve review threads, request reviewers, mark ready, enable auto-merge, release, deploy, tag, publish, promote memory, commit local changes, push local branches, mutate source, or continue workflow.

## Required Lines

Merge decision package is not merge execution.
Merge execution is not release.
Release is not deployment.
Merge execution is not tag creation.
Merge execution is not publishing.
Merge execution is not memory promotion.
Merge execution is not workflow continuation.
Approval is not merge.
Validation evidence is not approval.
No self-approval.
No hidden mutation.
AY merges only the expected PR head into the expected base branch.
AY does not enable auto-merge.
AY does not release.
AY does not deploy.
AY does not tag.
AY does not publish.
AY does not promote memory.
AY does not continue workflow.

## Acceptance

AY requires an eligible AX merge decision package, an evidence-only AX boundary, `CanMergeForExecutor = true`, matching repository and pull request identity, matching head branch and head SHA, matching base branch and base SHA where observable, a supported selected merge strategy, a live open non-draft unmerged mergeable pull request, no conflicts, and no behind-base state.

After the merge mutation, AY re-observes the pull request and reports success only when post-state proves the pull request is merged and a merge commit is present.

## Review Line

Block AY consumes an eligible AX merge decision package and performs the controlled merge. It does not release, deploy, tag, publish, promote memory, or continue workflow.
