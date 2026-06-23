# B08 - AskBeforeMutation Boundary Proof

## Summary

AskBeforeMutation stops at each authority boundary.

AskBeforeMutation forbidden operations come from RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations.

This proof covers every AskBeforeMutation-forbidden operation.

The proof uses hostile evidence refs, hostile receipt refs, accepted apply approval refs, bounded grant refs, fresh expiry, hostile subject text, and eligible operation decisions.

The proof is test and receipt only.

## Boundary

Accepted apply approval cannot authorize later lanes.

Bounded grant refs cannot widen AskBeforeMutation.

Approval and policy refs cannot widen AskBeforeMutation.

Source apply receipt is not accepted apply approval.

Accepted apply approval without eligibility still blocks.

Eligibility mismatch still blocks.

Eligible status is not execution.

Expired status cannot be refreshed by accepted approval.

Receipt refs are not authority.

Status text is not authority.

Proposal-safe operations still use normal eligibility mapping.

Profile boundary wins before accepted apply approval and eligibility.

No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, or generated client path was added.

## Hostile Inputs

B08 intentionally proves AskBeforeMutation blocking survives:

- eligible operation decisions
- bounded-run-authority-grant refs
- operation-eligibility-decision refs
- accepted apply approval refs
- accepted source apply request refs
- source apply authority refs
- source apply receipt refs
- rollback authority refs
- commit authority refs
- push authority refs
- draft PR authority refs
- ready-for-review authority refs
- merge authority refs
- release authority refs
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

For every operation in RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations:

- State is Blocked.
- BlockedReasons contains AskBeforeMutationOperationBlocked:{operation}.
- ForbiddenActions contains do not perform {operation} under AskBeforeMutation.
- ForbiddenActions contains do not treat accepted apply approval as authority for later mutation lanes.
- Status does not become Eligible.
- Accepted apply approval does not reach later lanes.
- Bounded grant refs do not widen AskBeforeMutation.
- Receipt refs do not become authority.

For SourceApply and DurableSourceMutation:

- Missing accepted apply approval blocks before eligibility.
- Source apply receipt cannot substitute accepted apply approval.
- Accepted apply approval without eligibility still blocks.
- Eligibility operation mismatch still blocks.
- Eligible status remains non-executing.
- Expired status cannot be refreshed by accepted approval.

## Validation

- Focused B08: 15/15 passed.
- B07 compatibility: 10/10 passed.
- B06 compatibility: 14/14 passed.
- B05 compatibility: 16/16 passed.
- B04 compatibility: 12/12 passed.
- B03 compatibility: 12/12 passed.
- B01 compatibility: 11/11 passed.
- BQ/BR/BS/BT/BU compatibility: 80/80 passed.
- Stable governance/status corridor: 1557/1557 passed.
- Build: 0 errors / 4 warnings.
- git diff --check: passed.
- git diff --cached --check: passed.

## Review Traps

Reject this PR if:

- it changes production code
- it modifies AuthorityProfileStatusMapper
- it changes AskBeforeMutation operation sets
- it weakens B06 canonical boundary behavior
- it treats accepted apply approval as later-lane authority
- it treats bounded grant refs as widening AskBeforeMutation
- it treats source apply receipt as accepted apply approval
- it lets eligibility override AskBeforeMutation forbidden operations
- it lets status text or receipt refs become authority
- it adds executor or mutation code
- it breaks proposal-safe eligibility behavior

## Killjoy

AskBeforeMutation asks for one guarded door. It does not open the hallway.
