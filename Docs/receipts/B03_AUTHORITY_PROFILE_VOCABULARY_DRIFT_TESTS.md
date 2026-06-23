# B03 - Authority Profile Vocabulary Drift Tests

## Summary

B03 locks the post-B01 authority profile vocabulary contract with compile-time and static regression tests.

This PR is test-only.

AuthorityProfileKind is canonical.

RunAuthorityProfileKind must not be reintroduced.

No mapper, bridge, translator, or adapter may hide profile-kind drift.

## Boundary

Profile kind is classification, not authority.

Profile kind is not approval.

Profile kind is not policy satisfaction.

Profile kind is not execution permission.

Profile kind is not source mutation permission.

Profile kind is not workflow continuation.

Profile allowance is not approval, policy satisfaction, execution authority, source mutation authority, or workflow continuation.

Tests guard the contract. They do not implement new behavior.

## Locked Contract

AuthorityProfileKind remains the only canonical authority profile kind enum.

AuthorityProfileKind numeric values remain locked:

- Unknown = 0
- ProposalOnly = 1
- AskBeforeMutation = 2
- BoundedRunAuthority = 3

RunAuthorityProfile.Kind uses AuthorityProfileKind.

RunAuthorityDecision.ProfileKind uses AuthorityProfileKind.

AuthorityProfileStatusRequest.ProfileKind uses AuthorityProfileKind.

ProposalOnly behavior did not widen.

ProposalOnly allowed operations remain exact.

ProposalOnly forbidden operations remain exact.

AskBeforeMutation and BoundedRunAuthority remain known but unsupported for run-profile evaluation.

Unknown and undefined profile kinds fail closed.

## Non-Authority Proof

No executor, mutation, source apply, rollback, commit, push, PR, merge, release, deploy, memory promotion, or workflow continuation path was added.

No production behavior changed.

No production files changed.

No compatibility bridge was added.

No legacy wire support was added.

No approval creation or policy satisfaction path was added.

## Validation

- Focused B03: 11/11 passed
- B01 compatibility: 11/11 passed
- BQ/BS/BT compatibility: 44/44 passed
- Stable governance/status corridor: 1489/1489 passed
- Build: 0 errors / 4 warnings
- git diff --check: passed
- git diff --cached --check: passed

## Review Traps

Reject this PR if:

- It changes production behavior.
- It modifies AuthorityProfileKind.
- It reintroduces RunAuthorityProfileKind.
- It adds a mapper, bridge, translator, or adapter.
- It makes AskBeforeMutation runnable.
- It makes BoundedRunAuthority runnable.
- It changes ProposalOnly allowed or forbidden operations.
- It weakens fail-closed validation.
- It uses authority, approval, grant, execution, or policy satisfaction wording for profile allowance.
- It touches executor or mutation surfaces.

## Killjoy

If the vocabulary drifts, the gate drifts.
