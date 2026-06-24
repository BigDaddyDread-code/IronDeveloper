# E01 - Source Apply Receipt Persistence Hardening

## Purpose

E01 hardens source-apply receipt persistence as a bounded, reference-only backend witness path.

Block D made operation state readable and explainable. E01 starts the next backend hardening pass by ensuring source-apply receipt metadata can be persisted with tenant, project, operation, and correlation scope without creating action authority.

## Review Line

A source-apply receipt is a witness. It is not permission.

## Stack

- Base branch while D rollup is pending: `status/d-rollup-main`
- Head branch: `apply/source-apply-receipt-persistence-hardening`
- Scope: E01 source-apply receipt persistence models, validator, store interface, service, focused tests, and this receipt.

## Files

- `IronDev.Core/Governance/SourceApplyReceiptPersistenceModels.cs`
- `IronDev.Core/Governance/SourceApplyReceiptPersistenceValidator.cs`
- `IronDev.Core/Governance/ISourceApplyReceiptPersistenceStore.cs`
- `IronDev.Core/Governance/SourceApplyReceiptPersistenceService.cs`
- `IronDev.IntegrationTests/BlockE01SourceApplyReceiptPersistenceHardeningTests.cs`
- `Docs/receipts/E01_SOURCE_APPLY_RECEIPT_PERSISTENCE_HARDENING.md`

## Persistence Behavior

Source apply receipt persistence records bounded reference-only receipt metadata as a durable witness. It does not perform source apply, approve work, satisfy policy, validate freshness, grant authority, commit, push, create PRs, merge, release, deploy, promote memory, retry, rollback, recover, or continue workflow.

The persistence service:

- requires tenant, project, operation, and correlation scope;
- requires receipt id, source-apply attempt id, patch artifact id, patch artifact hash, known outcome, timestamps, and safe source text;
- computes a deterministic fingerprint from safe metadata only;
- writes through an injected `ISourceApplyReceiptPersistenceStore`;
- treats identical replay of the same receipt fingerprint as `AlreadyPersisted`;
- treats the same receipt id with a different fingerprint as `Conflict`;
- treats the same source-apply attempt id with a conflicting terminal outcome as `Conflict`;
- rejects cross-tenant, cross-project, cross-operation, and cross-correlation persistence;
- rejects raw patch, raw diff, raw source, validation log, raw evidence, raw receipt, private reasoning, credential, token, and secret-shaped material.

## Boundary

E01 is persistence-only and reference-only.

E01 does not:

- perform source apply;
- invoke source apply executors;
- call Git;
- read source files;
- read raw patches;
- read raw diffs;
- run validation;
- accept approval;
- satisfy policy;
- create commit packages;
- commit;
- push;
- create PRs;
- merge;
- release;
- deploy;
- promote memory;
- retry;
- resume;
- recover;
- rollback;
- continue workflow;
- add API, frontend, OpenAPI, CLI, runner, SQL, or provider behavior.

## Reference-Only Rule

The persisted record stores only safe IDs, references, timestamps, outcome kind, redaction metadata, source text, and a computed fingerprint.

It does not store raw patch content, raw diff content, source file content, validation logs, raw receipt payloads, raw evidence payloads, prompt text, private reasoning, secrets, tokens, connection strings, command text, shell/API request bodies, or execution transcripts.

## Validation

| Lane | Result |
| --- | --- |
| Focused E01 | Passed 48/48 |
| Focused D10 | Passed 120/120 |
| Focused D16 | Passed 74/74 |
| D01-D20 stacked resolver/read-model lane | Passed 1439/1439 |
| A02 + A05 read-adapter corridor | Passed 61/61 |
| Governance/status corridor | Passed 1667/1667 |
| Governance boundary CI script | Passed locally: B-series 133/133, BQ-BU 80/80, security boundary 66/66, API boundary 38/38, CLI boundary 41/41 |
| Security boundary tests | Passed focused C11/security scan 10/10; governance-boundary security band passed 66/66 |
| Build | Passed: 0 errors / 4 warnings |
| `git diff --check` | Passed |
| `git diff --cached --check` | Passed |

## Review Traps

Reject E01 if it:

- performs source apply;
- invokes source apply executor code;
- calls Git;
- reads source files;
- reads or stores raw patch/diff/source/receipt/evidence content;
- stores raw validation logs;
- commits secret-shaped fixture literals;
- weakens secret scanning;
- adds fixture allowlists;
- silently overwrites receipt records;
- permits conflicting terminal outcomes;
- treats persisted receipt as approval;
- treats persisted receipt as policy satisfaction;
- treats persisted success as commit, push, or PR authority;
- treats persisted failure as retry authority;
- treats persisted interrupted state as resume authority;
- changes API, UI, OpenAPI, runner, SQL, merge, release, deploy, memory, or workflow continuation behavior.

## Killjoy

Persisted receipt is not source authority, commit authority, push authority, PR authority, or workflow continuation.
