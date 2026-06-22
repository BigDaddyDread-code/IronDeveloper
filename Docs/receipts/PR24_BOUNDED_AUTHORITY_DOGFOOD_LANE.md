# PR24 Bounded Authority Dogfood Lane

## Purpose

PR24 adds an end-to-end bounded-authority dogfood lane.

The lane proves IronDev can move through proposal, validation reporting, freshness checking, controlled source apply, commit packaging, controlled commit, controlled push, and controlled draft PR creation under scoped fixture authority only.

Review line:

> Bounded authority should make the common path fast without making the dangerous path possible.

Killjoy:

> A scoped key opens one door, not the building.

## Dogfood Task

The dogfood task lives at `Docs/dogfood/PR24_BOUNDED_AUTHORITY_DRAFT_PR_LANE.md`.

It proposes a narrow governance receipt clarification, packages the patch, reports validation, checks freshness, applies inside a controlled fixture, commits through a fake controlled commit gateway, pushes through a fake controlled push gateway, creates a draft PR through a fake controlled draft PR gateway, and then stops.

## Bounded Authority Summary

The lane uses a test-local scoped bounded-authority grant for the dogfood fixture.

The grant is bound to:

- one repo: `BigDaddyDread-code/IronDeveloper`
- one branch: `dogfood/bounded-authority-draft-pr-lane`
- one run id: `run-pr24`
- one patch hash
- one file scope: `Docs/receipts/PR24_BOUNDED_AUTHORITY_DOGFOOD_LANE.md`

Allowed operation kinds:

- `SourceApply`
- `Commit`
- `Push`
- `DraftPullRequest`

Stop-before list:

- `ReadyForReview`
- `Merge`
- `Release`
- `Deployment`
- `MemoryPromotion`
- `WorkflowContinuation`

This PR does not create a production grant issuer and does not add global authority.

## Artifact Summary

Patch package:

- `patch.diff`
- `review-summary.md`
- `known-risks.md`
- `validation-summary.md`
- `patch-package-manifest.json`
- `operation-status.json`

Validation summary:

- validation is explicit evidence
- validation is not approval
- validation is not policy satisfaction

Freshness summary:

- repo state freshness is checked before mutation
- freshness is not authority

Receipt summary:

- source apply receipt is bound to repo, branch, run, patch, and file scope
- commit package consumes source apply receipt and scoped commit authority
- commit receipt is not push authority
- push receipt is not PR authority
- draft PR receipt is not ready-for-review authority
- draft PR receipt is not merge readiness
- draft PR receipt is not release readiness

## Stop Proof

The lane proves the following are blocked:

- ready-for-review
- merge
- release
- deployment
- memory promotion
- workflow continuation

The lane also proves:

- no real provider is called
- the real IronDev repository is not mutated
- hostile task, validation, status, PR body, memory, and UI text cannot expand authority

## Validation

- Focused PR24: 44/44 passed.
- PR23 focused lane: 41/41 passed.
- PR22 focused lane: 30/30 passed.
- PR21 focused lane: 44/44 passed.
- PR20 focused lane: 31/31 passed.
- CA focused lane: 16/16 passed.
- BJ through PR24 authority corridor: 611/611 passed.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.
- `git diff --check HEAD~1 HEAD`: passed.
