# D19 - Backend Next Safe Action Formatter Hardening

## Purpose

D19 adds a deterministic backend formatter for operation-status display guidance. It consumes only supplied D18 read envelopes and supplied D07-D15 diagnostic status facts, then renders bounded display lines for a frontend or CLI surface.

The formatter is display-only. It does not resolve evidence, read stores, invoke D07-D18 resolvers, invoke the D16 paginator, refresh validation, inspect source, run commands, apply patches, commit, push, create PRs, merge, release, deploy, promote memory, recover, rollback, retry, or continue workflow.

## Stack

- Base branch: `status/operation-status-error-envelope`
- Head branch: `status/next-safe-action-formatter-hardening`
- Scope: D19 formatter models, validator, formatter, focused tests, and this receipt.

## Files

- `IronDev.Core/Governance/OperationStatusNextSafeActionFormatterModels.cs`
- `IronDev.Core/Governance/OperationStatusNextSafeActionFormatterValidator.cs`
- `IronDev.Core/Governance/OperationStatusNextSafeActionFormatter.cs`
- `IronDev.IntegrationTests/BlockD19NextSafeActionFormatterHardeningTests.cs`
- `Docs/receipts/D19_NEXT_SAFE_ACTION_FORMATTER_HARDENING.md`

## Boundary

D19 formats supplied safe facts only.

Every rendered line carries this boundary:

```text
Display only. This does not grant authority or execute workflow.
```

The formatter:

- does not read stores;
- does not write stores;
- does not call APIs;
- does not create, update, or repair operation status;
- does not call the D18 envelope factory;
- does not invoke missing-evidence, forbidden-action, receipt, evidence, validation staleness, patch/base freshness, worktree/base/head freshness, interrupted-run, rollback/recovery, or pagination logic;
- does not read raw evidence, receipts, timelines, validation logs, patches, diffs, prompts, private reasoning, secrets, tokens, connection strings, raw request bodies, or raw response bodies;
- does not choose executable actions;
- does not grant approval, policy satisfaction, source apply, rollback, retry, recovery, commit, push, PR creation, ready-for-review, reviewer request, merge, release, deployment, memory promotion, or workflow continuation.

## Safety Rules

- formatter-input-is-scoped
- formatter-output-is-display-only
- rendered-lines-carry-boundary
- invalid-input-fails-closed
- ambiguous-status-does-not-select-winner
- unassessable-status-stays-diagnostic
- redacted-status-stays-redacted
- missing-evidence-display-is-not-evidence-resolution
- forbidden-action-display-is-not-action-authority
- receipt-display-is-reference-only
- evidence-display-is-reference-only
- validation-staleness-display-is-not-approval
- freshness-display-is-not-source-apply-authority
- interrupted-run-display-is-not-recovery
- rollback-material-display-is-not-rollback-execution
- line-count-is-capped

## Validation

| Lane | Result |
| --- | --- |
| Focused D19 | Passed: 37/37 |
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
| D01-D19 stacked resolver/read-model lane | Passed: 1385/1385 |
| A02 + A05 read-adapter corridor | Passed: 61/61 |
| Governance/status corridor | Passed: 1565/1565 |
| Build | Passed: 0 errors / 4 warnings |
| `git diff --check` | Passed |
| `git diff --cached --check` | Passed |

## Review Traps

Reject D19 if it:

- reads stores;
- writes stores;
- invokes diagnostic resolvers;
- invokes the D16 paginator;
- invokes the D18 envelope factory;
- adds API, OpenAPI, frontend, SQL, store, auth, or tenant-authorization behavior;
- creates executable instructions;
- uses system time instead of supplied `AsOfUtc`;
- includes `Can*`, approval, policy-satisfaction, action-permission, endpoint, command, mutation, or raw-payload fields;
- leaks raw evidence, raw receipt, raw timeline, raw patch, raw diff, validation log, prompt, private reasoning, secret, token, connection string, raw request body, or raw response body content;
- treats formatted text as approval, policy satisfaction, source apply authority, retry permission, rollback execution, recovery authority, commit, push, PR creation, ready-for-review, reviewer request, merge, release, deployment, memory promotion, or workflow continuation.

## Review Line

Backend guidance may explain what to review next. It does not grant permission to do it.

## Killjoy

Helpful wording is not authority. A formatter can point at the locked door; it cannot unlock it.
