# H11 Evidence Artifact Retention Policy

## Purpose

H11 defines evidence artifact lifecycle policy, retention classes, expiry expectations, access boundaries, deletion/hold requirements, and future implementation prerequisites.

H11 is a policy and contract-test slice.

Artifact retention policy controls lifecycle. It does not make artifacts safe.

A retained artifact can still leak.

## Relationship To H10

H10 defines raw payload redaction and retention policy.

H11 defines evidence artifact lifecycle policy.

H11 references H10 for raw payload handling. H11 does not re-implement H10 and does not implement redaction.

H11 focuses on:

- artifact classes
- artifact retention expectations
- artifact expiry and hold requirements
- artifact deletion prerequisites
- artifact reference safety
- artifact storage/read boundary
- future implementation requirements

## 1. Scope

This policy covers evidence artifacts and artifact-like records, including:

- patch artifacts
- source-apply artifacts
- source-apply dry-run artifacts
- rollback artifacts
- rollback support artifacts
- rollback execution artifacts
- validation result artifacts
- CI/test result artifacts
- run report artifacts
- workflow checkpoint artifacts
- evidence bundles
- diagnostic bundles
- trace/log artifacts
- exported review packages
- generated patch packages
- external evidence references
- vector/indexed copies where artifact text is derived into Weaviate or another index

H11 does not implement deletion.

H11 does not implement storage changes.

H11 does not implement artifact expiry.

H11 does not implement artifact lifecycle execution.

## 2. Definitions

### `EvidenceArtifact`

A durable or semi-durable file, blob, package, record, bundle, log, patch, generated output, validation result, or reference captured to support review, debugging, audit, rollback, or reconstruction.

### `ArtifactReference`

A pointer to an artifact, such as an ID, URI, hash, path, receipt ID, governance event ID, commit hash, PR ID, or external ticket ID.

A reference is not the artifact body.

### `ArtifactBody`

The actual retained content: file body, patch body, log body, command output, validation output, bundle contents, trace body, or generated package body.

### `ArtifactHash`

A hash used to identify or verify an artifact. A hash is evidence of identity/integrity, not safety.

### `RetentionClass`

The lifecycle category controlling whether the artifact may be retained, for how long, under what access boundary, and with what expiry/hold behavior.

### `LegalOrAuditHold`

A future explicit hold that prevents deletion while investigation, legal, audit, release, or recovery obligations exist.

H11 defines the concept only. It does not implement holds.

### `DeletionEligibility`

A future state meaning an artifact may be deleted because retention requirements are satisfied and no hold applies.

H11 defines the concept only. It does not delete.

## 3. Artifact Retention Classes

### `ReferenceOnlyArtifact`

Only retain reference metadata.

Examples:

- commit hash
- PR URL/number
- artifact ID
- receipt ID
- governance event ID
- safe external ticket ID

### `ShortLivedDiagnosticArtifact`

Temporary artifact retained for debugging only.

Examples:

- test logs
- CI failure logs
- local diagnostic bundle
- transient trace output

Requires future expiry and owner.

### `GovernedEvidenceArtifact`

Artifact retained because it supports governance review, rollback, audit, or reproducibility.

Examples:

- patch artifact
- controlled source-apply receipt package
- rollback execution package
- validation result artifact
- release-readiness evidence package

Requires minimization, access boundary, and retention reason.

### `RecoveryCriticalArtifact`

Artifact required for rollback, interrupted-run recovery, or reconstruction.

Examples:

- patch package needed for rollback
- source baseline references
- rollback support bundle
- operation recovery bundle

Requires explicit expiry/hold decision before deletion.

### `RedactedArtifact`

Artifact body retained after redaction/minimization.

Examples:

- redacted log
- redacted command output
- redacted validation output

Original raw source must not be assumed gone unless deletion is explicitly implemented.

### `StoreProhibitedArtifact`

Artifact that must not be retained.

Examples:

- secret dumps
- raw credentials
- private keys
- raw production data
- raw customer/person data not required for governance
- full environment dumps
- raw prompt/context dumps with sensitive content

### `ExternalArtifactReference`

Reference to artifact stored outside IronDev.

Examples:

- GitHub Actions artifact
- external ticket attachment
- CI provider log URL
- cloud storage object
- document repository link

External retention is not controlled unless an integration explicitly manages it.

## 4. Artifact Lifecycle States

Future lifecycle states:

- `Captured`
- `Classified`
- `Redacted`
- `Retained`
- `Held`
- `DeletionEligible`
- `Deleted`
- `ExternalOnly`
- `Unknown`

These are policy vocabulary only unless already implemented elsewhere.

H11 does not add lifecycle state columns.

H11 does not add lifecycle state transitions.

H11 does not add an artifact lifecycle executor.

## 5. Retention Expectations

| Class | Retention expectation | Required handling |
| --- | --- | --- |
| `ReferenceOnlyArtifact` | Retain reference metadata | No raw body |
| `ShortLivedDiagnosticArtifact` | Temporary only | Future expiry/owner required |
| `GovernedEvidenceArtifact` | Retain while governance value exists | Minimize, classify, boundary-label |
| `RecoveryCriticalArtifact` | Retain until recovery/rollback window closes | Explicit hold/expiry decision required |
| `RedactedArtifact` | Retain redacted body only | Do not assume original gone |
| `StoreProhibitedArtifact` | Do not retain | Reject, redact, or replace with reference |
| `ExternalArtifactReference` | Record reference only | External retention remains outside IronDev unless managed |

No exact day counts are defined in H11 because a complete retention calendar is not present yet. That is a policy gap for a future lifecycle slice.

## 6. Required Artifact Metadata For Future Implementation

Future artifact retention implementation must require:

- artifact ID
- artifact type
- retention class
- artifact reference
- artifact hash, where available
- artifact owner
- project/tenant scope where applicable
- source operation ID where applicable
- source receipt/governance event where applicable
- captured timestamp
- retention reason
- redaction status
- access boundary
- expiry/review timestamp, where applicable
- legal/audit/recovery hold status
- external storage indicator
- deletion eligibility state

H11 does not add this metadata to storage. H11 defines future requirements.

## 7. Artifact Access Policy

- Default access is reference-only.
- Artifact body access requires explicit future contract.
- Viewer role visibility is not artifact body access.
- Operator/support diagnostics must be minimized.
- External artifact links must not expose secrets through URLs/query strings.
- Artifact download/display is not evidence validation.
- Artifact access is not approval authority.
- Artifact access is not policy satisfaction.
- Artifact access is not source-apply authority.

## 8. Artifact Deletion Policy

Future deletion may happen only when all are true:

- artifact has a known retention class
- artifact is not `StoreProhibitedArtifact`
- artifact is not under legal/audit/recovery hold
- artifact no longer supports active rollback/recovery/review
- artifact expiry/review date has passed where applicable
- deletion scope is known
- downstream copies are understood or explicitly out of scope
- deletion is logged as evidence of lifecycle action

H11 does not implement deletion.

H11 does not implement artifact expiry jobs.

H11 does not implement retention deletion jobs.

H11 does not implement cleanup commands.

## 9. External Artifact Policy

External artifacts must be treated carefully:

- IronDev may store external references.
- external retention may be controlled by GitHub, CI, cloud storage, document systems, or ticket systems.
- deleting an IronDev reference does not delete the external artifact.
- deleting an external artifact may break reproducibility.
- External artifact references must avoid credentialed/private query strings.
- external artifact retention requires future integration-specific policy.

## 10. Weaviate / Vector-Index Policy

If artifact content or summaries are indexed into Weaviate or vector stores:

- index safe summaries only.
- never index raw artifact bodies unless a future explicit safe contract exists.
- vector deletion must not be assumed from SQL deletion.
- vector recall is not artifact retention compliance.
- vector recall is not authority.
- rebuildable vector indexes must be reconstructible from safe source material.

H11 does not implement Weaviate deletion, rebuild, authentication, production configuration, or vector-retention behavior.

## 11. Backup And Downstream-Copy Policy

Retention policy must acknowledge:

- backups may retain artifacts after primary deletion
- CI logs may duplicate artifact content
- local developer worktrees may duplicate artifacts
- exported review packages may duplicate artifacts
- vector indexes may duplicate summaries
- external systems may retain copies

Retention policy must not claim deletion is complete unless downstream copies are covered by an implementation contract.

## 12. Non-Authority Boundary

Artifact retention policy controls lifecycle only.

Artifact retention policy does not make artifacts safe.

A retained artifact can still leak.

An artifact hash is not safety.

An artifact reference is not validation.

An artifact body is not approval.

A retained artifact is not approval.

A retained artifact is not policy satisfaction.

A retained artifact is not source-apply authority.

A retained artifact is not workflow continuation authority.

A retained artifact is not merge readiness.

A retained artifact is not release readiness.

A retained artifact is not deployment readiness.

A retained artifact is not rollback authority.

A retained artifact is not retry authority.

A retained artifact is not mutation authority.

A retained artifact does not prove the payload is true.

A retained artifact does not prove the actor was authorized.

A retained artifact does not prove the next action is safe.

Artifact retention policy is lifecycle control, not authority.

## 13. Explicit Non-Implementation

H11 defines policy only.

H11 does not implement artifact deletion.

H11 does not implement artifact expiry.

H11 does not implement retention deletion.

H11 does not implement artifact lifecycle jobs.

H11 does not add a SQL migration.

H11 does not alter tables.

H11 does not add indexes.

H11 does not alter stored procedures.

H11 does not alter triggers.

H11 does not change permissions.

H11 does not change API/CLI/UI behavior.

H11 does not change Weaviate behavior.

H11 does not change workflow/source-apply/rollback/release/deployment authority.

H11 does not add data migration.

H11 does not add projection rebuild.

H11 does not add backfill.

H11 does not add replay.

H11 does not add migration runner or DbUp work.

## 14. Next Slice

H12 - Backup/rebuild story for read projections.

Review line: Projection rebuild plans restore read models. They do not recreate authority records.

Killjoy: A rebuilt projection is not the source of truth.
