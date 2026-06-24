# D16 Operation Status Pagination Filtering

## Purpose

D16 adds bounded, deterministic, read-only pagination and filtering for supplied operation status summary rows.

The operation status paginator filters and pages supplied operation status summary rows only. It does not fetch operation status from stores, invoke diagnostic resolvers, approve operations, satisfy policy, choose next safe action, execute mutation, retry, rollback, recover, apply patches, commit, push, create PRs, merge, release, deploy, promote memory, or continue workflow.

## Stack

Stack base while open:

```text
status/rollback-recovery-read-model
```

Suggested title:

```text
core(status): add operation status pagination filtering
```

## Files

```text
IronDev.Core/Governance/OperationStatusPaginationModels.cs
IronDev.Core/Governance/OperationStatusPaginationValidator.cs
IronDev.Core/Governance/OperationStatusPaginator.cs
IronDev.IntegrationTests/BlockD16OperationStatusPaginationFilteringTests.cs
Docs/receipts/D16_OPERATION_STATUS_PAGINATION_FILTERING.md
```

## Boundary

D16 is supplied-row-only. The caller provides tenant/project scope, `AsOfUtc`, page size, sort mode, optional filters, optional cursor, and the summary rows to page.

D16 does not read operation status from a store, write a store, add API endpoints, change OpenAPI, change frontend code, add SQL, run validation, inspect Git, inspect source files, read raw patch/diff/source content, read raw evidence or receipt payloads, or invoke upstream resolvers.

D16 does not invoke D02 lookup, D04 timeline assembly, D05 projection, D07 missing evidence resolution, D08 forbidden action resolution, D09 receipt resolution, D10 evidence resolution, D11 validation staleness, D12 patch/base freshness, D13 worktree/base/head freshness, D14 interrupted-run read model, or D15 rollback/recovery read model.

D16 enforces tenant/project scoping, bounded page sizes, deterministic sorting, deterministic cursor paging, supplied `AsOfUtc`, and redacted-row exclusion unless explicitly requested.

Filtered rows are not approved operations.

Listed rows are not action candidates.

Matching fresh, interrupted, rollback, or recovery diagnostic statuses do not grant source apply, retry, resume, rollback, recovery, commit, push, PR, merge, release, deployment, memory, or continuation authority.

Empty page is not denial.

Full page is not approval queue.

Cursor is not authority.

## Validation

Recorded after implementation:

```text
Focused D16: 74/74 passed
Focused D15: 59/59 passed
D01-D16 stacked resolver/read-model lane: 1230/1230 passed
A02 + A05 read-adapter corridor: 61/61 passed
Governance/status corridor: 1410/1410 passed
Build: 0 errors / 4 warnings
git diff --check: passed
git diff --cached --check: passed
```

## Review Line

Pagination makes status browseable. It does not make any operation actionable.

## Killjoy

A filtered operation list is not an approval queue.
