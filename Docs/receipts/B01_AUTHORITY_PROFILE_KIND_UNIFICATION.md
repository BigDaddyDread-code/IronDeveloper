# B01 — Authority Profile Kind Unification

## Summary

B01 removes the duplicated authority-profile enum vocabulary.

AuthorityProfileKind is canonical.

RunAuthorityProfileKind was removed.

Run authority profiles, run authority decisions, validators, evaluators, status mapping, tests, and receipts now speak the same profile-kind language.

## Boundary

This is cleanup, not new authority.

A profile kind is classification, not authority.

A profile kind is not approval.

A profile kind is not policy satisfaction.

A profile kind is not execution permission.

A profile kind is not source mutation permission.

A profile kind is not workflow continuation.

One authority vocabulary, one interpretation path.

## Behavior Preserved

ProposalOnly behavior did not widen.

ProposalOnly allowed operations remain proposal-safe only.

ProposalOnly forbidden operations remain blocked.

ProposalOnly dangerous flags remain rejected.

ProposalOnly safe flags remain required.

Unknown still fails closed.

AskBeforeMutation and BoundedRunAuthority did not become runnable run profiles in this PR.

Known enum values outside the existing ProposalOnly run-profile behavior fail closed until a dedicated behavior PR adds explicit validator/evaluator support.

## Numeric Stability

AuthorityProfileKind numeric values remain stable:

- Unknown = 0
- ProposalOnly = 1
- AskBeforeMutation = 2
- BoundedRunAuthority = 3

## Non-Authority Proof

This PR adds no executor, mutation, source apply, rollback, commit, push, PR, merge, release, deploy, memory promotion, or workflow continuation path.

It does not add approval creation.

It does not satisfy policy.

It does not add a mapper between RunAuthorityProfileKind and AuthorityProfileKind.

It does not add support for AskBeforeMutation or BoundedRunAuthority run-profile evaluation.

It does not rename IsAllowedByProfile into approval, authorization, execution, or grant language.

## Validation

- Focused B01: 11/11 passed
- Focused BQ/BS/BT compatibility: 55/55 passed
- Stable governance/status corridor: 1478/1478 passed
- Build: 0 errors / 4 warnings
- git diff --check: passed
- git diff --cached --check: passed

## Review Traps

Reject this PR if:

- RunAuthorityProfileKind remains alive as a second enum.
- A compatibility mapper translates between RunAuthorityProfileKind and AuthorityProfileKind.
- AskBeforeMutation becomes a runnable run profile.
- BoundedRunAuthority becomes a runnable run profile.
- ProposalOnly allowed operations widen.
- ProposalOnly forbidden operations narrow.
- Dangerous ProposalOnly flags become true.
- Profile-kind wording becomes approval, authorization, execution, grant, or policy satisfaction language.
- Executor or mutation surfaces are touched.

## Killjoy

Two enum names for authority is how same meaning becomes different gate.
