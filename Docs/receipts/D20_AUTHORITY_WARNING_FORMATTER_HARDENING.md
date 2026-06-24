# D20 - Authority-Warning Formatter Hardening

## Purpose

D20 adds a deterministic backend formatter for operation-status authority warnings. It consumes only supplied safe read envelopes, D19 formatter results, and supplied warning facts, then renders bounded display-only lines explaining what the supplied facts do not authorize.

The authority-warning formatter emits display-only authority boundary warnings from supplied safe facts. It does not approve operations, deny operations, satisfy policy, choose workflow steps, grant authority, execute mutation, retry, rollback, recover, apply patches, commit, push, create PRs, merge, release, deploy, promote memory, or continue workflow.

## Stack

- Base branch: `status/next-safe-action-formatter-hardening`
- Head branch: `status/authority-warning-formatter-hardening`
- Scope: D20 authority-warning formatter models, validator, formatter, focused tests, and this receipt.

## Files

- `IronDev.Core/Governance/OperationStatusAuthorityWarningFormatterModels.cs`
- `IronDev.Core/Governance/OperationStatusAuthorityWarningFormatterValidator.cs`
- `IronDev.Core/Governance/OperationStatusAuthorityWarningFormatter.cs`
- `IronDev.IntegrationTests/BlockD20AuthorityWarningFormatterHardeningTests.cs`
- `Docs/receipts/D20_AUTHORITY_WARNING_FORMATTER_HARDENING.md`

## Boundary

D20 is supplied-safe-facts-only and supplied-`AsOfUtc`-only.

Every rendered line carries this boundary:

```text
Warning only. It grants no authority, denies no authority, and performs no workflow action.
```

The formatter:

- does not read stores;
- does not write stores;
- does not call APIs;
- does not create, update, repair, or project operation status;
- does not perform tenant authorization;
- does not assemble timelines;
- does not invoke missing evidence, forbidden action, receipt, evidence, validation staleness, patch/base freshness, worktree/base/head freshness, interrupted-run, rollback/recovery, or pagination logic;
- does not invoke the D18 envelope factory;
- does not invoke the D19 next-safe-action formatter;
- does not read raw evidence, receipts, timelines, validation logs, patches, diffs, source files, prompts, private reasoning, secrets, tokens, connection strings, raw request bodies, or raw response bodies;
- does not emit command text;
- does not choose workflow steps;
- does not grant approval, policy satisfaction, source apply, rollback, retry, recovery, commit, push, PR creation, ready-for-review, reviewer request, merge, release, deployment, memory promotion, or workflow continuation.

## Safety Rules

- authority-warning formatter-is-display-only
- authority-warning-formatter-is-display-only
- warning-text-is-not-authority
- warning-text-is-not-approval
- warning-text-is-not-policy-satisfaction
- warning-text-is-not-denial
- warning-text-is-not-permission
- warning-text-is-not-workflow-step-selection
- warning-text-does-not-execute-workflow
- supplied-facts-only
- supplied-as-of-only
- controlled-warning-wording-only
- no-system-clock
- no-store-api-ui-sql-surface
- no-upstream-resolver-invocation
- no-paginator-invocation
- no-envelope-factory-invocation
- no-d19-formatter-invocation
- line-count-is-capped

## Validation

| Lane | Result |
| --- | --- |
| Focused D20 | Passed 54/54 |
| Focused D19 | Passed 37/37 |
| Focused D18 | Passed 60/60 |
| Focused D17 | Passed 58/58 |
| Focused D16 | Passed 74/74 |
| Focused D15 | Passed 59/59 |
| Focused D14 | Passed 56/56 |
| Focused D13 | Passed 128/128 |
| Focused D12 | Passed 109/109 |
| Focused D11 | Passed 85/85 |
| Focused D10 | Passed 120/120 |
| Focused D09 | Passed 88/88 |
| Focused D08 | Passed 63/63 |
| Focused D07 | Passed 81/81 |
| Focused D06 | Passed 62/62 |
| Focused D05 | Passed 89/89 |
| Focused D04 | Passed 67/67 |
| Focused D03 | Passed 56/56 |
| Focused D02 | Passed 39/39 |
| Focused D01 | Passed 54/54 |
| D01-D20 stacked resolver/read-model lane | Passed 1439/1439 |
| A02 + A05 read-adapter corridor | Passed 61/61 |
| Governance/status corridor | Passed 1619/1619 |
| Build | Passed, 0 errors / 4 warnings |
| `git diff --check` | Passed |
| `git diff --cached --check` | Passed |

## Review Traps

Reject D20 if it:

- performs lookup;
- reads from or writes to a store;
- adds API, OpenAPI, frontend, SQL, store, auth, or tenant-authorization behavior;
- invokes diagnostic resolvers;
- invokes the D16 paginator;
- invokes the D18 envelope factory;
- invokes the D19 next-safe-action formatter;
- uses system time;
- emits command text;
- emits permission text;
- emits denial text;
- emits raw payloads or raw upstream text;
- exposes `Can*`, approval, policy-satisfaction, action-permission, denial-authority, endpoint, command, mutation, executor, or raw-payload fields;
- touches executors, source apply, rollback, commit, push, PR, merge, release, deploy, memory, or workflow continuation code.

## Review Line

Authority warnings explain boundaries. They do not create authority.

## Killjoy

A warning about authority is still not authority.
