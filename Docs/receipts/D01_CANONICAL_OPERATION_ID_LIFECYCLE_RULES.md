# D01 - Canonical Operation ID Lifecycle Rules

## Summary

D01 defines the canonical lifecycle rules for `OperationId`.

Every governed operation has one durable backend-minted root identifier. Related run IDs, patch IDs, apply IDs, commit IDs, push IDs, PR IDs, receipt IDs, evidence IDs, and correlation IDs are references only.

Operation identity is scoped by tenant and project. Tenant and project scope are required identity fields, not optional read-model hints.

## Boundary

Operation identity is a durable reference spine. It does not grant authority, approval, policy satisfaction, validation freshness, source apply, rollback, commit, push, PR creation, merge readiness, release readiness, deployment readiness, memory promotion, or workflow continuation.

D01 does not add operation lookup, timeline projection, status projection, event projection, pagination, API endpoints, SQL persistence, UI behavior, or mutation execution.

## Canonical Operation ID Rules

A canonical `OperationId` is:

- required
- non-empty
- stable
- opaque
- backend-minted
- preserved once assigned
- scoped to one tenant
- scoped to one project
- not derived from UI text
- not derived from memory
- not derived from model output
- not derived from receipt content
- not derived from evidence payload text
- not derived from run-report interpretation
- not re-minted by status, timeline, or read-model layers

D01 accepts backend-shaped IDs using the `op_` prefix followed by lowercase hex or a lowercase GUID without braces. Human prose, blank values, whitespace, control characters, run IDs, patch IDs, apply IDs, commit package IDs, commit SHAs, push IDs, PR IDs, receipt IDs, evidence IDs, and correlation IDs fail validation as canonical operation IDs.

Tenant ID and project ID are required. Missing tenant or project scope fails validation so durable operation identity cannot become cross-tenant or cross-project ambiguous.

## Reference ID Rules

Run IDs, patch IDs, apply IDs, commit IDs, push IDs, PR IDs, receipt IDs, evidence IDs, and correlation IDs may reference an operation. They must not replace the canonical OperationId.

Operation references record:

- reference kind
- reference id
- observed time
- source

References cannot override the canonical operation ID. Duplicate identical references are rejected. Duplicate reference kinds are allowed only when the reference IDs are distinct and orderable by observed time.

A linked reference proves only that the operation has a related identifier. It does not prove the referenced action is valid, authorized, fresh, complete, or safe.

## Lifecycle Transition Rules

D01 defines advisory lifecycle transition validation only. It does not create operations and does not transition stored records.

Allowed identity lifecycle transitions:

- `Unknown -> Minted`
- `Minted -> LinkedToRun`
- `LinkedToRun -> LinkedToPatch`
- `LinkedToPatch -> LinkedToApply`
- `LinkedToApply -> LinkedToCommit`
- `LinkedToCommit -> LinkedToPush`
- `LinkedToPush -> LinkedToPullRequest`
- any active linked state to `Failed`
- any active linked state to `Interrupted`
- any active linked state to `RolledBack`
- any active linked state to `Completed`

Impossible transitions fail validation.

## Authority Separation

Lifecycle states are identity visibility states, not authority states.

- `LinkedToCommit` does not imply push authority.
- `LinkedToPush` does not imply PR creation authority.
- `LinkedToPullRequest` does not imply merge readiness.
- `Completed` does not imply release readiness.
- `Interrupted` does not imply retry authority.
- `RolledBack` does not imply future rollback authority or rollback execution.

Transition validation is advisory contract only. It does not execute source mutation, rollback, commit, push, PR creation, merge, release, deploy, memory promotion, workflow continuation, approval, or policy satisfaction.

## Existing Read Model Compatibility

Existing operation status and operation timeline read repositories remain read-only.

D01 does not make them operation ID creators. They may read by operation ID, but they must not mint operation IDs, derive operation IDs, or replace missing operation IDs with generated values.

Frontend readiness sources remain read-only and must not mint operation IDs.

## Validation

- Focused D01 tests: 54/54 passed.
- A02 operation status read adapter tests and A05 operation timeline read adapter tests: 61/61 passed.
- Frontend readiness/read-only practical corridor: 660/660 passed for A01-A07 and A10-A12.
- Full A01-A12 frontend readiness corridor was attempted: 794/797 passed; three A08/A09 assertions now report `Expired` where the older fixture expected `Available` or `Current` on 2026-06-24. D01 does not modify those time-sensitive frontend-readiness fixtures.
- Governance/status corridor: 211/211 passed for BJ/BK/BL/BT/BZ plus D01.
- Build: `dotnet build IronDev.slnx --no-restore -v:minimal` passed with 0 errors and 4 existing warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Review Traps

Reject D01 if:

- operation ID is derived from run ID
- operation ID is derived from patch ID
- operation ID is derived from commit SHA
- operation ID is derived from PR number or URL
- operation ID is derived from receipt, evidence, or correlation ID
- missing operation ID is silently generated by read adapters
- read repositories mint operation IDs
- frontend readiness source mints operation IDs
- lifecycle state implies authority
- linked-to-commit implies push
- linked-to-push implies PR creation
- linked-to-PR implies merge readiness
- completed implies release readiness
- interrupted implies retry permission
- rollback state implies rollback execution authority
- D01 adds lookup behavior
- D01 adds projection behavior
- D01 adds resolver behavior
- D01 adds API endpoints
- D01 touches UI
- D01 touches SQL
- D01 touches executors
- D01 touches release/deploy/memory/governance authority code

## Killjoy

If operation identity can be re-derived differently by each layer, the UI will eventually lie.
