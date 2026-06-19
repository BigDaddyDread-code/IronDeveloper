# PR319-324 - CI and Review Feedback Loop

Block AN observes CI and review feedback for a controlled PR and turns that feedback into bounded, reviewable evidence.

AN is an observation, classification, and planning block.

It does not fix the PR.

## What Landed

- AN1 feedback loop request.
- AN2 CI status observation.
- AN3 review feedback observation.
- AN4 feedback classification.
- AN5 remediation plan proposal.
- AN6 feedback readiness report.
- AN7 bypass tests and receipt.

## Boundary

Block AN observes CI and review feedback.

It does not commit.
It does not stage files.
It does not push.
It does not force-push.
It does not create branches.
It does not update PRs.
It does not create PRs.
It does not reply to comments.
It does not resolve review threads.
It does not dismiss reviews.
It does not request reviewers.
It does not mark PRs ready.
It does not rerun CI.
It does not set commit status.
It does not merge.
It does not release.
It does not deploy.
It does not continue workflow.
It does not mutate source.
It does not mutate workspaces.
It does not promote memory.
It does not satisfy policy.

AN may point at feedback. It may not apply the fix path.

## CLI Surface

Supported commands:

- `irondev feedback request --run <run-id-or-path> --repo <owner/name> --pr <number> --expected-head <sha> [--json]`
- `irondev feedback ci --run <run-id-or-path> [--json]`
- `irondev feedback review --run <run-id-or-path> [--json]`
- `irondev feedback classify --run <run-id-or-path> [--json]`
- `irondev feedback plan --run <run-id-or-path> [--json]`
- `irondev feedback readiness --run <run-id-or-path> [--json]`
- `irondev feedback status --run <run-id-or-path> [--json]`

Unsupported authority-shaped commands:

- `fix`
- `apply`
- `commit`
- `push`
- `reply`
- `resolve`
- `rerun-ci`
- `ready`
- `request-reviewers`
- `merge`
- `release`
- `deploy`
- `continue`

## Artifact Set

The AN CLI writes run-scoped artifacts:

- `feedback-loop-request.json`
- `feedback-loop-request.md`
- `ci-observation-snapshot.json`
- `ci-observation-report.md`
- `ci-failure-excerpts.jsonl`
- `review-feedback-snapshot.json`
- `review-feedback-report.md`
- `review-feedback-comments.jsonl`
- `feedback-classification-report.json`
- `feedback-classification-report.md`
- `feedback-findings.jsonl`
- `feedback-remediation-plan.json`
- `feedback-remediation-plan.md`
- `suggested-feedback-test-profile.json`
- `feedback-known-risks.md`
- `feedback-readiness-report.json`
- `feedback-readiness-report.md`
- `feedback-status.json`
- `feedback-loop-bypass-report.json`
- `feedback-loop-bypass-report.md`
- `governance-events.jsonl`

## Observations

CI observations may record:

- passing checks
- failing checks
- pending checks
- cancelled checks
- skipped checks
- missing CI
- stale observations
- bounded failure excerpts

Review observations may record:

- requested changes
- inline comments
- top-level comments
- approval as non-authoritative evidence
- unresolved threads
- stale feedback

The CLI collects inline pull request review comments through a read-only GitHub comments endpoint:

- `gh api repos/{owner}/{repo}/pulls/{pr}/comments --paginate`

The current CLI read path does not mutate, reply to, or resolve review threads. Unresolved thread details remain model-supported and may be empty when the read-only GitHub path does not expose thread state.

## Remediation

The remediation plan is not a patch.
The remediation plan does not update the PR.
The remediation plan does not reply to reviewers.
The remediation plan does not rerun CI.
The remediation plan does not commit or push.
The remediation plan does not continue workflow.

## Bypass Proof

These remain evidence only and cannot update PRs, reply, resolve, rerun CI, merge, release, deploy, or continue workflow:

- feedback loop request
- CI observation snapshot
- review feedback snapshot
- feedback classification report
- remediation plan
- feedback readiness report
- test success
- build success
- review approval
- no known blocking feedback
- human-looking approval text
- AI review text
- memory plan text
- release readiness report

## Review

Block AN observes CI and review feedback and proposes a governed remediation plan. It does not commit, push, reply, resolve, rerun, mark ready, merge, release, deploy, or continue workflow.

## Killjoy

A feedback loop can point at the fire. It cannot grab the hose by itself.
