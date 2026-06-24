# E02 - Commit Receipt Persistence Hardening

## Purpose

E02 hardens commit receipt persistence as a bounded, reference-only backend witness path.

E01 hardened source-apply receipt persistence. E02 applies the same boundary discipline to commit receipt metadata after a governed commit attempt has already occurred elsewhere in the system.

## Review Line

A commit receipt is a witness. It is not push authority.

## Stack

- Base while stacked: `apply/source-apply-receipt-persistence-hardening`
- Head branch: `commit/commit-receipt-persistence-hardening`
- Scope: E02 commit receipt persistence models, validator, store interface, service, focused tests, and this receipt.

## Files

- `IronDev.Core/Governance/CommitReceiptPersistenceModels.cs`
- `IronDev.Core/Governance/CommitReceiptPersistenceValidator.cs`
- `IronDev.Core/Governance/ICommitReceiptPersistenceStore.cs`
- `IronDev.Core/Governance/CommitReceiptPersistenceService.cs`
- `IronDev.IntegrationTests/BlockE02CommitReceiptPersistenceHardeningTests.cs`
- `Docs/receipts/E02_COMMIT_RECEIPT_PERSISTENCE_HARDENING.md`

## Persistence Behavior

Commit receipt persistence records bounded reference-only commit receipt metadata as a durable witness. It does not create commits, approve work, satisfy policy, validate freshness, grant authority, push, create PRs, mark PRs ready, request reviewers, merge, release, deploy, promote memory, retry, rollback, recover, or continue workflow.

The persistence service:

- requires tenant, project, operation, and correlation scope;
- requires receipt id, commit attempt id, commit package id, commit package hash, source-apply receipt id, source-apply attempt id, patch artifact id, patch artifact hash, known outcome, timestamps, and safe source text;
- requires valid commit SHA for succeeded outcomes;
- rejects commit SHA on failed, interrupted, or cancelled outcomes;
- computes a deterministic fingerprint from safe metadata only;
- writes through an injected `ICommitReceiptPersistenceStore`;
- treats identical replay of the same receipt fingerprint as `AlreadyPersisted`;
- treats the same receipt id with a different fingerprint as `Conflict`;
- treats the same commit attempt id with a conflicting terminal outcome as `Conflict`;
- treats the same commit SHA under different tenant, project, operation, or correlation scope as `Conflict`;
- rejects cross-tenant, cross-project, cross-operation, and cross-correlation persistence;
- rejects raw patch, raw diff, raw source, raw commit message/body, validation log, raw evidence, raw receipt, private reasoning, credential, token, and secret-shaped material.

## Boundary

E02 is persistence-only and reference-only.

E02 does not:

- create commits;
- invoke commit executors;
- create commit packages;
- perform source apply;
- call Git;
- read source files;
- read raw patches;
- read raw diffs;
- store raw commit messages or bodies;
- run validation;
- accept approval;
- satisfy policy;
- push;
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

The persisted record stores only safe IDs, references, commit SHA/hash references, timestamps, outcome kind, redaction metadata, source text, and a computed fingerprint.

It does not store raw patch content, raw diff content, source file content, changed file lists, raw commit message, raw commit body, author identity payload, validation logs, raw receipt payloads, raw evidence payloads, prompt text, private reasoning, secrets, tokens, connection strings, command text, shell/API request bodies, or execution transcripts.

## Validation

| Lane | Result |
| --- | --- |
| Focused E02 | Passed 61/61 |
| Focused E01 | Passed 48/48 |
| Focused D10 | Passed 120/120 |
| Focused D16 | Passed 74/74 |
| D01-D20 stacked resolver/read-model lane | Passed 1439/1439 |
| A02 + A05 read-adapter corridor | Passed 61/61 |
| Governance/status corridor | Passed 1728/1728 |
| Governance boundary CI script | Passed locally: B-series 133/133, BQ-BU 80/80, security boundary 66/66, API boundary 38/38, CLI boundary 41/41 |
| Security boundary tests | Passed focused C11/security scan 10/10; governance-boundary security band passed 66/66 |
| Build | Passed: 0 errors / 4 warnings |
| `git diff --check` | Passed |
| `git diff --cached --check` | Passed |

## Review Traps

Reject E02 if it:

- creates commits;
- invokes commit executor code;
- creates commit packages;
- calls Git;
- reads source files;
- reads or stores raw patch/diff/source/commit/evidence/receipt content;
- stores raw validation logs;
- commits secret-shaped fixture literals;
- weakens secret scanning;
- adds fixture allowlists;
- silently overwrites commit receipt records;
- permits conflicting terminal outcomes;
- treats persisted receipt as approval;
- treats persisted receipt as policy satisfaction;
- treats persisted success as push or PR authority;
- treats persisted commit SHA as merge readiness;
- treats persisted failure as retry authority;
- treats persisted interrupted state as resume authority;
- changes API, UI, OpenAPI, runner, SQL, push, PR, merge, release, deploy, memory, or workflow continuation behavior.

## Killjoy

Persisted commit receipt is not source authority, push authority, PR authority, merge readiness, or workflow continuation.
