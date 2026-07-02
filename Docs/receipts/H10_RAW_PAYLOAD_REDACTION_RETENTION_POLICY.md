# H10 Raw Payload Redaction and Retention Policy Receipt

## Purpose

H10 defines the raw payload redaction and retention policy for governance-adjacent records.

H10 defines policy only.

Redaction policy limits exposure only.

Retention policy does not make retained payloads safe.

A retained secret is still a secret.

## Files Changed

- `Docs/policies/H10_RAW_PAYLOAD_REDACTION_RETENTION_POLICY.md`
- `Docs/receipts/H10_RAW_PAYLOAD_REDACTION_RETENTION_POLICY.md`
- `IronDev.IntegrationTests/Governance/RawPayloadRedactionRetentionPolicyTests.cs`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

H10 does not update `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md` because H10 tests do not connect to SQL or use real external resources.

## Policy Classes Defined

H10 defines these retention/redaction classes:

- `StoreProhibited`
- `RedactBeforeStore`
- `ReferenceOnly`
- `SafeSummaryAllowed`
- `GovernanceEvidenceRetained`
- `TemporaryDiagnostic`

## Known Surfaces Classified

H10 classifies known raw-ish or high-risk surfaces including:

- `PayloadJson`
- `EvidenceJson`
- `CommandAuditsJson`
- `EvidenceReferencesJson`
- `BoundaryMaximsJson`
- `BoundaryText`
- `FileResultsJson`
- `IssueCodesJson`
- `SourceUri`
- `Summary`
- `EvidenceLabel`
- `EvidenceSummary`
- `SafeSummary`
- `AllowedUse`
- patch artifact content/hash/reference surfaces
- tool request payload surfaces
- governance event payload surfaces
- workflow/handoff/memory proposal evidence reference surfaces
- run report / trace surfaces
- Weaviate/vector-indexed text

Reference-only surfaces remain reference-only.

Safe-summary surfaces remain summary-only.

Raw payload surfaces require redaction/minimization or future explicit exception handling before durable retention.

## Unsafe Marker List

H10 records prohibited or high-risk markers including:

- password
- secret
- token
- bearer
- api key
- private key
- connection string
- credential
- authorization header
- cookie
- session
- refresh token
- access token
- client secret
- SSH key
- certificate private key
- environment dump
- `.env`
- local machine path where unsafe
- private URL query string
- raw email body
- raw customer/person data
- raw legal/regulatory data
- raw production data
- raw prompt/context dump

The marker list is not exhaustive. Unknown sensitive material is still sensitive even if a marker list misses it.

## Required Handling

H10 requires:

- redact before durable storage where possible
- redact before display where storage still contains raw/private material
- never rely on UI-only redaction as storage safety
- never rely on vector retrieval filters as redaction
- never store full secrets and call them evidence
- never store raw external content when a reference, hash, or safe summary is enough
- never include raw private payloads in receipts just because receipts are append-only
- never include raw private payloads in governance events just because events are append-only
- never include raw private payloads in Weaviate/vector text
- keep read models from replaying raw payloads through safe references
- keep viewer/operator/support visibility minimized and non-authoritative

## What Was Intentionally Not Built

H10 does not implement redaction.

H10 does not implement retention deletion.

H10 does not implement evidence artifact retention.

H11 owns evidence artifact retention policy.

H10 does not implement artifact deletion.

H10 does not add a SQL migration.

H10 does not alter tables.

H10 does not add indexes.

H10 does not alter stored procedures.

H10 does not alter triggers.

H10 does not change permissions.

H10 does not change API/CLI/UI behavior.

H10 does not change Weaviate behavior.

H10 does not change workflow/source-apply/rollback/release/deployment authority.

H10 does not add data migration.

H10 does not add projection rebuild.

H10 does not add backfill.

H10 does not add replay.

H10 does not add migration runner or DbUp work.

## Non-Authority Boundary

Retention policy does not make retained payloads true.

A retained payload is not approval.

A retained payload is not policy satisfaction.

A retained payload is not source-apply authority.

A retained payload is not workflow continuation authority.

A retained payload is not merge readiness.

A retained payload is not release readiness.

A retained payload is not deployment readiness.

A retained payload is not rollback authority.

A retained payload is not retry authority.

A retained payload is not mutation authority.

A retained payload does not prove the payload is true.

A retained payload does not prove the actor was authorized.

A retained payload does not prove the next action is safe.

A redacted payload is not necessarily safe if originals remain elsewhere.

Redaction means exposure is limited. It does not mean the original never existed.

Existing payloads are not clean merely because this policy exists.

## Tests Added

H10 adds `RawPayloadRedactionRetentionPolicyTests`.

The tests prove:

- the policy defines the required payload/retention classes
- the policy lists required unsafe material markers
- the policy classifies known raw-ish/high-risk surfaces
- the policy requires redaction before storage/display where needed
- the policy rejects UI-only redaction and vector filtering as storage safety
- the policy defers evidence artifact retention lifecycle to H11
- the policy and receipt do not treat retained payloads as authority
- H10 did not add schema, runtime, redaction-engine, retention-job, Weaviate, API, CLI, UI, replay, rebuild, or artifact-deletion implementation
- the receipt records scope and limitations

## Commands Run

- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter FullyQualifiedName~RawPayloadRedactionRetentionPolicyTests --logger "trx;LogFileName=h10-raw-payload-policy.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~RawPayloadRedactionRetentionPolicyTests --logger "trx;LogFileName=h10-raw-payload-policy.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --logger "trx;LogFileName=h10-category-contract.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "trx;LogFileName=h10-c11-secret-scan.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~TenantEnforcementReadModelTests|FullyQualifiedName~UtcTimestampDbConstraintReviewTests|FullyQualifiedName~RawPayloadRedactionRetentionPolicyTests" --logger "trx;LogFileName=h08-h10-policy-corridor.trx"`
- `dotnet build IronDev.slnx --no-restore`
- `git diff --check`
- `git diff --cached --check`

## Validation Results

- Initial H10 focused test run: 7/8 passed, 1 receipt wording failure for missing exact H11 ownership wording.
- H10 focused tests after receipt fix: 8/8 passed.
- G13 category contract: 7/7 passed.
- C11 secret scan: 9/9 passed.
- H08-H10 policy/read-model/metadata corridor: 25/25 passed.
- Solution build: 0 errors / 4 existing warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging the exact H10 files.

## Known Limitations

H10 does not sanitize historical data.

H10 does not prove existing records are clean.

H10 does not remove already-retained secrets.

H10 does not prove backups, traces, artifacts, logs, vector indexes, or downstream copies are clean.

H10 does not define exact retention day counts because a complete retention calendar is not present yet.

H10 does not implement exception handling for raw payload retention.

H10 does not implement evidence artifact retention lifecycle.

H11 owns evidence artifact retention policy.

H10 does not implement H11.

## Next Intended Slice

H11 - Evidence artifact retention policy.

Review line: Artifact retention policy controls lifecycle. It does not make artifacts safe.

Killjoy: A retained artifact can still leak.
