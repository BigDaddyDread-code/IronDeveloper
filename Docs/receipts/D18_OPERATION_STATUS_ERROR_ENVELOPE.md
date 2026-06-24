# D18 - Operation Status Not-Found / Error Envelope Standard

## Purpose

D18 adds a standard read-only envelope for operation status read-model success, not-found, invalid request, ambiguous input, unassessable state, redaction, and safe internal read-model error results.

The operation status not-found/error envelope standard represents read-model failure safely using supplied context and safe metadata only. It does not perform lookup, reveal cross-tenant or cross-project existence, fetch rows, invoke diagnostic resolvers, invoke pagination, approve operations, satisfy policy, choose next safe action, execute mutation, retry, rollback, recover, apply patches, commit, push, create PRs, merge, release, deploy, promote memory, or continue workflow.

## Stack

- Base branch: `status/operation-status-tenant-isolation-tests`
- Head branch: `status/operation-status-error-envelope`
- Scope: D18 envelope models, validator, factory, focused tests, and this receipt.

## Files

- `IronDev.Core/Governance/OperationStatusReadEnvelopeModels.cs`
- `IronDev.Core/Governance/OperationStatusReadEnvelopeValidator.cs`
- `IronDev.Core/Governance/OperationStatusReadEnvelopeFactory.cs`
- `IronDev.IntegrationTests/BlockD18OperationStatusErrorEnvelopeTests.cs`
- `Docs/receipts/D18_OPERATION_STATUS_ERROR_ENVELOPE.md`

## Boundary

D18 is supplied-context-only and supplied-safe-summary-only.

The envelope:

- does not read stores;
- does not write stores;
- does not perform operation lookup;
- does not perform tenant authorization;
- does not assemble timelines;
- does not project status;
- does not invoke missing evidence, forbidden action, receipt, evidence, validation staleness, patch/base freshness, worktree/base/head freshness, interrupted-run, rollback/recovery, or pagination logic;
- does not inspect source, Git, patches, diffs, validation logs, raw evidence payloads, raw receipt payloads, raw timeline payloads, prompts, private reasoning, secrets, tokens, connection strings, raw request bodies, or raw response bodies;
- does not choose next safe actions;
- does not grant approval, policy satisfaction, source apply, rollback, retry, resume, recovery, commit, push, PR creation, merge, release, deployment, memory promotion, or workflow continuation.

## Safety Rules

- not-found-does-not-leak-tenant-existence
- not-found-does-not-leak-project-existence
- envelope-is-not-authority
- not-found-is-not-denial
- invalid-request-is-not-forbidden
- redacted-is-not-denied
- error-is-not-permission
- success-is-not-action-allowed

## Validation

| Lane | Result |
| --- | --- |
| Focused D18 | Passed: 60/60 |
| Focused D17 | Passed: 58/58 |
| Focused D16 | Passed: 74/74 |
| Focused D15 | Passed: 59/59 |
| Focused D14 | Passed: 56/56 |
| Focused D13 | Passed: 128/128 |
| Focused D12 | Passed: 109/109 |
| Focused D11 | Passed: 85/85 |
| Focused D10 | Passed: 120/120 |
| Focused D09 | Passed: 88/88 |
| Focused D08 | Passed: 63/63 |
| Focused D07 | Passed: 81/81 |
| Focused D06 | Passed: 62/62 |
| Focused D05 | Passed: 89/89 |
| Focused D04 | Passed: 67/67 |
| Focused D03 | Passed: 56/56 |
| Focused D02 | Passed: 39/39 |
| Focused D01 | Passed: 54/54 |
| D01-D18 stacked resolver/read-model lane | Passed: 1348/1348 |
| A02 + A05 read-adapter corridor | Passed: 61/61 |
| Governance/status corridor | Passed: 1528/1528 |
| Build | Passed: 0 errors / 4 warnings |
| `git diff --check` | Passed |
| `git diff --cached --check` | Passed |

## Review Traps

Reject D18 if it:

- performs lookup;
- reads from or writes to a store;
- adds API, OpenAPI, frontend, SQL, tenant store, auth, or authorization behavior;
- invokes diagnostic resolvers or the D16 paginator;
- leaks foreign tenant or project existence;
- includes stack traces, exception types, SQL text, file paths, raw payloads, raw request/response bodies, raw patches, raw diffs, raw evidence, raw receipts, raw timelines, prompts, private reasoning, secrets, tokens, or connection strings;
- treats not-found as denial;
- treats invalid request as forbidden;
- treats success as action allowed;
- chooses next safe actions;
- exposes `Can*`, approval, policy, next-action, or authority-grant fields;
- touches executors, source apply, rollback, commit, push, PR, merge, release, deploy, memory, or workflow continuation code.

## Review Line

A safe error envelope explains failure without leaking authority or tenant existence.

## Killjoy

Not found is not denial. Error is not permission. Envelope is not authority.
