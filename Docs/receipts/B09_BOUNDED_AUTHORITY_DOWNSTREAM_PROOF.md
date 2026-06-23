# B09 - Bounded Authority Downstream Proof

## Summary

BoundedRunAuthority does not imply downstream authority.

BoundedRunAuthority may name bounded lanes through draft PR creation, but each lane remains separately bounded, separately evidenced, and separately eligible.

A source apply decision is not commit authority.

A commit decision is not push authority.

A push decision is not draft PR authority.

A draft PR decision is not ready-for-review authority.

The proof is test and receipt only.

## Boundary

Ready-for-review remains outside BoundedRunAuthority.

Merge, release, deployment, memory promotion, and workflow continuation remain outside BoundedRunAuthority.

Eligibility operation must match the requested operation.

Evidence refs are not downstream authority.

Receipt refs are not downstream authority.

Fresh expiry is not downstream authority.

Eligible status is not execution.

Executor re-check remains separate.

No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, or generated client path was added.

## Hostile Inputs

B09 intentionally proves BoundedRunAuthority blocking survives:

- bounded-run-authority-grant refs
- operation-eligibility-decision refs
- accepted apply approval refs
- accepted source apply request refs
- source apply authority refs
- source apply receipt refs
- rollback authority refs
- rollback receipt refs
- commit authority refs
- commit package refs
- commit-created refs
- push authority refs
- push receipt refs
- remote branch updated refs
- draft PR authority refs
- draft PR created refs
- pull request URL refs
- ready-for-review refs
- merge authority refs
- merge receipt refs
- release authority refs
- release candidate refs
- deployment authority refs
- memory promotion refs
- workflow continuation refs
- approval request refs
- policy satisfaction refs
- provider mutation refs
- package publication refs
- validation result refs
- patch package refs
- worktree diff refs
- hostile receipt refs
- fresh grant expiry
- hostile subject text

## Required Result

For every operation in RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations:

- State is Blocked.
- BlockedReasons contains BoundedRunAuthorityOperationBlocked:{operation}.
- ForbiddenActions contains do not perform {operation} under BoundedRunAuthority.
- ForbiddenActions contains do not treat bounded profile allowance as later-stage authority.

For bounded mutation lanes:

- Matching operation eligibility can become Eligible.
- Eligible status remains non-executing.
- Bounded grant evidence ref is required.
- Operation eligibility decision evidence ref is required.
- Eligibility decisions cannot be reused from another operation.

For downstream evidence:

- Source apply evidence cannot authorize commit, push, draft PR, ready-for-review, merge, release, deployment, or workflow continuation.
- Commit evidence cannot authorize push, draft PR, ready-for-review, merge, release, deployment, or workflow continuation.
- Push evidence cannot authorize draft PR, ready-for-review, merge, release, deployment, or workflow continuation.
- Draft PR evidence cannot authorize ready-for-review, merge, release, deployment, memory promotion, or workflow continuation.
- Expired status cannot be refreshed by downstream-looking refs.

## Validation

- Focused B09: 14/14 passed.
- B08 compatibility: 15/15 passed.
- B07 compatibility: 10/10 passed.
- B06 compatibility: 14/14 passed.
- B05 compatibility: 16/16 passed.
- B04 compatibility: 12/12 passed.
- B03 compatibility: 12/12 passed.
- B01 compatibility: 11/11 passed.
- BQ/BR/BS/BT/BU compatibility: 80/80 passed.
- Stable governance/status corridor: 1571/1571 passed.
- Build: 0 errors / 4 warnings.
- git diff --check: passed.
- git diff --cached --check: passed.

## Review Traps

Reject this PR if:

- it changes production code
- it modifies AuthorityProfileStatusMapper
- it changes BoundedRunAuthority operation sets
- it weakens B06 canonical boundary behavior
- it treats source apply as commit authority
- it treats commit as push authority
- it treats push as PR authority
- it treats draft PR as ready-for-review authority
- it treats ready-for-review as inside BoundedRunAuthority
- it lets eligibility override BoundedRunAuthority forbidden operations
- it lets status text or receipt refs become downstream authority
- it adds executor or mutation code
- it breaks matching-operation eligibility behavior for bounded lanes

## Killjoy

A bounded lane ends where the next authority boundary begins.
