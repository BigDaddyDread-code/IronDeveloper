# PR315-318 - Controlled Pull Request Creation

Block AM creates a controlled draft pull request only.

It creates a review container from verified branch and commit-package evidence.

A draft PR is not approval.
A draft PR is not merge readiness.
A draft PR is not release readiness.
A draft PR is not deployment readiness.

## What Landed

- AM1 pull request creation request.
- AM2 branch and commit evidence validation.
- AM3 pull request title/body proposal.
- AM4 draft PR creation gate.
- AM5 draft PR creation executor and receipt.
- AM6 bypass tests and receipt.

## Boundary

Block AM creates a controlled draft pull request only.

It does not commit.
It does not stage files.
It does not push.
It does not force-push.
It does not create branches.
It does not create non-draft PRs.
It does not mark ready for review.
It does not request reviewers.
It does not approve PRs.
It does not merge.
It does not release.
It does not deploy.
It does not continue workflow.
It does not mutate source.
It does not mutate workspaces.
It does not promote memory.
It does not satisfy policy.

AM may open a draft review container. It may not move code into that container.

## CLI Surface

Supported commands:

- `irondev pull-request request --run <run-id-or-path> --repo <owner/name> --base <branch> --head <branch> --expected-head <sha> [--json]`
- `irondev pull-request validate --run <run-id-or-path> [--json]`
- `irondev pull-request text --run <run-id-or-path> [--json]`
- `irondev pull-request gate --run <run-id-or-path> --decision <decision.json> --thought-ledger-ref <ref> [--json]`
- `irondev pull-request create-draft --run <run-id-or-path> [--json]`
- `irondev pull-request status --run <run-id-or-path> [--json]`

Unsupported authority-shaped commands:

- `create`
- `create-ready`
- `ready`
- `request-reviewers`
- `approve`
- `merge`
- `release`
- `deploy`
- `push`
- `commit`
- `continue`

## Artifact Set

The AM CLI writes run-scoped artifacts:

- `pull-request-creation-request.json`
- `pull-request-creation-request.md`
- `pull-request-branch-validation.json`
- `pull-request-branch-validation.md`
- `pull-request-evidence-validation.json`
- `pull-request-evidence-validation.md`
- `pull-request-text-proposal.json`
- `pull-request-text-proposal.md`
- `pull-request-creation-gate.json`
- `pull-request-creation-gate.md`
- `pull-request-created-receipt.json`
- `pull-request-created-receipt.md`
- `pull-request-status.json`
- `pull-request-status.md`
- `pull-request-creation-bypass-report.json`
- `pull-request-creation-bypass-report.md`
- `governance-events.jsonl`

## Gate

The AM4 gate may allow only:

- `CreateDraftPullRequest`

The gate must not allow:

- non-draft PR creation
- ready-for-review
- reviewer requests
- merge readiness
- release readiness
- deployment readiness
- workflow continuation

## Bypass Proof

These remain evidence only and cannot create a pull request:

- commit package request alone
- commit readiness review alone
- PR text proposal alone
- branch validation alone
- test success
- build success
- artifact consistency report
- release readiness report
- chat text
- AI review text
- memory plan text
- human-looking approval text

Only the AM5 executor may call draft PR creation, and only after AM4 has returned `CreateDraftPullRequest` for the same request and expected head SHA.

## Review

Block AM creates one controlled draft pull request from verified evidence. It does not commit, push, mark ready, request review, merge, release, deploy, or continue workflow.

## Killjoy

A draft PR is a question, not a merge.
