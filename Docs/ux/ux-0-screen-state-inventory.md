# UX-0 — Screen/State Inventory Against Backend Truth

**Status:** Committed inventory (UX-0)
**Purpose:** map every surface in `full-ux-map.md` to its existing frontend route/component and
backing API endpoint, with an honest status. This is the work-list for AFFORDANCE-1, NAV-1,
and every later UX slice — and the proof of what is already real.

**Sources:** `IronDev.TauriShell/src` (routes, features, design-system) and a survey of all
41 controllers in `IronDev.Api/Controllers` (2026-07-07). Rows marked *survey-partial* had
action lists redacted/abbreviated in the survey and need verification before the owning slice
starts.

**Status vocabulary:**

```text
Ready       frontend + endpoint exist and align with the map
Partial     one side exists, or exists in a different shape than the map targets
Missing     neither side exists — ships as honest 501 route until its slice
Reshape     exists but the map moves/renames it (work is re-homing, not building)
```

---

## 1. Headline Findings

```text
1. The affordance envelope ALREADY EXISTS in embryo. ControlledActionRequestCreateResponse
   (FrontendControlledActionRequestModels.cs) carries BlockedReasons[], MissingEvidence[],
   NextSafeActions[], ForbiddenActions[], and FrontendActionRequestBoundary capability flags
   (CanApprove, CanExecute, CanMutateSource, CanContinueWorkflow, ...). AFFORDANCE-1 is a
   GENERALIZATION of this structure to every screen, not an invention.
2. No endpoint returns 501 today. The 501-first pattern needs the stub-controller convention
   introduced in AFFORDANCE-1 and used from NAV-1 onward.
3. Runs already stream: GET api/runs/{runId}/events is SSE. The "poll-shaped API" assumption
   in flow-first-ux-spec.md is stale — the Build stage can be live.
4. The governed spine is API-complete: start/continue/revise/apply skeleton runs, critic
   package + review, finding dispositions, accepted approvals, apply preview, receipts,
   reports. The gap between backend and cockpit is presentation, not capability.
5. Batch endpoints exist (batch-maps, batch-plans, batch-runs) — the Board's future
   sequencing layer has backend truth waiting.
6. Tenant/user/auth endpoints exist (login/JWT, tenant list/select, tenant users CRUD) —
   TEAM-0 is closer than assumed, but A12 scope-proof and the F-matrix remain the gate.
7. ChatGovernanceGate (chat) + UiAuthorityFirewall (governance) are two frontend authority
   filters that AFFORDANCE-1 should unify under the one envelope renderer.
```

---

## 2. Global Chrome

| Map element | Frontend today | Backend today | Status |
|---|---|---|---|
| Truth Strip (source + refreshed + corr id) | `ApiStatusBadge`, ad-hoc | correlation ids exist in responses | Partial |
| Preflight (named remedies) | none (doctor is CLI-side) | `api/v1/operations/health` (+ backend/dependencies), `projects/{id}/services/status` | Partial |
| Attention queue (waiting on you/others) | none | derivable from runs/approvals queries; no dedicated endpoint | Missing |
| Command bar | `CommandBar` (design-system) | n/a | Ready |
| Identity + role lens | auth exists (`SignInRoute`, JWT) | `api/auth/login`, `api/tenants`, `api/tenants/{id}/users` | Partial |

## 3. The Board

| Map element | Frontend today | Backend today | Status |
|---|---|---|---|
| Board surface (pipeline columns) | none — `HomeRoute` is a workspace home | ticket list + run summaries (`api/projects/{id}/tickets`, `api/run-reports`) | Missing (frontend) |
| Card contract (state, assignee, repair, lease chips) | `WorkspaceListItem` partial precedent | run report has repair/attempt data; no assignee/lease fields | Partial |
| Batch sequencing (future) | none | `batch-maps/{mapId}`, `batch-plans`, `batch-runs` | Partial (backend ahead) |

## 4. Work Item Spine

| Stage / element | Frontend today | Backend today | Status |
|---|---|---|---|
| Stage rail + gate locks | `FlowStageRail` (chatToBuild) — precedent, not spine-wide | gate truth split across readiness/critic/approval endpoints | Partial |
| Shape (chat as tool) | `ChatRoute`, `ChatWorkspace`, `ChatGovernanceGate`, suggested actions | chat sessions CRUD, mode-aware completion, turn audit | Ready (Reshape into spine) |
| Shape → draft ticket | `GeneratedTicketPanel`, `DiscussionComposer` | `tickets/draft`, `draft/confirm`, `draft/plan`, discussions → tickets | Ready (Reshape) |
| Ticket (frozen contract + readiness) | `TicketDetail`, `TicketEditForm`, `TicketsWorkspace` | ticket CRUD, import-external, build-readiness (verify route survives) | Ready (Reshape) |
| Build — run cockpit header/timeline | `RunEventTimeline`, `DisposableRunPanel` | `runs/{id}` + `runs/{id}/events` (SSE), skeleton-run start | Partial |
| Build — repair panel (REPAIR-2) | none | repair attempts in run report (post-#736 revise/repair models) | Missing (frontend) |
| Review — critic package | `RunReviewPackagePanel` | `skeleton-runs/{runId}/critic-review` (POST), critic package GET | Partial |
| Review — findings ledger + dispositions | `TicketRunReviewPanel`, `HumanReviewChecklist` | `findings/{findingId}/disposition` (POST); revise loop (#736) | Partial |
| Review — human gate (eligibility, self-approval) | `AcceptedApprovalPanel` | accepted-approvals V1 (*survey-partial*); no eligibility/actor computation | Partial |
| Done — apply preflight + stages | `SourceApplyReviewPanel`, `SourceApplyDryRunReceiptPanel` | `skeleton-runs/{runId}/apply`, apply-preview, dry-run receipts, rollback receipts | Partial |
| Done — final report | `RunReportsRoute`, `useRunReportsWorkspace` | `api/run-reports/{runId}` + evidence + evidence/text | Ready (Reshape) |
| Contract rail (criteria → test → finding → receipt) | `CodeStandardsSummary`, chips scattered | criteria on ticket; coverage in critic package | Partial |

## 5. Gates (backend truth per lock)

| Gate | Condition source today | Status |
|---|---|---|
| Shape → Ticket | draft confirm validation (`tickets/draft/confirm`) | Partial — refusal reasons exist, not envelope-shaped |
| Ticket → Build | build-readiness + start-run validation | Partial |
| Build → Review | critic package ready event + hash | Ready (event exists) |
| Review → Done | continuation refusals (review missing / undispositioned / approval / hash / stale) | Ready (backend) — needs envelope surfacing |
| Apply eligibility | apply validation + mutation lease + preview | Partial |

## 6. The Library

| Section | Frontend today | Backend today | Status |
|---|---|---|---|
| Projects list/select | `ProjectSelector` | projects CRUD, select, local-path | Ready |
| Provisioning wizard (PROJECT-0..3) | none | `profile` GET/POST, profile commands, options — no scan/readiness/decision-record | Missing (both, partly) |
| Solution explorer | none (`KnowledgeRoute` adjacent) | code-index: files, search, recent, file-count, snippets | Partial (needs list UI) |
| Documents / decisions / ADRs | `KnowledgeRoute`, Documents/Decisions clients | documents CRUD+versions, decisions CRUD, memory search | Ready (Reshape) |
| Governance viewers (17) | all exist under `features/governance` | matching V1 endpoints (receipts, patches, policies, tool gates, transitions) | Ready (Reshape as drill-downs) |
| Reports archive | `RunReportsRoute` | `api/run-reports` (recent) | Ready (Reshape) |
| Audit ledger | `GovernanceTimelineRoute`, `AgentRunAudit` clients | governance traces search, agent-run audit + thought ledger, workflow read-only | Partial (unify into one ledger view) |
| Admin — users/roles | none | `tenants/{id}/users` CRUD (*survey-partial*); no invite flow, no role matrix | Partial (backend) / Missing (frontend) |
| Settings four-way split | `SettingsRoute` (flat) | profile/commands, agent-profiles (model per role) | Partial |
| Human-intervention dial (9.6) | none | none (AUTH-0 machinery) | Missing — Level 0 real, 1–3 as 501 |

## 7. Integrations (post-TEAM-0)

| Element | Backend today | Status |
|---|---|---|
| ADO import | `ImportExternalTicket` exists on TicketsController | Partial (generic import, no ADO adapter) |
| ADO status mirror | none | Missing |
| Slack digest | none | Missing |

---

## 8. Consequences for the Next Slices

```text
AFFORDANCE-1  Generalize FrontendActionRequestBoundary + NextSafeActions into ONE envelope
              contract used by every screen; unify ChatGovernanceGate + UiAuthorityFirewall
              behind one renderer; introduce the 501 stub-controller convention; prove on
              the Runs surface. (Smaller than planned — the backend shape exists.)
NAV-1         New honest-501 routes needed: Board, Projects/provisioning, Admin, Audit
              (unified), Dial. Everything else is Reshape, not build.
REPAIR-2      Pure frontend: render repair attempts already present in the run report.
FLOW SPINE    The largest frontend work is the Work Item spine itself (stage rail + contract
              rail + re-homing chat/build/review panels onto it) — Reshape-heavy, per the
              flow-first component inventory (tear down 8-tab shell).
STALE NOTE    flow-first-ux-spec.md "poll-shaped" caveat is outdated: run events are SSE.
```

Killjoy line:

```text
The backend is ahead of the cockpit. The work is making truth visible, not making truth.
```

---

## 9. Correction (AFFORDANCE-1 PR)

The original survey read `src/features/` and missed `src/flow/` — the flow-first shell is
already substantially built. Corrected statuses:

```text
Board surface              Ready — flow/board/BoardScreen.tsx (was: Missing frontend)
Work item spine            Ready — flow/workitem/WorkItemScreen + Build/Review stages,
                           StageRail, ContractRail (was: Partial)
Repair panel (REPAIR-2)    Ready — flow/workitem/RepairAttemptsPanel.tsx renders
                           report.repairAttempts with the honest boundary (was: Missing)
Critic package/findings    Ready — CriticPackageViewer, FindingsPanel, ApprovalGate exist
Batch surface              Ready — flow/batch/BatchScreen.tsx
Library                    Ready — SolutionExplorer + GovernanceHost re-homed
Settings                   Partial — users/roles live against tenant API; approval policy
                           (the intervention dial) is an honestly-labeled LOCAL DRAFT with
                           backend invariants locked; backend contract is AUTH-0
```

What this PR added on top: the `PlannedSurfaceEnvelope` + honest-501 convention
(`PlannedSurfacesController`), the shared `NotImplementedPanel` (universal state 6), Library
sections for Provisioning/Audit/Admin-invite probing real 501 routes, and the dial's backend
probe in Settings. REPAIR-2 required no code — it was already built and wired.

Lesson recorded: inventory against ALL frontend roots before declaring anything missing.
