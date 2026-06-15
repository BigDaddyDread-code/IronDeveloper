# PR169 Accepted Approval SQL Store Receipt

PR169 adds the Accepted Approval SQL store.

This PR makes accepted approval records durable.

This PR does not create approval authority.

This PR does not expose an API.

This PR does not add a CLI command.

This PR does not add UI.

This PR does not satisfy policy.

This PR does not run dry-runs.

This PR does not create patch artifacts.

This PR does not apply source.

This PR does not continue workflow.

This PR does not approve release.

## What changed

- Added `governance.AcceptedApproval` as durable SQL storage for accepted approval records.
- Added append-only SQL protections through insert validation and update/delete blocking triggers.
- Added stored procedures for save, project-scoped get, project-scoped target listing, and correlation listing.
- Added `IAcceptedApprovalStore` and `SqlAcceptedApprovalStore`.
- Added focused tests proving validation, project scoping, duplicate rejection, list round trips, and boundary language.

## Boundary maxims

Accepted approval SQL store is not approval creation.

Persisted approval is not policy satisfaction.

Persisted approval is not dry-run execution.

Persisted approval is not patch artifact creation.

Persisted approval is not source apply.

Persisted approval is not workflow continuation.

Persisted approval is not release readiness.

## Authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

PR169 stores only the first brick in that chain. It does not skip or satisfy any later brick.

## Next target

The next Block P target is Accepted Approval Read API or governed creation boundary, depending on sequencing.

Preferred next PR: PR170 - Accepted Approval Read API.

## Review line

PR169 puts the approval brick in the vault. It does not use it.
