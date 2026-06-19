# PR309-314 - Controlled Commit Package

Block AL creates a controlled commit package for human review.

It prepares evidence a human or later governed executor needs before a commit can safely exist.

It does not add commit authority.

## What Landed

- AL1 creates commit package request.
- AL2 builds commit file manifest.
- AL3 collects commit evidence bundle.
- AL4 creates commit message proposal.
- AL5 performs Killjoy commit readiness review.
- AL6 proves commit evidence cannot bypass authority.

## Boundary

Block AL prepares a controlled commit package.

It does not stage files.
It does not create commits.
It does not push.
It does not create pull requests.
It does not merge.
It does not release.
It does not deploy.
It does not continue workflow.
It does not mutate source.
It does not promote memory.
It does not satisfy policy.
It does not approve commits.

Commit package artifacts are review material only.
Human staging and committing remain outside Block AL.

## CLI Surface

Supported commands:

- `irondev commit-package request --run <run-id-or-path> --source-repo <path> [--json]`
- `irondev commit-package manifest --run <run-id-or-path> --source-repo <path> [--json]`
- `irondev commit-package evidence --run <run-id-or-path> [--json]`
- `irondev commit-package message --run <run-id-or-path> [--json]`
- `irondev commit-package review --run <run-id-or-path> [--json]`
- `irondev commit-package status --run <run-id-or-path> [--json]`

Unsupported authority-shaped commands:

- `stage`
- `commit`
- `push`
- `pr`
- `merge`
- `release`
- `deploy`
- `continue`

## Artifact Set

The CLI writes run-scoped commit package artifacts:

- `commit-package-request.json`
- `commit-package-request.md`
- `commit-file-manifest.json`
- `commit-file-manifest.md`
- `commit-staging-plan.json`
- `commit-staging-plan.md`
- `commit-evidence-bundle.json`
- `commit-evidence-bundle.md`
- `commit-message-proposal.json`
- `commit-message-proposal.md`
- `commit-readiness-review.json`
- `commit-readiness-review.md`
- `commit-package-risk-report.json`
- `commit-package-risk-report.md`
- `commit-package-boundary-report.json`
- `commit-package-boundary-report.md`
- `commit-package-bypass-report.json`
- `commit-package-bypass-report.md`
- `governance-events.jsonl`

## Governance Events

AL records evidence-only events:

- `CommitPackageRequestCreated`
- `CommitFileManifestCreated`
- `CommitEvidenceBundleCreated`
- `CommitMessageProposalCreated`
- `CommitReadinessReviewCreated`
- `CommitPackageBoundaryReportCreated`
- `CommitPackageBypassReportCreated`

These events are not staging permission, commit permission, push permission, PR creation permission, merge permission, release permission, deployment permission, workflow continuation, memory promotion, source mutation, policy satisfaction, or approval.

## Bypass Proof

The following remain evidence only:

- commit package request
- file manifest
- human staging plan
- evidence bundle
- commit message proposal
- readiness review
- risk report
- boundary report
- test pass
- build pass
- diff-check pass

None of these can stage files, create commits, push, create pull requests, merge, release, deploy, continue workflow, mutate source, promote memory, satisfy policy, or approve a commit.

## Review

Block AL prepares a controlled commit package. It does not stage, commit, push, create PRs, merge, release, deploy, or continue workflow.

## Killjoy

The package can tell a human what to commit. It cannot touch the index.
