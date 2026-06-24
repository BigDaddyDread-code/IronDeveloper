# E04 - Draft PR Receipt Persistence Hardening

## Purpose

E04 hardens draft pull request receipt persistence as a bounded, reference-only backend witness path.

E01 hardened source-apply receipt persistence. E02 hardened commit receipt persistence. E03 hardened push receipt persistence. E04 applies the same boundary discipline to draft PR receipt metadata after a governed draft PR creation attempt has already occurred elsewhere in the system.

## Review Line

A draft PR receipt is a witness. It is not review, merge, release, or workflow authority.

## Stack

- Base after E03 merge: `main`
- Head branch: `pr/draft-pr-receipt-persistence-hardening`
- Scope: E04 draft PR receipt persistence models, validator, store interface, service, focused tests, and this receipt.

## Files

- `IronDev.Core/Governance/DraftPullRequestReceiptPersistenceModels.cs`
- `IronDev.Core/Governance/DraftPullRequestReceiptPersistenceValidator.cs`
- `IronDev.Core/Governance/IDraftPullRequestReceiptPersistenceStore.cs`
- `IronDev.Core/Governance/DraftPullRequestReceiptPersistenceService.cs`
- `IronDev.IntegrationTests/BlockE04DraftPullRequestReceiptPersistenceHardeningTests.cs`
- `Docs/receipts/E04_DRAFT_PR_RECEIPT_PERSISTENCE_HARDENING.md`

## Persistence Behavior

Draft PR receipt persistence records bounded reference-only draft pull request receipt metadata as a durable witness. It does not create PRs, approve work, satisfy policy, validate freshness, grant authority, mark PRs ready, request reviewers, merge, release, deploy, promote memory, retry, rollback, recover, or continue workflow.

The persistence service:

- requires tenant, project, operation, and correlation scope;
- requires receipt id, draft PR attempt id, push receipt id, push attempt id, commit receipt id, commit attempt id, valid commit SHA, repository reference, provider reference, base branch reference, head branch reference, known outcome, timestamps, and safe source text;
- requires pull request reference, pull request number reference, and observed draft state `Draft` for succeeded outcomes;
- rejects `NotDraft` state for succeeded draft PR receipts;
- rejects pull request references on failed, interrupted, or cancelled outcomes;
- computes a deterministic fingerprint from safe metadata only;
- writes through an injected `IDraftPullRequestReceiptPersistenceStore`;
- treats identical replay of the same receipt fingerprint as `AlreadyPersisted`;
- treats the same receipt id with a different fingerprint as `Conflict`;
- treats the same draft PR attempt id with a conflicting terminal outcome as `Conflict`;
- treats the same pull request ref under different tenant, project, operation, or correlation scope as `Conflict`;
- treats the same pull request number ref under different tenant, project, operation, or correlation scope as `Conflict`;
- rejects cross-tenant, cross-project, cross-operation, and cross-correlation persistence;
- rejects raw PR title/body, raw GitHub/API output, raw patch, raw diff, raw source, raw commit message/body, raw push output, raw Git output, validation log, raw evidence, raw receipt, private reasoning, credential, token, and secret-shaped material.

## Boundary

E04 is persistence-only and reference-only.

E04 does not:

- create PRs;
- invoke PR creation executors;
- call GitHub;
- call Git;
- mark PRs ready;
- request reviewers;
- merge;
- release;
- deploy;
- read source files;
- read raw patches;
- read raw diffs;
- store raw PR title/body content;
- store raw API request/response bodies;
- store raw validation logs;
- accept approval;
- satisfy policy;
- promote memory;
- retry;
- resume;
- recover;
- rollback;
- continue workflow;
- add API, frontend, OpenAPI, CLI, runner, SQL, or provider behavior.

## Reference-Only Rule

The persisted record stores only safe IDs, references, commit SHA references, PR references, PR title/body hashes, timestamps, outcome kind, observed draft state, redaction metadata, source text, and a computed fingerprint.

It does not store raw PR title, raw PR body, raw PR URL with credentials or tokens, raw GitHub/API responses, raw patch content, raw diff content, source file content, changed file lists, raw commit message, raw commit body, raw push output, raw Git output, validation logs, raw receipt payloads, raw evidence payloads, prompt text, private reasoning, secrets, tokens, connection strings, command text, shell/API request bodies, or execution transcripts.

## Validation

| Lane | Result |
| --- | --- |
| Focused E04 | Passed 80/80 |
| Focused E03 | Passed 68/68 |
| Focused E02 | Passed 61/61 |
| Focused E01 | Passed 48/48 |
| Focused D10 | Passed 120/120 |
| Focused D16 | Passed 74/74 |
| D01-D20 stacked resolver/read-model lane | Passed 1439/1439 |
| A02 + A05 read-adapter corridor | Passed 61/61 |
| Governance/status corridor (A02/A05 + D01-D20 + E01-E04) | Passed 1757/1757 |
| Governance boundary CI script | Passed locally: B-series 133/133, BQ-BU 80/80, security boundary 66/66, API boundary 38/38, CLI boundary 41/41 |
| Security boundary tests | Passed through governance-boundary security band 66/66 |
| Build | Passed: 0 errors / 4 warnings |
| E04 secret-shaped fixture scan | Passed: no matches |
| `git diff --check` | Passed |
| `git diff --cached --check` | Passed |

## Review Traps

Reject E04 if it:

- creates PRs;
- invokes PR creation executor code;
- calls GitHub or Git;
- marks PRs ready;
- requests reviewers;
- reads source files;
- reads or stores raw patch/diff/source/PR/API/GitHub/evidence/receipt content;
- stores raw validation logs;
- commits secret-shaped fixture literals;
- weakens secret scanning;
- adds fixture allowlists;
- silently overwrites draft PR receipt records;
- permits conflicting terminal outcomes;
- treats persisted receipt as approval;
- treats persisted receipt as policy satisfaction;
- treats persisted draft PR receipt as ready-for-review authority;
- treats persisted draft PR receipt as reviewer-request authority;
- treats persisted PR number/ref as merge readiness;
- treats persisted failure as retry authority;
- treats persisted interrupted state as resume authority;
- changes API, UI, OpenAPI, runner, SQL, ready-for-review, reviewer, merge, release, deploy, memory, or workflow continuation behavior.

## Killjoy

Persisted draft PR receipt is not ready-for-review, reviewer-request, merge, release, deployment, or workflow continuation authority.
