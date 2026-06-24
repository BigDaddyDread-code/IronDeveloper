# D10 - Evidence Resolver with Raw-Payload Redaction

## Purpose

D10 adds a read-only resolver that matches scoped evidence reference requests to scoped evidence metadata supplied to the resolver. It can also produce bounded redacted previews from payload text supplied directly to the resolver request.

It is stacked on `status/receipt-reference-resolver`.

## Files Changed

- `IronDev.Core/Governance/EvidenceResolverModels.cs`
- `IronDev.Core/Governance/EvidenceResolverValidator.cs`
- `IronDev.Core/Governance/EvidencePayloadRedactor.cs`
- `IronDev.Core/Governance/EvidenceResolver.cs`
- `IronDev.IntegrationTests/BlockD10EvidenceResolverRedactionTests.cs`
- `Docs/receipts/D10_EVIDENCE_RESOLVER_RAW_PAYLOAD_REDACTION.md`

## Boundary

The evidence resolver resolves scoped evidence metadata and optional redacted previews from supplied payload text only. It does not fetch raw evidence payloads, return raw payloads, verify evidence authenticity, accept approval, satisfy policy, validate freshness, grant authority, choose next safe action, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.

Evidence found is not authority.

Raw payload is never returned.

Complete evidence resolution is not action allowed.

## Metadata-First Behavior

- Tenant, project, operation, evidence kind, and correlation ID must match.
- External reference kind and ID are optional only as a pair.
- When an external reference pair is supplied, both kind and ID must match.
- A direct evidence reference may resolve by exact evidence ID.
- Redacted metadata may be returned only as metadata and must carry a redaction reason.

## Supplied-Payload-Only Redaction

D10 never fetches raw payloads. It only redacts payload text supplied in `SuppliedEvidencePayloadForRedaction`.

The redacted preview is bounded to 512 characters. Truncation is explicit. Unsafe payloads are suppressed instead of leaked.

The redactor detects and redacts or suppresses:

- authorization headers
- bearer tokens
- API keys
- passwords
- token assignments
- connection strings
- private keys
- private reasoning and scratchpad text
- prompt or model response text
- raw evidence, receipt, validation, request, or response body markers
- patch and diff markers
- unsafe control characters

## Resolution States

- `NoReferences`: a valid request contained no evidence references.
- `Resolved`: every requested reference matched exactly one scoped metadata record.
- `PartiallyResolved`: at least one reference matched and at least one did not.
- `NotFound`: no requested reference matched scoped metadata.
- `AmbiguousEvidence`: duplicate or conflicting metadata prevents deterministic resolution.
- `RedactionFailed`: redaction input could not be safely resolved.
- `InvalidRequest`: required scope or metadata is missing or unsafe.

Ambiguity never selects a winner. It is diagnostic only and is not denial or approval.

## Non-Authority Boundaries

- `ValidationEvidence` found is not validation freshness.
- `ApprovalEvidenceReference` found is not accepted approval.
- `PolicyEvidenceReference` found is not policy satisfaction.
- `SourceApplyEvidence` found is not source apply authority.
- `RollbackEvidence` found is not rollback authority or rollback success.
- `CommitEvidence` found is not push authority.
- `PushEvidence` found is not pull request creation authority.
- `PullRequestEvidence` found is not merge readiness.
- `ReleaseReadinessEvidence` found is not release readiness.
- `DeploymentReadinessEvidence` found is not deployment readiness.
- `MemoryPromotionEvidence` found is not memory promotion authority.
- `WorkflowContinuationEvidence` found is not workflow continuation authority.

## No New Surface

D10 does not add API controllers, frontend files, OpenAPI changes, SQL migrations, stores, repositories, executors, runners, source apply, commit, push, pull request creation, merge, release, deploy, memory promotion, or workflow continuation behavior.

The resolver does not perform D02 lookup, D04 timeline assembly, D05 status projection, D07 missing evidence resolution, D08 forbidden-action resolution, or D09 receipt reference resolution.

## Validation

Local validation on `status/evidence-resolver-redaction`:

- D10 focused tests: 120/120 passed
- D01-D10 stacked resolver lane: 719/719 passed
- A02 + A05 read-adapter corridor: 61/61 passed
- Governance/status corridor through D10: 899/899 passed for BJ/BK/BL/BT/BZ plus D01-D10
- `dotnet build IronDev.slnx --no-restore -v:minimal`: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed
