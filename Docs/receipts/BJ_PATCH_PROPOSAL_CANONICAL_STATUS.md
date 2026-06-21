# BJ - Patch Proposal Canonical Status Mapping

## Purpose

This slice maps patch proposal outcomes into canonical GovernedOperationStatus.

It is a status/read-model mapping only.

## Boundary

Patch proposal status can explain:

- proposal completion
- blocked reasons
- missing evidence
- failed proposal state
- expired or stale proposal state
- next safe action
- forbidden actions

Patch proposal status cannot approve.
Patch proposal status cannot satisfy policy.
Patch proposal status cannot execute.
Patch proposal status cannot mutate source.
Patch proposal status cannot commit.
Patch proposal status cannot push.
Patch proposal status cannot create PRs.
Patch proposal status cannot merge.
Patch proposal status cannot release.
Patch proposal status cannot deploy.
Patch proposal status cannot promote memory.
Patch proposal status cannot continue workflow.

A completed patch proposal may recommend requesting controlled source apply.
A completed patch proposal is not controlled source apply authority.

Patch hash is evidence, not permission.
Validation success is evidence, not approval.
Review summary is evidence, not approval.
Known risks are evidence, not approval.
NextSafeActions are guidance, not permission.

## Mapping

```text
ReadyForReview -> Completed
Blocked        -> Blocked
Failed         -> Failed
Expired        -> Expired
```

PatchProposal never maps to SourceApply eligibility.

## Validation

The mapper writes OperationKind = PatchProposal and validates every mapped status through GovernedOperationStatusValidator.

ReadyForReview statuses include a status artifact reference so the canonical completed-status rule has a receipt-like reference without inventing mutation authority.

Unsafe status text is red-flagged when it implies that patch proposal evidence, validation, review summaries, known risks, memory, UI state, or old proposals approve or authorize later operations.

## Review Line

A patch proposal is evidence, not permission.

## Killjoy

A patch proposal can point at the door. It cannot open it.
