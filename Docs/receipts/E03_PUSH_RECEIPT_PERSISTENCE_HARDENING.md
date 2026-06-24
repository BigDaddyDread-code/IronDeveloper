# E03 - Push Receipt Persistence Hardening

## Purpose

E03 hardens push receipt persistence as a bounded, reference-only backend witness path.

E01 hardened source-apply receipt persistence. E02 hardened commit receipt persistence. E03 applies the same boundary discipline to push receipt metadata after a governed push attempt has already occurred elsewhere in the system.

## Review Line

A push receipt is a witness. It is not PR authority.

## Stack

- Base after E02 merge: `main`
- Head branch: `push/push-receipt-persistence-hardening`
- Scope: E03 push receipt persistence models, validator, store interface, service, focused tests, and this receipt.

## Files

- `IronDev.Core/Governance/PushReceiptPersistenceModels.cs`
- `IronDev.Core/Governance/PushReceiptPersistenceValidator.cs`
- `IronDev.Core/Governance/IPushReceiptPersistenceStore.cs`
- `IronDev.Core/Governance/PushReceiptPersistenceService.cs`
- `IronDev.IntegrationTests/BlockE03PushReceiptPersistenceHardeningTests.cs`
- `Docs/receipts/E03_PUSH_RECEIPT_PERSISTENCE_HARDENING.md`

## Persistence Behavior

Push receipt persistence records bounded reference-only push receipt metadata as a durable witness. It does not push, approve work, satisfy policy, validate freshness, grant authority, create PRs, mark PRs ready, request reviewers, merge, release, deploy, promote memory, retry, rollback, recover, or continue workflow.

The persistence service:

- requires tenant, project, operation, and correlation scope;
- requires receipt id, push attempt id, commit receipt id, commit attempt id, valid commit SHA, repository reference, remote reference, target branch reference, known outcome, timestamps, and safe source text;
- requires observed remote head reference for succeeded outcomes;
- rejects observed remote head reference on failed, interrupted, or cancelled outcomes;
- computes a deterministic fingerprint from safe metadata only;
- writes through an injected `IPushReceiptPersistenceStore`;
- treats identical replay of the same receipt fingerprint as `AlreadyPersisted`;
- treats the same receipt id with a different fingerprint as `Conflict`;
- treats the same push attempt id with a conflicting terminal outcome as `Conflict`;
- treats the same commit SHA and target branch under different tenant, project, operation, or correlation scope as `Conflict`;
- treats the same observed remote head under different tenant, project, operation, or correlation scope as `Conflict`;
- rejects cross-tenant, cross-project, cross-operation, and cross-correlation persistence;
- rejects raw remote URLs with credentials, raw patch, raw diff, raw source, raw commit message/body, raw push output, raw Git output, validation log, raw evidence, raw receipt, private reasoning, credential, token, and secret-shaped material.

## Boundary

E03 is persistence-only and reference-only.

E03 does not:

- push;
- invoke push executors;
- call Git;
- read source files;
- read raw patches;
- read raw diffs;
- store raw push output or raw Git output;
- run validation;
- accept approval;
- satisfy policy;
- create PRs;
- mark PRs ready;
- request reviewers;
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

The persisted record stores only safe IDs, references, commit SHA/hash references, remote/head references, timestamps, outcome kind, redaction metadata, source text, and a computed fingerprint.

It does not store raw patch content, raw diff content, source file content, changed file lists, raw commit message, raw commit body, author identity payload, validation logs, raw push output, raw Git output, raw receipt payloads, raw evidence payloads, prompt text, private reasoning, secrets, tokens, connection strings, command text, shell/API request bodies, or execution transcripts.

## Validation

| Lane | Result |
| --- | --- |
| Focused E03 | Passed 68/68 |
| Focused E02 | Passed 61/61 |
| Focused E01 | Passed 48/48 |
| Focused D10 | Passed 120/120 |
| Focused D16 | Passed 74/74 |
| D01-D20 stacked resolver/read-model lane | Passed 1439/1439 |
| A02 + A05 read-adapter corridor | Passed 61/61 |
| Governance/status corridor (A02/A05 + D01-D20 + E01-E03) | Passed 1677/1677 |
| Governance boundary CI script | Passed locally: B-series 133/133, BQ-BU 80/80, security boundary 66/66, API boundary 38/38, CLI boundary 41/41 |
| Security boundary tests | Passed through governance-boundary security band 66/66 |
| Build | Passed: 0 errors / 4 warnings |
| E03 secret-shaped fixture scan | Passed: no matches |
| `git diff --check` | Passed |
| `git diff --cached --check` | Passed |

## Review Traps

Reject E03 if it:

- pushes;
- invokes push executor code;
- calls Git;
- reads source files;
- reads or stores raw patch/diff/source/commit/push/Git/evidence/receipt content;
- stores raw validation logs;
- commits secret-shaped fixture literals;
- weakens secret scanning;
- adds fixture allowlists;
- silently overwrites push receipt records;
- permits conflicting terminal outcomes;
- treats persisted receipt as approval;
- treats persisted receipt as policy satisfaction;
- treats persisted success as PR authority;
- treats persisted push target as ready-for-review authority;
- treats persisted observed remote head as merge readiness or release readiness;
- treats persisted failure as retry authority;
- treats persisted interrupted state as resume authority;
- changes API, UI, OpenAPI, runner, SQL, PR, merge, release, deploy, memory, or workflow continuation behavior.

## Killjoy

Persisted push receipt is not PR creation, ready-for-review, reviewer-request, merge, release, deployment, or workflow continuation authority.
