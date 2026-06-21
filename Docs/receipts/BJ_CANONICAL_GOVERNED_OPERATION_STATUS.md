# BJ - Canonical Governed Operation Status

## Purpose

Block BJ adds the canonical governed operation status contract.

It gives operators and future frontend layers one stable way to understand:

- current state
- blocked reasons
- missing evidence
- next safe actions
- forbidden actions
- evidence references
- receipt references

## Boundary

Status is not approval.
Status is not policy satisfaction.
Status is not execution authority.
Status is not workflow continuation.
Status is not memory promotion.
Status is not source mutation.
Status is not a receipt.
Status is not evidence by itself.

NextSafeActions are guidance, not permission.
EvidenceRefs do not satisfy authority unless the relevant evaluator says so.
ReceiptRefs do not become new authority.

BJ does not approve.
BJ does not satisfy policy.
BJ does not execute.
BJ does not mutate source.
BJ does not promote memory.
BJ does not continue workflow.
BJ does not release.
BJ does not deploy.
BJ does not commit.
BJ does not push.
BJ does not merge.
BJ does not create frontend behavior.

## Contract

The canonical status model records:

```text
OperationId
OperationKind
Subject
State
BlockedReasons
MissingEvidence
NextSafeActions
ForbiddenActions
EvidenceRefs
ReceiptRefs
ExpiresAtUtc
ObservedAtUtc
```

The canonical state model supports:

```text
Eligible
Blocked
Running
Completed
Failed
Expired
```

## Validation

The validator enforces:

```text
OperationId is required.
OperationKind is required.
Subject is required.
ObservedAtUtc is required.
State must be explicit.
Blocked status must include a blocked reason.
Blocked status must include missing evidence or a next safe action.
Eligible status must not include blocked reasons.
Completed status must include a receipt reference.
Expired status must include expiry evidence.
Forbidden actions are required for authority-bearing operations.
Status language must not imply approval, policy satisfaction, execution, mutation, workflow continuation, release, deployment, rollback, memory promotion, or frontend authority.
```

## Future Operation Examples

BJ can describe future operation kinds without wiring them:

```text
PatchProposal
SourceApply
Rollback
Commit
Push
DraftPullRequest
Merge
Release
Deployment
MemoryPromotion
WorkflowContinuation
```

These are sample statuses only.
No executor integration is added in BJ.

## Review Line

Governance is not usable until every blocked state can explain the next safe action.

## Killjoy

A status can explain the locked door. It cannot unlock it.
