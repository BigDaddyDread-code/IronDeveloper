# D09 - Receipt Reference Resolver

## Purpose

D09 adds a read-only resolver that matches scoped receipt reference requests to scoped receipt metadata supplied to the resolver.

It is stacked on `status/forbidden-action-resolver`.

## Files Changed

- `IronDev.Core/Governance/ReceiptReferenceResolverModels.cs`
- `IronDev.Core/Governance/ReceiptReferenceResolverValidator.cs`
- `IronDev.Core/Governance/ReceiptReferenceResolver.cs`
- `IronDev.IntegrationTests/BlockD09ReceiptReferenceResolverTests.cs`
- `Docs/receipts/D09_RECEIPT_REFERENCE_RESOLVER.md`

## Boundary

The receipt reference resolver resolves scoped receipt metadata references only. It does not fetch raw receipt payloads, verify receipt authenticity, prove execution, accept approval, satisfy policy, validate freshness, grant authority, choose next safe action, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.

Receipt resolution is reference-only and metadata-only. A found receipt is a witness that metadata exists, not permission to perform the action named by the receipt kind.

## Resolution States

- `NoReferences`: a valid request contained no receipt references.
- `Resolved`: every requested reference matched exactly one scoped metadata record.
- `PartiallyResolved`: at least one reference matched and at least one did not.
- `NotFound`: no requested reference matched scoped metadata.
- `AmbiguousReferences`: duplicate or conflicting metadata prevents deterministic resolution.
- `InvalidRequest`: required scope or metadata is missing or unsafe.

Ambiguity never selects a winner. It is diagnostic only and is not denial or approval.

## Reference Rules

- Tenant, project, operation, receipt kind, and correlation ID must match.
- External reference kind and ID are optional only as a pair.
- When an external reference pair is supplied, both kind and ID must match.
- A direct receipt reference may resolve by exact receipt ID.
- Redacted metadata may be returned only as metadata and must carry a redaction reason.

## Non-Authority Boundaries

- `ValidationReceipt` found is not validation freshness.
- `SourceApplyReceipt` found is not source apply authority.
- `RollbackReceipt` found is not rollback authority or rollback success.
- `CommitReceipt` found is not push authority.
- `PushReceipt` found is not pull request creation authority.
- `PullRequestReceipt` found is not merge readiness.
- `ReleaseReadinessReceipt` found is not release readiness.
- `DeploymentReadinessReceipt` found is not deployment readiness.
- `MemoryPromotionReceipt` found is not memory promotion authority.
- `WorkflowContinuationReceipt` found is not workflow continuation authority.
- Complete receipt resolution is not action allowed.

## No New Surface

D09 does not add API controllers, frontend files, OpenAPI changes, SQL migrations, stores, repositories, executors, runners, source apply, commit, push, pull request creation, merge, release, deploy, memory promotion, or workflow continuation behavior.

The resolver does not perform D02 lookup, D04 timeline assembly, D05 status projection, D07 missing evidence resolution, or D08 forbidden-action resolution.

## Validation

Local validation on `status/receipt-reference-resolver`:

- D09 focused tests: 88/88 passed
- D08 focused tests: 63/63 passed
- D07 focused tests: 81/81 passed
- D06 focused tests: 62/62 passed
- D05 focused tests: 89/89 passed
- D01-D04 focused tests: 216/216 passed
- D01-D09 stacked resolver lane: 599/599 passed
- A02 + A05 read-adapter corridor: 61/61 passed
- Governance/status corridor through D09: 1156/1156 passed
- `dotnet build IronDev.slnx --no-restore -v:minimal`: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed
