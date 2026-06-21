# PR23 Ask-Before-Mutation Dogfood Lane

## Purpose

PR23 adds an end-to-end AskBeforeMutation dogfood lane.

The lane proves IronDev can produce useful proposal evidence, report validation state, confirm freshness evidence, and then stop clearly at the source mutation boundary.

Stopping is acceptable only if the next safe action is obvious.

A locked gate still needs a sign.

## Dogfood Task

The dogfood task is a small IronDev governance documentation task:

- propose a receipt clarification for `Docs/receipts/PR23_ASK_BEFORE_MUTATION_DOGFOOD_LANE.md`
- package the proposed patch for review
- package validation evidence honestly
- evaluate fresh repo-state evidence
- block source apply due to missing explicit source-apply authority
- show next safe action and forbidden actions

The task note lives at `Docs/dogfood/PR23_ASK_BEFORE_MUTATION_BOUNDARY_LANE.md`.

## Produced Artifacts

The lane composes existing backend pieces and produces:

- patch package
- canonical patch proposal status
- validation result package
- review summary
- repo freshness result
- blocked canonical `SourceApply` status
- next safe action
- forbidden actions
- durable source no-mutation proof

The patch package contains:

- `patch.diff`
- `review-summary.md`
- `known-risks.md`
- `validation-summary.md`
- `patch-package-manifest.json`
- `operation-status.json`

## Validation Result

The lane reports validation evidence honestly.

When full validation has not run, the result is inconclusive.

Tests reported is not approval.

Validation passed is not policy satisfaction.

Validation result is not source apply authority.

## Freshness Result

The lane includes repo freshness evidence.

Freshness may show the current state is not stale.

Fresh repo state is not source apply authority.

This lane proves the second case:

- patch package exists
- validation is reported
- freshness is acceptable
- source apply is still blocked because explicit source-apply authority is absent

## Source Apply Stop

The lane produces a blocked `SourceApply` status.

The blocked reason is explicit:

- `AskBeforeMutationRequiresSourceApplyApproval`
- `MissingExplicitSourceApplyAuthority`
- `NoBoundedAuthorityGrantForSourceApply`

The missing authority is explicit:

- `accepted-source-apply-request`
- `bounded-authority-grant:SourceApply`
- `policy-satisfaction`
- `dry-run`

The next safe action is:

> Create or request an explicit governed source-apply authority decision for repo `BigDaddyDread-code/IronDeveloper`, branch `dogfood/ask-before-mutation-boundary-lane`, run `run-pr23`, patch hash `<patchHash>`, and file scope `Docs/receipts/PR23_ASK_BEFORE_MUTATION_DOGFOOD_LANE.md`.

The next safe action is guidance only.

## Forbidden Actions

The stop status forbids:

- do not apply source without explicit source-apply authority
- do not treat patch package as source apply authority
- do not treat validation as approval
- do not treat freshness as authority
- do not commit
- do not push
- do not create PR
- do not continue workflow
- do not promote memory

## No-Mutation Proof

The focused tests snapshot durable source before running the dogfood lane, run the lane using a disposable workspace outside the durable source root, then snapshot durable source after the lane completes.

Durable source remains unchanged.

The lane does not accept approval, satisfy policy, dry-run source apply, apply source, rollback, commit, push, create pull requests, merge, release, deploy, promote memory, continue workflow, mutate providers, or mutate durable source.

## Validation

- Focused PR23: passed 41/41.
- PR22 focused lane: passed 30/30.
- PR21 focused lane: passed 44/44.
- PR20 focused lane: passed 31/31.
- CA focused lane: passed 16/16.
- BJ through PR23 authority corridor: passed 567/567.
- Build: passed with 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.
- `git diff --check HEAD~1 HEAD`: passed.

## Killjoy

A locked gate still needs a sign.
