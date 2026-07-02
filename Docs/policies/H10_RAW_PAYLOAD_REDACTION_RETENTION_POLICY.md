# H10 Raw Payload Redaction and Retention Policy

## Purpose

H10 defines how IronDev classifies, redacts, retains, and refuses raw payloads in governance-adjacent records.

H10 is a policy and contract-test slice.

Redaction policy limits exposure. It does not make retained payloads safe.

A retained secret is still a secret.

## 1. Scope

This policy covers raw-ish payloads and high-risk metadata in:

- governance events
- tool requests
- tool execution audits
- tool gate decisions
- approval decision records
- policy decision records
- receipts
- evidence references
- patch artifacts
- source-apply artifacts
- rollback artifacts
- workflow records
- agent handoffs
- memory proposals
- run reports
- diagnostic traces
- frontend/read-model summaries
- Weaviate or vector-indexed text, if present

H10 does not implement artifact lifecycle behavior. Evidence artifact retention policy belongs to H11.

H10 may identify artifact references and expected handling. H10 must not implement artifact deletion, artifact expiry, artifact lifecycle jobs, or retention jobs.

## 2. Definitions

### `RawPayload`

Unredacted tool input/output, model prompt/response, code/file content, command output, stack trace, API response, external record, log body, database row payload, email/message text, document body, or other source material captured before minimization.

### `SensitivePayload`

Payload that may contain secrets, credentials, API keys, tokens, passwords, private keys, connection strings, personal data, customer data, legal/regulatory material, private URLs, local paths, internal hostnames, account identifiers, or confidential source content.

### `SafeSummary`

A minimized, purpose-limited summary that removes secrets/private content and preserves only what a reviewer needs.

### `EvidenceReference`

A pointer to evidence, such as an ID, hash, URI, receipt reference, governance event ID, artifact ID, or safe label. A reference is not the raw payload.

### `Redaction`

Removal, masking, hashing, or replacement of sensitive/raw content before durable storage or display.

### `RetentionClass`

The policy category that controls whether and how long material may be retained.

## 3. Raw Payload Classification

### `StoreProhibited`

Must not be stored durably.

Examples:

- secrets
- passwords
- API keys
- private keys
- bearer tokens
- connection strings
- raw environment dumps
- full credentialed request/response bodies
- private customer/person data not needed for governance
- full email/message/document bodies unless explicitly approved for a safe evidence workflow

### `RedactBeforeStore`

May be stored only after redaction/minimization.

Examples:

- command output with file paths or hostnames
- stack traces
- API error bodies
- model responses containing copied source/context
- logs that may contain identifiers or private URLs

### `ReferenceOnly`

Store only IDs, hashes, URIs, or safe labels. Do not store the raw content body.

Examples:

- artifact IDs
- governance event IDs
- receipt IDs
- pull request IDs
- commit hashes
- evidence hashes
- external ticket IDs

### `SafeSummaryAllowed`

Store a short safe summary if it does not contain secrets/private raw content.

Examples:

- "validation failed because test X failed"
- "rollback plan references patch artifact Y"
- "tool request was denied by policy Z"

### `GovernanceEvidenceRetained`

Durable evidence needed for auditability, but still minimized.

Examples:

- approval decision metadata
- policy decision metadata
- source-apply receipt metadata
- rollback receipt metadata
- governance event payloads that have already passed unsafe-marker checks

### `TemporaryDiagnostic`

May be retained briefly for debugging and must have explicit expiry/owner in a future implementation.

Examples:

- local debug traces
- transient test logs
- CI failure snippets
- diagnostic bundles

## 4. Retention Policy

| Class | Retention expectation | Required handling |
| --- | --- | --- |
| `StoreProhibited` | Do not retain | Reject or redact before storage |
| `RedactBeforeStore` | Retain only redacted form | Redaction required before durable write/display |
| `ReferenceOnly` | Retain reference only | No raw payload body |
| `SafeSummaryAllowed` | Retain safe summary | Must be purpose-limited |
| `GovernanceEvidenceRetained` | Retain as governed evidence | Must be minimized and boundary-labeled |
| `TemporaryDiagnostic` | Short-lived only | Future deletion/expiry implementation required |

No exact day counts are defined in H10 because the project does not yet have a complete retention calendar. That is a policy gap for a future lifecycle slice.

## 5. Redaction Rules

- Redact before durable storage where possible.
- Redact before display where storage still contains raw/private material.
- Never rely on UI-only redaction as storage safety.
- Never rely on vector retrieval filters as redaction.
- Never store full secrets and call them evidence.
- Never store raw external content when a reference, hash, or safe summary is enough.
- Never include raw private payloads in receipts just because receipts are append-only.
- Never include raw private payloads in governance events just because events are append-only.
- Never include raw private payloads in Weaviate or vector text.
- Append-only storage does not make raw payloads safe.
- Redacted display is not proof storage is redacted.

## 6. Required Unsafe Material List

The following markers are prohibited or high-risk:

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

This list is not exhaustive. Unknown sensitive material is still sensitive even if the marker list misses it.

## 7. Surface Inventory

Known raw-ish/high-risk surfaces are classified as follows:

| Surface | Classification | Policy handling |
| --- | --- | --- |
| `PayloadJson` | `RedactBeforeStore` or `GovernanceEvidenceRetained` | Store only minimized, unsafe-marker-checked governance payloads. |
| `EvidenceJson` | `RedactBeforeStore` or `GovernanceEvidenceRetained` | Store minimized evidence metadata; do not store raw evidence bodies unless a future exception contract allows it. |
| `CommandAuditsJson` | `RedactBeforeStore` | Store command/audit metadata and redacted snippets only. |
| `EvidenceReferencesJson` | `ReferenceOnly` | Store IDs/hashes/refs, not evidence payload bodies. |
| `BoundaryMaximsJson` | `SafeSummaryAllowed` | Store boundary text only; no raw secrets/private content. |
| `BoundaryText` | `SafeSummaryAllowed` | Store short boundary statements only. |
| `FileResultsJson` | `RedactBeforeStore` | Store file result metadata and safe snippets only; do not store full file content. |
| `IssueCodesJson` | `ReferenceOnly` | Store issue codes/labels only. |
| `SourceUri` | `ReferenceOnly` | Store source reference only; private query strings are prohibited. |
| `Summary` | `SafeSummaryAllowed` | Store purpose-limited safe summary only. |
| `EvidenceLabel` | `ReferenceOnly` | Store safe label only. |
| `EvidenceSummary` | `SafeSummaryAllowed` | Store minimized summary only. |
| `SafeSummary` | `SafeSummaryAllowed` | Store minimized summary only. |
| `AllowedUse` | `SafeSummaryAllowed` | Store policy text only; not authority. |
| patch artifact content/hash/reference surfaces | `ReferenceOnly` for hashes/refs; `RedactBeforeStore` for raw content | H11 owns artifact lifecycle policy. |
| tool request payload surfaces | `RedactBeforeStore` | Store only minimized/redacted request material. |
| governance event payload surfaces | `GovernanceEvidenceRetained` only after unsafe-marker checks | Append-only events are not safe places for secrets. |
| workflow/handoff/memory proposal evidence reference surfaces | `ReferenceOnly` or `SafeSummaryAllowed` | Store references and safe summaries only. |
| run report / trace surfaces | `TemporaryDiagnostic` or `SafeSummaryAllowed` | Keep raw diagnostic material short-lived until future expiry tooling exists. |
| Weaviate/vector-indexed text | `SafeSummaryAllowed` only | Index safe summaries or approved redacted text only. |

If a listed surface is already safe-summary/reference-only by design, this policy preserves that classification.

## 8. Display and Read-Model Policy

Read models and frontend/API surfaces must not expand safe references back into raw payloads unless a future explicit authority/safety contract allows it.

- Summary display is not raw payload display.
- Reference display is not evidence validation.
- Redacted display is not proof storage is redacted.
- Read-model projection must not become raw-payload replay.
- Viewer role visibility is not permission to expose raw payload.
- Operator/support diagnostics must be minimized by default.
- Receipt metadata display must remain reference-only unless a future payload-access contract exists.
- Evidence metadata display must remain reference-only unless a future payload-access contract exists.

## 9. Weaviate / Vector Policy

If text is indexed into Weaviate or another vector store:

- Index only safe summaries or approved redacted content.
- Never index secrets/raw payloads.
- Vector recall is not authority.
- Vector recall is not retention compliance.
- Deleting SQL/source records must not be assumed to delete vector copies unless the deletion path explicitly handles it.

H10 does not implement Weaviate deletion, rebuild, authentication, production configuration, or vector-retention behavior. Later Weaviate and retention slices own those implementation details.

## 10. Audit and Exception Policy

If raw payload retention is ever required, a future implementation must require:

- explicit retention class
- purpose
- owner
- expiry/review date
- access boundary
- redaction status
- evidence reference
- reason safer reference/summary was insufficient

H10 does not implement exception handling. H10 defines the requirement.

## 11. Non-Authority Boundary

Redaction policy does not make retained payloads safe.

Retention policy does not make retained payloads true.

A retained secret is still a secret.

A redacted payload is not approval.

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

Existing payloads are not clean merely because this policy exists.

Redaction means exposure is limited. It does not mean the original never existed.

Retention is evidence handling, not authority.

## 12. Explicit Non-Implementation

H10 does not implement redaction.

H10 does not implement retention deletion.

H10 does not implement evidence artifact retention.

H10 does not add a SQL migration.

H10 does not alter tables.

H10 does not add indexes.

H10 does not alter stored procedures.

H10 does not alter triggers.

H10 does not change permissions.

H10 does not change API/CLI/UI behavior.

H10 does not change Weaviate behavior.

H10 does not change workflow/source-apply/rollback/release/deployment authority.

H10 does not add data migration, projection rebuild, backfill, replay, migration runner, or DbUp adoption.

## 13. Next Slice

H11 owns evidence artifact retention policy.

Review line: Artifact retention policy controls lifecycle. It does not make artifacts safe.

Killjoy: A retained artifact can still leak.
