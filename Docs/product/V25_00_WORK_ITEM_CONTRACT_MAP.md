# V25-00 Work Item Contract Map

**Status:** Ready for implementation planning
**Purpose:** Lock the current-to-target ownership map before V25-01 adds durable Work Item storage.
**Scope:** Product, API, database, and compatibility contract only. No schema or runtime change is introduced by this document.

## Why This Exists

v2 has a useful Work Item route and read model, but the current implementation is still ticket-backed. The route parameter called `workItemId` is passed to `ITicketService.GetTicketByIdAsync`, collaboration rows point at `dbo.ProjectTickets`, and the Work Item read model composes ticket, run, approval, apply, membership, and activity truth at read time.

That was the right bridge for v2. It is not enough for v2.5.

v2.5 needs a durable Work Item that owns the lifecycle across contract revisions, runs, repairs, approvals, failed apply attempts, retries, and final outcome. A ticket becomes the versioned contract inside the Work Item. A run becomes an attempt inside the Work Item. Chat/Workshop, documents, approval, apply, and audit remain provenance and evidence, not lifecycle owners.

## Non-Goals

This slice does not:

- add `dbo.WorkItems`;
- migrate rows;
- rename routes;
- change the current `/work-items/{id}` API behavior;
- rename Chat to Workshop in the product;
- add agent profiles, AI connections, credentials, or run snapshots;
- change approval, continuation, apply, commit, push, release, or deployment authority.

## Current Truth Map

| Current area | Current owner | Current role | v2.5 target |
| --- | --- | --- | --- |
| Confirmed user intent and acceptance criteria | `dbo.ProjectTickets` / `ProjectTicket` | Stores the current ticket fields, structured acceptance criteria, linked source, source chat, source document version, status, and revision. | Becomes `WorkItemContract`, versioned under a durable Work Item. |
| Work Item route | `api/projects/{projectId}/work-items/{workItemId}` | Currently treats `workItemId` as `ProjectTickets.Id`. | Route remains canonical. Existing identifiers continue to resolve after migration. |
| Work Item projection | `ProjectWorkItemReadService` / `ProjectWorkItemProjector` | Composes ticket, latest run, readiness, collaboration, membership, apply recovery, execution proof, and evidence links. | Reads from durable Work Item identity first, then current contract and run attempts. |
| Collaboration ownership | `dbo.ProjectWorkItemCollaboration`, `dbo.ProjectWorkItemFollowers`, `dbo.ProjectWorkItemActivity` | Stores assignee, waiting-on state, followers, and activity, but foreign keys use `dbo.ProjectTickets(Id)`. | Points to durable `WorkItem.Id`; activity remains collaboration/evidence, not authority. |
| Workshop/Chat provenance | `dbo.ProjectChatSessions`, `dbo.ChatMessages`, ticket source fields | Records conversation/session/message provenance and ticket draft origins. | Workshop creates Work Items with exact source messages, participants, and open questions. |
| Document provenance | `dbo.ProjectDocuments`, `dbo.ProjectDocumentVersions`, `ProjectTickets.SourceDocumentVersionId` | Stores immutable document versions and optional ticket source version. | Work Item contract creation records exact document versions used. |
| Governed runs | run stores and workflow run stores | Store attempts, durable execution events, evidence, gates, and statuses, linked today through ticket/run identifiers. | Runs are attempts under a Work Item. A failed run, repair, or retry stays inside the same Work Item. |
| Accepted approvals | `governance.AcceptedApproval` | Stores exact approval target kind, target id, target hash, capability, actor, evidence, and expiry. | Approval remains exact-package authority. Work Item identity may group approvals but cannot broaden them. |
| Apply evidence | `workflow.ApplyDryRunRecord`, `governance.SourceApplyDryRunReceipt`, `governance.SourceApplyReceipt` | Stores dry-run, source-apply gate, mutation, rollback, and receipt evidence. | Apply attempts remain evidence under a Work Item run attempt. They do not change Work Item contract by themselves. |
| Audit ledger | `GET /api/v1/audit/ledger` | Read-only union over run events, approvals, Work Item activity, chat, documents, versions, and membership. | Adds durable Work Item identity as a filter and evidence link; remains read-only. |

## Target Ownership

The target durable aggregate is:

```text
WorkItem
- Id
- TenantId
- ProjectId
- Title
- OriginKind
- OriginReference
- CurrentContractId
- CurrentRunId
- CurrentStage
- CurrentState
- AssigneeUserId
- WaitingOnKind
- WaitingOnReference
- CreatedByUserId
- CreatedUtc
- UpdatedUtc
- Version
```

The associated versioned contract is:

```text
WorkItemContract
- Id
- TenantId
- ProjectId
- WorkItemId
- ContractVersion
- SourceTicketId
- Title
- Summary
- Problem
- AcceptanceCriteria
- TechnicalNotes
- TestExpectations
- LinkedFilePaths
- LinkedCodeIndexEntryIds
- LinkedSymbols
- SourceWorkshopSessionId
- SourceWorkshopMessageIds
- SourceDocumentVersionIds
- CreatedByUserId
- CreatedUtc
- SupersedesContractId
- ContractHash
```

The associated run attempt link is:

```text
WorkItemRunAttempt
- Id
- TenantId
- ProjectId
- WorkItemId
- RunId
- AttemptNumber
- StartedByUserId
- StartedUtc
- CompletedUtc
- Status
- PackageHash
- ApprovalTargetId
- ApplyAttemptId
- CreatedUtc
```

These names are planning names. V25-01 may adjust physical names to match repository conventions, but it must preserve the ownership split.

## Locked Ownership Rules

1. `WorkItem` owns lifecycle identity.
2. `WorkItemContract` owns the confirmed contract text and version history.
3. Runs own execution attempts and evidence.
4. Accepted approvals own exact approval authority.
5. Apply receipts own source mutation evidence.
6. Workshop owns formation provenance.
7. Documents own immutable document versions.
8. Audit owns read-only traceability.
9. Membership owns visibility and collaboration eligibility.
10. No UI route, profile, prompt, document, chat message, or local state grants authority.

## Identifier Compatibility

Existing URLs and user-visible identifiers must continue to work:

```text
/projects/{projectId}/work-items/{existingTicketId}
```

V25-01/V25-02 should preserve this by assigning migrated `WorkItem.Id` values from existing `ProjectTickets.Id` values where possible.

For new Work Items after the migration:

- the backend returns the canonical `WorkItem.Id`;
- compatibility ticket APIs return or link to the owning Work Item;
- frontend navigation uses the canonical Work Item route;
- legacy ticket routes may redirect or continue as contract-edit compatibility, but must not become a second lifecycle surface.

If exact identity preservation is impossible in a target environment, V25-02 must add an explicit legacy-identifier lookup:

```text
LegacyTicketId -> WorkItemId
```

That lookup must be backend-owned and tested. The client must not guess.

## Migration Contract

### V25-01: Add Core Identity

Add durable Work Item storage and link fields without changing user-facing behavior.

Required behavior:

- create a `WorkItem` row for every non-deleted `ProjectTickets` row;
- preserve `TenantId`, `ProjectId`, title, created timestamp, and creator where known;
- set `CurrentContractId` to the first contract row generated from the existing ticket;
- preserve current ticket id compatibility;
- keep existing Work Item read projection behavior stable;
- leave approval, run, apply, and audit authority unchanged.

### V25-02: Migrate and Retarget Compatibility

Move lifecycle reads to `WorkItem` first, then contract/run evidence.

Required behavior:

- `/api/projects/{projectId}/work-items/{workItemId}` reads durable Work Item identity;
- existing ticket-backed URLs still resolve;
- collaboration rows attach to `WorkItem.Id`;
- run lookup can list attempts for a Work Item, not only latest run for a ticket;
- ticket creation from Workshop creates a Work Item plus initial contract;
- ticket edit compatibility updates or creates a contract revision according to backend rules;
- stale writes use version checks on both Work Item and contract where relevant.

## Stage and State Source

`WorkItem.CurrentStage` and `WorkItem.CurrentState` are product navigation summaries, not substitutes for evidence.

Allowed stages:

```text
Shape
Ticket
Build
Review
Done
```

Stage derivation:

- `Shape`: Workshop provenance exists but no confirmed contract.
- `Ticket`: confirmed contract exists but no active governed run.
- `Build`: a governed run exists and is building, testing, repairing, or blocked before review.
- `Review`: a sealed package or critic findings require review, approval, or continuation.
- `Done`: final outcome is applied, abandoned, refused, or otherwise terminal.

State must name the waiting condition plainly, such as:

- `Drafting`
- `ReadyToRun`
- `Running`
- `WaitingForReview`
- `WaitingForApproval`
- `WaitingForContinuation`
- `ApplyReady`
- `ApplyFailed`
- `Applied`
- `Abandoned`

State does not grant authority. The backend still rechecks readiness, approval, package hash, apply gates, and membership before action.

## Contract Versioning Rules

A Work Item can have multiple contracts. Only one is current.

Create a new contract version when:

- acceptance criteria materially change;
- affected file scope materially changes;
- test expectations materially change;
- Workshop produces a new confirmed proposal after open questions are resolved;
- a repair or failed run requires explicit contract revision rather than implementation-only retry.

Do not create a new contract version for:

- assignment change;
- follower change;
- waiting-on change;
- run status change;
- approval;
- apply attempt;
- document upload without explicit contract inclusion;
- audit entry.

Historical runs keep their original contract hash and profile/run snapshots. Updating a Work Item contract does not rewrite old runs.

## Workshop Creation Rules

Workshop can propose a Work Item. It cannot approve it.

Creating a Work Item from Workshop records:

- source session id;
- source message ids;
- participants at creation;
- exact document version ids used;
- inspected source references where available;
- open questions at creation;
- creator user id;
- initial contract version;
- provenance hash.

If source messages or documents are later edited, removed from visibility, or superseded, the Work Item creation provenance remains historically true. Permission checks still control whether current viewers may open the source material.

## Authority Boundaries

The Work Item aggregate does not weaken any existing gate.

The backend must still verify:

- project membership and tenant access;
- project readiness;
- build/test/run gates;
- critic independence;
- accepted approval actor and exact target hash;
- continuation eligibility;
- apply dry-run and source-apply gate evidence;
- workspace boundary and rollback evidence;
- stale write version.

The Work Item may group these facts for the user. It cannot satisfy them by existing.

## API Shape Direction

V25-01/V25-02 should converge on these read surfaces:

```text
GET /api/projects/{projectId}/work-items
GET /api/projects/{projectId}/work-items/{workItemId}
GET /api/projects/{projectId}/work-items/{workItemId}/contracts
GET /api/projects/{projectId}/work-items/{workItemId}/runs
GET /api/projects/{projectId}/work-items/{workItemId}/audit
```

Existing ticket endpoints may remain for compatibility:

```text
GET /api/projects/{projectId}/tickets
POST /api/projects/{projectId}/tickets
POST /api/projects/{projectId}/tickets/legacy
```

Compatibility endpoints must return enough information for the client to navigate to the canonical Work Item route.

## UI Compatibility

The current primary route remains:

```text
/projects/{projectId}/work-items/{workItemId}
```

V25-00 does not require visible UI change.

V25-01/V25-02 must avoid a split where:

- Board points at Work Items;
- Workshop creates tickets;
- Work Item detail reads tickets;
- runs point somewhere else.

There must be one canonical Work Item identity returned by the backend.

## Audit Impact

The unified audit ledger should add durable Work Item identity once V25-01 lands.

Audit must remain read-only:

- no approval;
- no continuation;
- no apply;
- no export authority unless separately implemented;
- no raw secret or prompt payload exposure.

Audit entries may link to contract versions, run attempts, approval records, apply receipts, Workshop messages, document versions, and membership changes.

## Test Requirements

V25-01/V25-02 must add tests for:

- every existing ticket resolves to a Work Item after migration;
- existing `/work-items/{ticketId}` URLs still work;
- Work Item id cannot cross tenant or project boundaries;
- collaboration moves from ticket-backed to Work Item-backed without losing assignee, followers, waiting-on, or activity;
- latest run and run history are grouped under the Work Item;
- approval remains scoped to exact target id and hash;
- apply retry remains a new attempt under the same Work Item;
- contract revision does not rewrite historical run evidence;
- legacy ticket APIs return canonical Work Item navigation data;
- audit filters by Work Item id;
- deleted tickets do not create active Work Items unless a deliberate archive mapping is specified.

## Documentation Updates Required With V25-01/V25-02

When implementation starts, update:

- [Current Product Capabilities](CURRENT_PRODUCT_CAPABILITIES.md);
- [IronDev v2.5 Product Specification](IRONDEV_PRODUCT_UX_SPEC_V25.md), if schema names change;
- OpenAPI checked-in snapshot and TypeScript generated types;
- manual test contract if new first-user steps are added;
- release or PR receipt for the migration.

## Decisions Locked By V25-00

- Work Item is not a cosmetic rename of `ProjectTicket`.
- Existing identifiers must keep working.
- Ticket becomes contract.
- Run becomes attempt.
- Workshop evidence is provenance, not approval.
- Work Item state is a navigation summary, not gate satisfaction.
- Approval and apply authority stay in existing governed stores.
- Client must use backend canonical identity and must not infer mappings.

## Open Questions For V25-01

- Should the physical table be `dbo.WorkItems` or `dbo.ProjectWorkItems`?
- Should contract rows live in a new table or be introduced by adding Work Item/version fields to `dbo.ProjectTickets` first?
- Which current run store is the first source for `CurrentRunId` in the migration: ticket run records, workflow runs, or the projection's latest-run logic?
- Is `CreatedByUserId` recoverable for all existing tickets, or should unknown creator use a system actor?
- Should deleted tickets become archived Work Items or remain only in legacy ticket history?

These are implementation choices, not product direction gaps. V25-01 must answer them before adding schema.
