# PR175 Policy Satisfaction SQL Store

PR175 adds the Policy Satisfaction SQL store.

This PR makes policy satisfaction records durable.

## What changed

- Added the `governance.PolicySatisfaction` table.
- Added project-scoped stored procedures for save, get, subject listing, accepted-approval listing, and correlation listing.
- Added append-only update/delete blocking.
- Added SQL insert validation for raw/private material and misleading action-authority claims.
- Added `IPolicySatisfactionStore`.
- Added `SqlPolicySatisfactionStore`.
- Added focused SQL store tests and migration/inventory coverage.

## This PR does not

This PR does not create policy satisfaction records from accepted approvals.
This PR does not expose a policy satisfaction API.
This PR does not add CLI.
This PR does not add UI.
This PR does not run dry-runs.
This PR does not create patch artifacts.
This PR does not apply source.
This PR does not execute rollback.
This PR does not continue workflow.
This PR does not approve release.

## Boundary

Policy satisfaction SQL store is not policy satisfaction creation.

Persisted policy satisfaction is not dry-run execution.

Persisted policy satisfaction is not patch artifact creation.

Persisted policy satisfaction is not source apply.

Persisted policy satisfaction is not rollback.

Persisted policy satisfaction is not workflow continuation.

Persisted policy satisfaction is not release readiness.

Persisted policy satisfaction does not authorize execution by itself.

## Store rules

The store persists a supplied, already-shaped `PolicySatisfactionRecord`.

The store validates shape using `PolicySatisfactionValidation.Validate` before persistence.

The store inserts only.

The store does not update, delete, overwrite, or upsert records.

The store does not decide whether policy is satisfied.

The store does not evaluate accepted approval satisfaction.

The store does not authorize dry-run, patch artifact creation, source apply, workflow continuation, rollback, or release readiness.

All read/list paths are project-scoped.

## Authority chain

accepted approval record -> policy satisfaction record -> controlled dry-run -> patch artifact -> controlled source apply -> rollback -> workflow continuation -> release readiness gate

PR175 puts the policy brick in the vault. It does not spend it.

## Next target

The next Block Q target is Policy Satisfaction Read API.

Preferred next PR:

PR176 - Policy Satisfaction Read API
