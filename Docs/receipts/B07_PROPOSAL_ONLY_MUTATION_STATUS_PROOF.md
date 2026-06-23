# B07 - ProposalOnly Mutation Status Proof

## Summary

ProposalOnly plus mutation always maps Blocked.

ProposalOnly forbidden operations come from RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations.

This proof covers every ProposalOnly-forbidden operation.

The proof uses hostile evidence refs, hostile receipt refs, fresh expiry, and eligible operation decisions.

The proof is test and receipt only.

## Boundary

Eligibility cannot override ProposalOnly.

Accepted apply approval cannot override ProposalOnly.

Bounded grant refs cannot override ProposalOnly.

Approval and policy refs cannot override ProposalOnly.

Receipt refs are not authority.

Fresh expiry is not authority.

Status text is not authority.

ProposalOnly allowed operations still use normal eligibility mapping.

Profile boundary wins before eligibility.

Status remains explanation, not permission.

No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, or generated client path was added.

## Hostile Inputs

B07 intentionally proves ProposalOnly blocking survives:

- eligible operation decisions
- bounded-run-authority-grant refs
- operation-eligibility-decision refs
- accepted apply approval refs
- accepted source apply request refs
- source apply authority refs
- source apply receipt refs
- commit authority refs
- patch package refs
- validation result refs
- worktree diff refs
- human approval refs
- policy satisfaction refs
- hostile receipt refs
- fresh grant expiry
- hostile subject text

## Required Result

For every operation in RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations:

- State is Blocked.
- BlockedReasons contains ProposalOnlyDoesNotAllowDurableMutation.
- BlockedReasons contains ProposalOnlyOperationBlocked:{operation}.
- MissingEvidence contains bounded-run-authority-grant.
- MissingEvidence contains accepted-source-apply-authority.
- ForbiddenActions contains do not apply source under ProposalOnly.
- ForbiddenActions contains do not commit under ProposalOnly.
- ForbiddenActions contains do not push under ProposalOnly.
- ForbiddenActions contains do not continue workflow from ProposalOnly status.

## Validation

- Focused B07: 10/10 passed.
- B06 compatibility: 14/14 passed.
- B05 compatibility: 16/16 passed.
- B04/B03/B01 compatibility: 35/35 passed.
- BQ/BR/BS/BT/BU compatibility: 80/80 passed.
- Stable governance/status corridor: 1542/1542 passed.
- Build: 0 errors / 4 warnings.
- git diff --check: passed.
- git diff --cached --check: passed.

## Review Traps

Reject this PR if:

- it changes production code
- it modifies AuthorityProfileStatusMapper
- it changes ProposalOnly operation sets
- it weakens B06 canonical boundary behavior
- it adds executor or mutation code
- it treats evidence refs as authority
- it treats receipt refs as authority
- it treats accepted apply approval as valid under ProposalOnly
- it treats bounded grant refs as valid under ProposalOnly
- it lets eligibility override ProposalOnly
- it breaks ProposalOnly allowed operation eligibility behavior

## Killjoy

ProposalOnly means proposal only, even when every receipt begs otherwise.
