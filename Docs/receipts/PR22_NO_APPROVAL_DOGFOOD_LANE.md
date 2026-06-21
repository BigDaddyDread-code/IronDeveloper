# PR22 No-Approval Dogfood Lane

## Purpose

PR22 adds an end-to-end no-approval dogfood lane for ProposalOnly mode.

The lane proves IronDev can produce reviewable engineering evidence without approval, source mutation, rollback, commit, push, pull request creation, release, deployment, memory promotion, or workflow continuation.

No-approval mode must be useful enough that users do not need to bypass it.

Useful evidence is not mutation permission.

## Dogfood Task

The task used by the focused lane is an IronDev-style governance documentation task:

- tighten `Docs/receipts/PR22_NO_APPROVAL_DOGFOOD_LANE.md`
- produce a reviewable patch proposal
- record canonical status
- package validation evidence honestly
- produce a review summary for a human reviewer
- prove durable source was unchanged

The task text lives at `Docs/dogfood/PR22_NO_APPROVAL_PROPOSAL_ONLY_LANE.md`.

## Artifacts Produced

The lane composes existing ProposalOnly package builders.

It produces a disposable workspace patch package containing:

- `patch.diff`
- `review-summary.md`
- `known-risks.md`
- `validation-summary.md`
- `patch-package-manifest.json`
- `operation-status.json`

It also produces a validation result package containing:

- `validation-summary.md`
- `validation-evidence.md`
- `validation-result-package-manifest.json`
- `operation-status.json`
- copied validation evidence files

The review summary path is emitted by the patch package as `review-summary.md`.

## Validation Result

The dogfood lane records validation as inconclusive when full validation has not run.

It does not pretend validation passed.

Validation result package is evidence only.

Validation passed is not approval.

Validation passed is not policy satisfaction.

Validation passed is not source apply authority.

Validation inconclusive is not workflow continuation authority.

## Status Result

The lane produces canonical governed operation status through the existing patch proposal and validation result status paths.

The patch package status is allowed to explain evidence and missing validation.

Status is not authority.

NextSafeActions are guidance only.

Patch package is not source apply.

Review summary is not approval.

No-approval mode is not hidden approval.

Dogfood success is not merge readiness.

Dogfood success is not release readiness.

Useful output is not mutation permission.

## No-Mutation Proof

The focused tests capture durable source state before running the dogfood lane, run the lane against a disposable workspace outside the durable source root, then capture durable source state again.

Durable source was unchanged.

No approval request, accepted approval, policy satisfaction, source apply, rollback, commit, push, PR creation, memory promotion, release, deployment, or workflow continuation occurred.

The lane adds no executor, provider gateway, direct git execution, source mutation, rollback execution, commit execution, push execution, pull request creation, merge, release, deployment, memory promotion, workflow continuation, frontend behavior, or approval/policy acceptance.

## Boundary

ProposalOnly dogfood may:

- inspect task intent
- create or use disposable workspace state
- produce patch package evidence
- produce validation result evidence
- produce canonical operation status
- produce review summary evidence
- prove no durable source mutation

ProposalOnly dogfood must not:

- request approval
- accept approval
- satisfy policy
- dry-run source apply
- apply source
- rollback source
- commit
- push
- create or update pull requests
- mark ready for review
- merge
- release
- deploy
- promote memory
- continue workflow
- call providers
- mutate durable repo state

## Validation

- Focused PR22: 30/30.
- PR21 focused lane: 44/44.
- PR20 focused lane: 31/31.
- CA focused lane: 16/16.
- BJ through PR22 authority corridor: 526/526.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.
- `git diff --check HEAD~1 HEAD`: passed.

## Killjoy

Useful evidence is not mutation permission.
