# Current Product Capabilities

**Status:** Current product truth  
**Verified against:** `main` after API-CONTRACT-1  
**Last reviewed:** 11 July 2026

This matrix describes the capability a user can reach through the current API and React/Tauri product. It is not a roadmap and does not infer support from planned designs or historical receipts.

## Status vocabulary

| Status | Meaning |
| --- | --- |
| Supported | The normal route and backend contract exist, including material empty, blocked, and failure states. |
| Partial | Useful behavior exists, but the named product capability is not complete. |
| Not implemented | The product refuses honestly or has no route. |
| Unavailable | A temporary runtime failure, not a capability statement. |

## Entry and project context

| Capability | Status | Current route or contract | Boundary |
| --- | --- | --- | --- |
| Sign in | Supported | `/sign-in`, `POST /api/auth/login` | The client stores a session token; API authorization remains authoritative. |
| Conditional tenant choice | Supported | `/tenants/select`, tenant list/select API | Zero, one, and many accessible tenants have distinct entry behavior. |
| Project chooser | Supported | `/projects`, projects API | Project IDs are selected through visible product navigation. |
| Connect a local repository | Supported | `/projects/connect`, project create/update API | The backend records and evaluates the supplied path. |
| Guided project setup | Supported | `/projects/{id}/setup`, provisioning readiness/profile/commands APIs | Readiness, evidence, blockers, and remedies come from the backend. |

## Primary navigation

| Surface | Status | Current behavior | Known next contract |
| --- | --- | --- | --- |
| Board | Supported | Readiness header, one priority action, attention items, stage columns, and run queue. | Dedicated backend Board read model for richer waiting, assignment, and run summaries. |
| Chat | Supported | Direct IronDev sessions, project channels, persisted messages, explicit attributed `@IronDev` turns, person mentions, in-product notifications, durable unread markers, effective notification level, source/document context, reply context, and ticket-draft handoff. | Realtime presence and complete concurrency handling. |
| Work Item | Supported | Shape, Ticket, Build, Review, and Done stages consume a dedicated backend projection for lifecycle, gate, contract, collaboration, action, failed-apply recovery, execution proof, and evidence truth. | Assignment, follower, and attributed collaboration data remain empty until backend records exist. |
| Library | Supported | Explorer, Documents, Tools, Members, Governance, Project setup, Audit, and Settings are live. | Audit export and analytics are not implemented. |

## Governed work

| Capability | Status | Truth boundary |
| --- | --- | --- |
| Create and edit tickets | Supported | Backend validation owns persistence and readiness. |
| Start governed runs | Supported | Project readiness and run-start gates may refuse. |
| Execute bounded build/test work | Supported | Execution occurs in configured disposable workspaces with captured evidence. |
| Bounded repair and human-directed revision | Supported | Attempt budgets, findings, and fresh package hashes are preserved. |
| Critic review and finding dispositions | Supported | Review is evidence; each material finding requires a durable human disposition before continuation. |
| Accepted approval | Supported | Approval is scoped to actor, target, capability, and exact package hash. |
| Continue a halted run | Supported | The backend rechecks live approval and evidence; approval does not continue a run by itself. |
| Apply an approved run | Supported with explicit configuration | The skeleton path is sandbox-only and copy-based; controlled worktree apply writes only to an isolated non-main worktree after dry-run and approval checks. |
| Commit, push, merge, release, or deploy from the product | Not implemented | Apply receipts do not imply any of these authorities. |
| Autonomous direct mutation of the active repository | Not implemented | Agents cannot grant themselves mutation or approval authority. |

## Library and collaboration

| Capability | Status | Truth boundary |
| --- | --- | --- |
| Solution explorer and indexed source lookup | Supported | Retrieval is project-scoped and is not authority. |
| Document upload, processing, detail, and immutable versions | Supported | Exact document/version provenance is retained. |
| Attach document versions to Chat context | Supported | The backend returns the sources actually used. |
| Governed tool catalogue and detail | Supported read-only | Declared scope, callers, evidence, health, and connection state are visible. |
| General tool connection setup and invocation from the product | Not implemented | A catalogue entry is not executable permission. |
| Tenant-user administration | Supported | Tenant role and account state are backend-owned. |
| Channel member administration | Supported | Channel role, visibility, and notification level are backend-owned. |
| Project-specific membership | Not implemented | Current project visibility is tenant-scoped; the member directory reports this explicitly. |
| Invite/pending/accept membership lifecycle | Not implemented | Direct user administration does not imply invitations exist. |
| Shared project channels | Supported | Messages, membership, mentions, unread markers, effective notification levels, and the in-product notification inbox are durable; realtime presence is not claimed. |
| Unified audit ledger | Supported | Read-only project traceability spans run events, approvals, work item activity, chat, documents, versions, and membership changes with actor, outcome, correlation ID, filters, and evidence links. It does not approve, continue, apply, export, or grant authority. |

## Hosting posture

| Posture | Status | Meaning |
| --- | --- | --- |
| Local development | Supported | Developer-configured API, SQL, client, and optional provider/index services. |
| LocalTest | Supported for deterministic product and PR testing | Isolated test database and seeded local identity; not production data. |
| Multi-user domain contracts | Supported | Tenants, members, channels, attribution, and authority checks exist. |
| Production shared-host service | Not implemented | No published production deployment, identity, operations, backup, SLO, or security-operations contract. |

## Navigation contract

```text
/sign-in
/tenants/select                 only when a choice is required
/projects
/projects/connect
/projects/{projectId}/setup
/projects/{projectId}/board
/projects/{projectId}/chat
/projects/{projectId}/chat/sessions/{sessionId}
/projects/{projectId}/chat/channels/{channelReference}
/projects/{projectId}/work-items/{workItemId|new}
/projects/{projectId}/library
/projects/{projectId}/library/{documents|tools|members|governance|provisioning|audit|settings}
```

Compatibility URLs may redirect into this project-scoped model. They are not a second information architecture.
