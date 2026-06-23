# B06 - Authority Profile Status Canonical Model

## Summary

AuthorityProfileStatusMapper reflects the canonical profile model.

RunAuthorityProfileValidator remains the source of truth for profile operation ceilings.

Status mapping may explain profile/operation status.

Status mapping must not own a second authority profile operation model.

## Boundary

Status is not authority.

Profile kind is not authority.

Profile allowance is not approval.

Profile allowance is not policy satisfaction.

Profile allowance is not execution permission.

Eligibility is not execution.

Eligible status is not execution.

Evidence refs are not proof by themselves.

Receipt refs are not authority.

Profile-forbidden operations block before eligibility.

Unknown operations fail closed.

Future unmapped operations must fail closed.

AskBeforeMutation accepted apply approval cannot authorize later lanes.

BoundedRunAuthority requires bounded grant evidence refs for bounded lanes.

BoundedRunAuthority also requires visible operation eligibility evidence refs for bounded lanes.

No executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, or generated client path was added.

## Canonical Source

The mapper resolves profile boundaries from:

- RunAuthorityProfileValidator.ProposalOnlyAllowedOperations
- RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations
- RunAuthorityProfileValidator.AskBeforeMutationAllowedOperations
- RunAuthorityProfileValidator.AskBeforeMutationForbiddenOperations
- RunAuthorityProfileValidator.BoundedRunAuthorityAllowedOperations
- RunAuthorityProfileValidator.BoundedRunAuthorityForbiddenOperations

The mapper does not create a public mapper, bridge, translator, adapter, legacy enum, or alternate profile-kind vocabulary.

## Required Order

The mapper fails closed in this order:

1. Missing request.
2. Unknown or undefined profile kind.
3. Unknown or undefined operation kind.
4. Expired grant/status window.
5. Missing canonical profile boundary.
6. Profile-forbidden operation.
7. Operation missing from the canonical profile boundary.
8. Profile-specific evidence gates.
9. Missing eligibility decision.
10. Eligibility operation mismatch.
11. Eligibility blocked or missing evidence.
12. Eligible status with non-execution wording.

## Validation

- Focused B06: 14/14 passed.
- B05 compatibility: 16/16 passed.
- B04/B03/B01 compatibility: 35/35 passed.
- BQ/BR/BS/BT/BU compatibility: 80/80 passed.
- Stable governance/status corridor: 1532/1532 passed.
- Build: 0 errors / 4 warnings.
- git diff --check: passed with normal LF/CRLF warnings.
- git diff --cached --check: passed.

## Review Traps

Reject this PR if:

- status owns a second profile operation model
- profile ceilings are hard-coded in the status mapper instead of read from RunAuthorityProfileValidator
- eligibility overrides profile-forbidden operations
- unknown operations flow into eligibility
- future unmapped operations can become eligible
- accepted apply approval authorizes later lanes
- BoundedRunAuthority produces eligible bounded-lane status without bounded grant evidence refs
- BoundedRunAuthority produces eligible bounded-lane status without operation eligibility evidence refs
- Eligible status wording becomes execution wording
- any executor, mutation, approval, policy, UI, API, CLI, SQL, durable store, or generated client path is added

## Killjoy

Status may explain the gate. It must not become the gate.
