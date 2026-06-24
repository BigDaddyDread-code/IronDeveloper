# D17 - Operation Status Tenant Isolation Tests

## Purpose

D17 adds hostile tenant/project isolation coverage for the read-only operation status pagination and filtering model.

The operation status tenant isolation tests prove that pagination and filtering over supplied status rows fail closed on cross-tenant or cross-project input and do not leak foreign rows, counts, cursors, filters, redaction metadata, diagnostic statuses, or action authority.

Tenant isolation is not a UI convenience. It is a hard boundary.

## Stack

- Base branch: `status/operation-status-pagination-filtering`
- Head branch: `status/operation-status-tenant-isolation-tests`
- Scope: test-only D17 hostile isolation lane plus this receipt.

## Boundary

D17 is read-only test coverage. It does not add API endpoints, SQL queries, UI behavior, stores, projections, resolver invocation, executors, providers, source mutation, commit, push, PR creation, merge, release, deploy, memory promotion, policy satisfaction, approval, or workflow continuation.

The D17 tenant boundary is fail-closed:

- Mixed tenant or mixed project input is invalid.
- A foreign scoped row is invalid, even if filters or cursors would otherwise select an in-scope row.
- Foreign rows are never silently dropped to create an apparently safe page.
- Counts, cursors, `HasMore`, redaction metadata, diagnostic states, and filter matches do not leak foreign tenant or project presence.
- Listed, filtered, status, cursor, and redacted metadata are not authority.

## Files

- `IronDev.IntegrationTests/BlockD17OperationStatusTenantIsolationTests.cs`
- `Docs/receipts/D17_OPERATION_STATUS_TENANT_ISOLATION_TESTS.md`

## Validation

| Lane | Result |
| --- | --- |
| Focused D17 | Passed: 58/58 |
| Focused D16 | Passed: 74/74 |
| Focused D15 | Passed: 59/59 |
| D01-D17 stacked resolver/read-model lane | Passed: 1288/1288 |
| A02 + A05 read-adapter corridor | Passed: 61/61 |
| Governance/status corridor | Passed: 1468/1468 |
| Build | Passed: 0 errors / 4 warnings |
| `git diff --check` | Passed |
| `git diff --cached --check` | Passed |

## Review Traps

Reject this slice if it:

- drops foreign tenant/project rows silently instead of failing closed;
- exposes foreign row counts, cursors, `HasMore`, redaction metadata, diagnostic states, or filter matches;
- lets a cursor from another tenant or project select rows;
- treats listed status, filter results, or cursor evidence as authority;
- adds API, SQL, UI, store, projection, resolver invocation, executor, provider, or mutation behavior.

## Review Line

Operation status pagination and filtering must fail closed on tenant/project ambiguity. It must not leak foreign rows, counts, cursors, or action authority.

## Killjoy

Tenant isolation is not a UI convenience. It is a hard boundary.
