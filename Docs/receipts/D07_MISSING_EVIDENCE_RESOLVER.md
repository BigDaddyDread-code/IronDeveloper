# D07 -- Missing Evidence Resolver

## Purpose

D07 adds a read-only missing evidence resolver for governed operations.

It compares scoped evidence requirements against scoped observed evidence metadata and explains which requirements are missing, satisfied, or ambiguous. It consumes D01 operation identity, D03 correlation validation, and D05/D06 status projection contracts without changing their behavior.

## Stack

Base branch while stacked: `status/status-projection-rebuild-test`

Branch: `status/missing-evidence-resolver`

## Files Changed

- `IronDev.Core/Governance/MissingEvidenceResolverModels.cs`
- `IronDev.Core/Governance/MissingEvidenceResolverValidator.cs`
- `IronDev.Core/Governance/MissingEvidenceResolver.cs`
- `IronDev.IntegrationTests/BlockD07MissingEvidenceResolverTests.cs`
- `Docs/receipts/D07_MISSING_EVIDENCE_RESOLVER.md`

## Boundary

The missing evidence resolver explains absent scoped evidence metadata. It does not resolve raw evidence, accept approval, satisfy policy, validate freshness, determine forbidden actions, choose next safe action, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.

Evidence present is not authority. Evidence missing is not a policy decision. Ambiguous evidence is not denial or approval.

## Matching Behavior

Requirement satisfaction is metadata-only:

- tenant, project, and operation must match the request
- requirement kind must match observed evidence kind exactly
- no fuzzy matching
- no text inference
- no correlation-based substitution
- no reference-kind fallback
- no raw payload lookup

Redacted observed evidence remains visible as metadata only and keeps its redaction flag in the satisfied evidence output.

## Ambiguity Boundary

The resolver returns `AmbiguousEvidence` instead of choosing a winner when duplicate IDs, conflicting observed metadata, duplicate indistinguishable requirements, or multiple matching observed evidence records prevent deterministic matching.

Ambiguity is diagnostic only.

## No Expansion

D07 adds no API, SQL, UI, stores, repository wiring, raw evidence payload readers, receipt resolvers, validation freshness resolvers, forbidden action resolvers, next-safe-action formatters, executors, source apply, commit, push, PR creation, merge, release, deploy, memory promotion, workflow continuation, or CI behavior.

## Validation

Local validation on `status/missing-evidence-resolver`:

- D07 focused tests: 81/81 passed
- D06 focused tests: 62/62 passed
- D05 focused tests: 89/89 passed
- D01-D04 focused tests: 216/216 passed
- A02 + A05 read-adapter corridor: 61/61 passed
- governance/status corridor through D07: 674/674 passed
- `dotnet build IronDev.slnx --no-restore -v:minimal`: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed
