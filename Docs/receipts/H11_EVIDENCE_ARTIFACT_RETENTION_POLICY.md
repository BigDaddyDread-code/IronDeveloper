# H11 Evidence Artifact Retention Policy Receipt

## Purpose

H11 defines evidence artifact lifecycle policy, retention classes, lifecycle-state vocabulary, deletion prerequisites, access boundaries, external artifact caveats, downstream-copy caveats, and future implementation requirements.

H11 defines policy only.

Artifact retention policy controls lifecycle only.

Artifact retention policy does not make artifacts safe.

A retained artifact can still leak.

## Files Changed

- `Docs/policies/H11_EVIDENCE_ARTIFACT_RETENTION_POLICY.md`
- `Docs/receipts/H11_EVIDENCE_ARTIFACT_RETENTION_POLICY.md`
- `IronDev.IntegrationTests/Governance/EvidenceArtifactRetentionPolicyTests.cs`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

H11 does not update `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md` because H11 tests do not connect to SQL or use real external resources.

## Relationship To H10

H10 defines raw payload redaction and retention policy.

H11 defines evidence artifact lifecycle policy.

H11 references H10 for raw payload handling.

H11 does not re-implement H10.

H11 does not implement redaction.

## Artifact Classes Defined

H11 defines these artifact retention classes:

- `ReferenceOnlyArtifact`
- `ShortLivedDiagnosticArtifact`
- `GovernedEvidenceArtifact`
- `RecoveryCriticalArtifact`
- `RedactedArtifact`
- `StoreProhibitedArtifact`
- `ExternalArtifactReference`

## Lifecycle States Defined

H11 defines future lifecycle state vocabulary:

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

## Future Metadata Requirements

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

H11 does not add this metadata to storage.

## Deletion Prerequisites

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

## External And Downstream-Copy Caveats

H11 records:

- IronDev may store external references
- external retention may be controlled by GitHub, CI, cloud storage, document systems, or ticket systems
- deleting an IronDev reference does not delete the external artifact
- deleting an external artifact may break reproducibility
- external artifact references must avoid credentialed/private query strings
- backups may retain artifacts after primary deletion
- CI logs may duplicate artifact content
- local developer worktrees may duplicate artifacts
- exported review packages may duplicate artifacts
- vector indexes may duplicate summaries
- external systems may retain copies
- vector deletion must not be assumed from SQL deletion

Retention policy must not claim deletion is complete unless downstream copies are covered by an implementation contract.

## What Was Intentionally Not Built

H11 does not implement artifact deletion.

H11 does not implement artifact expiry.

H11 does not implement retention deletion.

H11 does not implement artifact lifecycle jobs.

H11 does not implement artifact cleanup commands.

H11 does not implement artifact redaction.

H11 does not add artifact retention columns.

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

## Non-Authority Boundary

Artifact retention policy is not lifecycle execution.

Artifact retention policy is not artifact deletion.

Artifact retention policy is not artifact redaction.

Artifact retention policy is not storage safety.

An artifact hash is not safety.

An artifact reference is not validation.

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

## Tests Added

H11 adds `EvidenceArtifactRetentionPolicyTests`.

The tests prove:

- the policy defines required artifact classes
- the policy defines lifecycle state vocabulary
- the policy defines future metadata requirements
- the policy defines deletion prerequisites without implementing deletion
- the policy defines external/downstream-copy caveats
- the policy and receipt do not treat retained artifacts as authority
- H11 did not add schema, runtime, deletion-job, retention-job, cleanup-command, Weaviate, API, CLI, UI, replay, rebuild, or artifact-lifecycle implementation
- the receipt records scope and limitations

## Commands Run

- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter FullyQualifiedName~EvidenceArtifactRetentionPolicyTests --logger "trx;LogFileName=h11-evidence-artifact-retention-policy.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~EvidenceArtifactRetentionPolicyTests --logger "trx;LogFileName=h11-evidence-artifact-retention-policy.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --logger "trx;LogFileName=h11-category-contract.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~RawPayloadRedactionRetentionPolicyTests|FullyQualifiedName~EvidenceArtifactRetentionPolicyTests" --logger "trx;LogFileName=h10-h11-policy-corridor.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "trx;LogFileName=h11-c11-secret-scan.trx"`
- `dotnet build IronDev.slnx --no-restore`
- `git diff --check`
- `git diff --cached --check`

## Validation Results

- Initial H11 focused test runs: 7/8 passed, with exact wording failures in external/downstream-copy policy lines.
- H11 focused tests after policy wording fixes: 8/8 passed.
- G13 category contract: 7/7 passed.
- H10-H11 policy corridor: 16/16 passed.
- C11 secret scan: first run exceeded the local tool timeout, rerun with longer timeout passed 9/9.
- Solution build: 0 errors / 4 existing warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging the exact H11 files.

## Known Limitations

H11 does not delete existing artifacts.

H11 does not expire existing artifacts.

H11 does not scrub existing artifact stores.

H11 does not prove existing artifacts are clean.

H11 does not prove retained artifacts are safe.

H11 does not prove backups, CI logs, local worktrees, exported packages, vector indexes, or external systems are clean.

H11 does not define exact retention day counts because a complete retention calendar is not present yet.

H11 does not implement hold management.

H11 does not implement deletion eligibility.

H11 does not implement H12.

## Next Intended Slice

H12 - Backup/rebuild story for read projections.

Review line: Projection rebuild plans restore read models. They do not recreate authority records.

Killjoy: A rebuilt projection is not the source of truth.
