# D02 - Operation Lookup By Reference IDs

## Summary

D02 adds read-only operation lookup by scoped external reference IDs.

D01 established that `OperationId` is the canonical backend-minted operation identity and that run IDs, patch IDs, apply IDs, commit package IDs, commit SHAs, push IDs, PR IDs, receipt IDs, evidence IDs, and correlation IDs are references only.

D02 consumes that identity rule. It can find stored canonical operation identity records by tenant, project, reference kind, and reference ID.

## Stack Base

D02 is based on `main` after D01 merged.

## Files Changed

- `IronDev.Core/Governance/OperationIdentityLookupModels.cs`
- `IronDev.Core/Governance/OperationIdentityLookupValidator.cs`
- `IronDev.Core/Governance/OperationIdentityLookupResolver.cs`
- `IronDev.IntegrationTests/BlockD02OperationLookupByReferenceIdTests.cs`
- `Docs/receipts/D02_OPERATION_LOOKUP_BY_REFERENCE_IDS.md`

## Lookup Behavior

Lookup requires:

- tenant scope
- project scope
- known `OperationReferenceKind`
- non-empty reference ID
- safe reference ID text

Lookup searches only records matching the request tenant and project. It then matches by exact reference kind and case-insensitive reference ID. It preserves stored reference casing in returned metadata.

Lookup returns the canonical stored `OperationId` from the matching `OperationIdentityRecord`.

Lookup does not derive the operation ID from the external reference.

Lookup does not mint a missing operation ID.

Lookup does not repair invalid stored identity records.

## Tenant And Project Boundary

Tenant and project scope are required. A lookup without tenant or project scope fails closed.

Same reference IDs in other tenants or projects do not leak into the result.

## No-Minting Boundary

Operation lookup can find canonical operation identity by scoped external references. It does not mint operation IDs, derive operation IDs, select authority, approve work, satisfy policy, validate freshness, execute mutation, create PRs, merge, release, deploy, promote memory, retry, rollback, or continue workflow.

External IDs can find an operation; they must never become the operation.

## Found-Multiple Boundary

Multiple scoped matches return `FoundMultiple`.

`FoundMultiple` is non-authoritative and does not select a winner. Matches are sorted deterministically by:

1. operation creation time
2. operation ID
3. matched reference observed time
4. matched reference source

## No-Authority Boundary

Lookup is read-only.

Lookup is not:

- operation creation
- operation ID minting
- status projection
- timeline projection
- correlation modeling
- evidence resolution
- receipt resolution
- blocked-state explanation
- next-safe-action formatting
- approval
- policy satisfaction
- validation freshness
- source apply
- rollback
- retry permission
- commit
- push
- PR creation
- merge readiness
- release readiness
- deployment readiness
- memory promotion
- workflow continuation

Commit lookup does not imply push.

Push lookup does not imply PR creation.

PR lookup does not imply merge readiness.

Completed lifecycle state does not imply release readiness.

Interrupted lifecycle state does not imply retry authority.

Rolled-back lifecycle state does not imply future rollback authority.

## No API, SQL, UI, Projection, Executor, Or Mutation Boundary

D02 does not add API controllers, endpoints, OpenAPI changes, frontend changes, SQL migrations, SQL stores, event projection, status projection, timeline projection, resolver formatting, executors, runners, source apply, commit, push, PR creation, merge, release, deploy, memory promotion, or workflow continuation.

## Validation

- Focused D02 tests: 39/39 passed
- Focused D01 tests: 54/54 passed
- A02 + A05 read-adapter corridor: 61/61 passed
- Governance/status corridor: 258/258 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Review Traps

Reject D02 if:

- lookup mints operation IDs
- lookup derives operation IDs from run, patch, apply, commit, push, or PR IDs
- lookup searches without tenant scope
- lookup searches without project scope
- lookup leaks matches across tenants
- lookup leaks matches across projects
- lookup silently selects one operation from multiple matches
- lookup treats commit lookup as push evidence
- lookup treats push lookup as PR evidence
- lookup treats PR lookup as merge readiness
- lookup treats completed lifecycle state as release readiness
- lookup treats interrupted lifecycle state as retry authority
- lookup treats rollback lifecycle state as rollback authority
- lookup adds API endpoints
- lookup adds SQL persistence
- lookup adds projection behavior
- lookup adds resolver formatting
- lookup adds UI behavior
- lookup touches executors
- lookup touches release, deploy, memory, or workflow authority code

## Killjoy

Lookup is not identity minting. A PR number that finds an operation still does not authorize merge, release, deploy, or anything else.
