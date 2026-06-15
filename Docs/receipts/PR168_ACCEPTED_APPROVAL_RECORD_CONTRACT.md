# PR168 - Accepted Approval Record Contract

## Purpose

PR168 adds the Accepted Approval Record contract.

This PR is contract/test/receipt only.

It defines what an accepted approval record is, what it must bind to, and what it does not authorize by itself.

PR168 does not create accepted approval records.

PR168 does not store accepted approval records.

PR168 does not read or write accepted approval records.

PR168 does not expose accepted approval APIs.

PR168 does not satisfy policy.

PR168 does not run dry-runs.

PR168 does not create patch artifacts.

PR168 does not apply source.

PR168 does not continue workflow.

PR168 does not approve release.

## Boundary

Approval package is not accepted approval.

Requested approval is not accepted approval.

Human-looking approval text is not accepted approval.

UI review is not accepted approval.

Accepted approval must be a backend-owned durable authority record.

Accepted approval is necessary but not sufficient for policy satisfaction.

Accepted approval is necessary but not sufficient for source apply.

Accepted approval is necessary but not sufficient for release readiness.

Validation is shape-only.

Validation does not create authority.

## Required binding

Accepted approval must bind to a specific target.

Minimum target binding:

- AcceptedApprovalId
- ProjectId
- ApprovalTargetKind
- ApprovalTargetId
- ApprovalTargetHash
- CapabilityCode
- ApprovalPurpose
- ApprovedByActorId
- AcceptedAtUtc
- ExpiresAtUtc and staleness rule
- CorrelationId
- CausationId
- EvidenceReferences
- BoundaryMaxims

Target hash is required because approval for artifact A must not authorize artifact B.

Do not bind only to free text.

Do not bind only to a package name.

Do not bind only to a workflow id.

## Future target kinds

- patch-artifact
- source-apply-request
- workflow-continuation-request
- release-readiness-decision
- policy-satisfaction-request

These are contract names only in PR168.

## Backend authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

Accepted approval is first in the chain.

Accepted approval does not complete the chain.

## Explicit non-goals

PR168 does not add SQL migration, stores, repositories, controllers, API endpoints, CLI commands, accepted approval create behavior, accepted approval read APIs, policy satisfaction evaluators, dry-run execution, patch artifact creation, source apply, rollback, workflow continuation, release readiness, UI, runtime workers, or schedulers.

## Review line

PR168 defines the approval brick. It does not approve anything.
