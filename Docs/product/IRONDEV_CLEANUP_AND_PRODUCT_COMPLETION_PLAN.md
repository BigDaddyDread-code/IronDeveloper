# IronDev Cleanup and Product Completion Map

**Status:** Canonical cleanup-era product map
**Verified against:** `main` at `0baa88cf`
**Date:** 12 July 2026

## Purpose

This document records what IronDev currently is, which backend and frontend surfaces own each capability, and what cleanup or deferred work remains. It is not a roadmap disguised as current support.

The canonical product information architecture is:

```text
Session and project entry
  -> Board
  -> Workshop
  -> Work Item
  -> Library
       -> Documents
       -> Tools
       -> Members
       -> Governance
       -> Project setup
       -> Audit
       -> Settings
```

## Status Vocabulary

| Status | Meaning |
| --- | --- |
| `Real` | A normal product/API path exists for its stated scope, including material failure or refusal states. |
| `Partial` | Useful current behavior exists, but a named ownership, lifecycle, reliability, or release contract remains incomplete. |
| `LegacyCompat` | Behavior remains supported as substrate or compatibility, but it is not canonical product design. |
| `Broken` | A confirmed defect prevents the stated contract. |
| `Deferred` | Intentionally outside the cleanup-era product scope. |

`Planned501` is not used. It mixes product status with an HTTP transport response. Planned but unavailable behavior is `Deferred`; an exposed route must state its actual refusal or not-implemented contract separately.

No area is described as "mostly done."

## Product Completion Matrix

| Area | Status | Canonical backend owner | Canonical frontend surface | Known defect or boundary | Legacy alternative | Next cleanup slice |
| --- | --- | --- | --- | --- | --- | --- |
| Authentication | `Real` | `IronDev.Api/Controllers/AuthController.cs` and backend token validation | `/sign-in` | Real for LocalTest/developer identity. Production identity and operations are not claimed. | Stored development sessions | CLN-15 proves base-token and tenant-token boundaries. |
| Tenant selection | `Real` | `TenantController`, `TenantUsersController`, selected-tenant token contract | Conditional `/tenants/select` | Zero, one, and many accessible tenants differ. Tenant choice never grants project authority. | Implicit tenant assumptions | CLN-15 verifies membership and cross-tenant refusal. |
| Project selection | `Real` | `ProjectsController` and project membership services | `/projects`, `/projects/connect` | Route project and selected tenant must agree; visible selection is not authorization. | Stored fallback project IDs | CLN-16 performs the project-access sweep. |
| Board | `Real` | `ProjectBoardController`, `IProjectBoardReadService` | `/projects/{projectId}/board` | Backend projection owns readiness, waiting, attention, assignment, and pipeline truth. | Old cockpit/route terminology | CLN-29 locks canonical routes; CLN-31 contains old paths. |
| Work Item | `Partial` | `ProjectWorkItemsController`, `IProjectWorkItemReadService`, ticket/run services | `/projects/{projectId}/work-items/{id}` | Product spine is real, but identity still rides the ticket substrate rather than a durable Work Item aggregate. | Ticket detail as product identity | Durable Work Item migration remains post-cleanup V2.5 completion work. |
| Chat / Workshop | `Real` | `ChatController`, project channel/session/member services | `/projects/{projectId}/workshop` | Direct IronDev sessions and shared channels are real. Realtime presence is not claimed. | `/chat` compatibility URLs and Chat naming | CLN-29 and CLN-31 formalize canonical and compatibility routes. |
| BA shaping | `Real` | Chat mode, clarification, context pipeline, and ticket-draft services | Workshop conversation and ticket-draft review | Exploration, formalization, clarification, and reviewed ticket handoff exist. No autonomous analyst authority is implied. | Prompt-shaped draft flows | CLN-30 consolidates shared refusal and stale-state rendering. |
| Tickets | `LegacyCompat` | `TicketsController` and ticket persistence | Reached through Work Item and Workshop draft review | Tickets remain durable substrate and compatibility API, not primary navigation or final Work Item identity. | `/tickets`, legacy ticket forms | Preserve until durable Work Item migration proves usage can be removed. |
| Provisioning | `Real` | `ProvisioningController`, project profile/command services | `/projects/{projectId}/setup` and Library Project setup | Backend stores and evaluates repo, command, profile, and structure evidence. | Direct profile/command repair APIs | CLN-19 checks that no request path silently owns core schema creation. |
| Readiness | `Real` | `IProjectProvisioningReadinessService` and Board projection | Project chooser badges, setup, Board header | Backend truth or explicit unavailable state; the client cannot infer Ready. | Frontend-derived readiness | CLN-35 removes any remaining duplicate readiness calculations. |
| Code index | `Real` | `CodeIndexController` and indexing services | Library Explorer and setup/readiness rows | Indexes are project-scoped derived state, not authority or durable canon. | Direct index diagnostics | CLN-39 maps health; CLN-27 later owns memory-index lifecycle only. |
| Runs | `Real` | `RunsController`, `AgentRunsV1Controller`, workflow/run stores | Work Item Build/Review and technical evidence | Bounded execution, lifecycle, events, and evidence exist. A run never grants approval. | Legacy run viewers and `/runs` compatibility | CLN-12 audits actor/correlation attribution. |
| Repair | `Real` | Ticket skeleton repair and governed loop services | Work Item Build recovery | Attempt budgets and new evidence/package hashes are required. | Manual retry endpoints | CLN-13 aligns governed refusal envelopes. |
| Revision | `Real` | Finding-driven revision services in `TicketsController` | Work Item Review/Build | Revision responds to cited findings and creates a fresh package; it is not approval. | Ad hoc ticket edits | CLN-12 verifies initiating actor and causation. |
| Critic review | `Real` | `ManualCriticReviewsV1Controller` and critic review services | Work Item Review and technical evidence | Critic output is evidence and may require human review. | Read-only critic viewer routes | CLN-18 checks redaction and sensitive material. |
| Findings | `Real` | Critic packages and finding stores | Work Item Review | Findings are attributable evidence, not veto or approval by themselves. | Historical report-only viewers | CLN-13 standardizes refusal/blocked reasoning where governed actions consume findings. |
| Dispositions | `Real` | Finding-disposition service through `TicketsController` | Work Item Review | Human reasoned disposition removes a finding blockage only. | Old disposition vocabulary | CLN-06 deprecates `FixRequired`, `RejectFinding`, and `DeferFix` terminology. |
| Approval | `Real` | `AcceptedApprovalsV1Controller`, approval stores/evaluators | Work Item Review and Governance evidence | Actor, target, capability, expiry, and package hash are backend-bound. Approval does not continue work. | Historical approval viewers | CLN-12 and CLN-15 verify actor and tenant scope. |
| Continuation | `Real` | `GovernedWorkflowContinuationController`, continuation gate/store | Work Item primary action | Backend rechecks live approval, policy, evidence, and current run state. | Generic continue endpoints | CLN-13 aligns refusal and next-safe-action contracts. |
| Apply | `Real` | Apply preview, controlled worktree apply services, source-apply stores | Work Item Review/Done and Governance | Real only for configured sandbox/copy or isolated non-main worktree apply. Commit, push, merge, release, and deploy are separate and not product authority. Rollback execution remains excluded. | Dry-run-only and historical apply viewers | CLN-16/18 sweep scope and sensitive evidence; rollback stays explicitly deferred. |
| Reports | `Partial` | `RunReportsController`, diagnosis, health, governance and evidence read services | Work Item evidence, Governance technical evidence, Audit | Run/evidence reports exist; general analytics, compliance reports, and bounded support bundle are absent. | Numerous legacy read-only viewers | CLN-41 defines the bounded support bundle; CLN-31 contains legacy viewers. |
| Governance | `Real` | `ProjectGovernanceController`, `IProjectGovernanceOverviewService`, governed action kernel | `/projects/{projectId}/library/governance` | Project posture, controls, attention, exceptions, decisions, and technical evidence are backend-owned and read-only where presented. | `/governance/*` technical viewers | CLN-31 keeps compatibility read-only and points to canonical Governance. |
| Audit | `Real` | `AuditLedgerController`, `ProjectAuditExportController`, audit services | `/projects/{projectId}/library/audit` | Filtered ledger, safe detail links, bounded JSON export, truncation and hash truth exist. Audit grants no authority. | Individual audit/report viewers | CLN-17 extends safe-link proof across all evidence surfaces. |
| Settings | `Partial` | `AgentProfilesController`, `AiConnectionsController`, project configuration APIs | Project Library Settings | Agent/AI/safety configuration works, but settings ownership and information architecture remain split. | Separate profile/configuration pages | CLN-30/33 consolidate states and accessibility before any redesign. |
| Agent profiles | `Real` | `AgentProfilesController`, profile/version and model resolution services | Settings Agents and AI connections | Versioned tenant defaults/project overrides, provenance, reset and controlled credential paths exist. Profiles never self-grant authority. | File/default profile configuration | CLN-18 reviews credential/log storage; CLN-12 checks actor attribution. |
| Memory | `Partial` | `MemoryController`, proposal/promotion/index services, SQL and derived retrieval stores | No canonical primary memory product surface; selected read/review evidence surfaces | Multiple memory generations coexist. Current write, promotion, retrieval, and index paths require classification before new intelligence. | Semantic memory, project memory map, agent-local and collective-memory paths | CLN-23 reality audit, CLN-24 write lock, and CLN-26 retrieval security. |
| Hosted workspaces | `Deferred` | No canonical hosted-workspace service | No product route | Local disposable and isolated worktrees exist; hosted lifecycle, tenancy, quota, retention, and operations do not. | Local workspace folders | Remains out of scope until cleanup exit and an explicit hosted contract. |
| CI | `Partial` | Seven GitHub workflows and `Scripts/ci` lane owners | GitHub pull-request checks and retained evidence | Required current-product lanes are green. Remaining integration and historical/component Playwright ownership is explicitly unclassified. | Developer-only broad test commands | CLN-08 assigns every suite; no uncovered suite is called green. |
| Release | `Partial` | Release/readiness evidence services and manual qualification contracts | Governance evidence plus documented operator journey | Local technical pilot evidence exists. Clean-clone, upgrade, and non-author operator gates remain cleanup exit requirements. Product commit/push/merge/release/deploy authority is not implemented. | Historical alpha receipts | CLN-42, CLN-43, and CLN-44 close qualification. |
| Documentation | `Partial` | Repository Markdown and generated API artifacts | Product/architecture/operations guidance | Current product truth exists, but competing active-looking designs and stale terminology remain. Historical receipts must not be rewritten. | Old roadmaps, milestone specs, next-branch instructions | CLN-03 creates the architecture index; CLN-04 performs the full document inventory. |

## Confirmed Broken Areas

No product area is classified `Broken` at this baseline. That is not a claim that no defect exists. A confirmed cleanup finding changes the affected row to `Broken` until its narrow remediation and evidence merge.

## Canonical Owners

- Runtime product state: backend APIs and SQL-backed stores for the contract they own.
- Derived retrieval/index state: rebuildable and non-authoritative.
- Product navigation and display: Tauri shell, consuming backend truth.
- Historical evidence: immutable receipts and release artifacts.
- Current support claims: this map plus `CURRENT_PRODUCT_CAPABILITIES.md`.

## Review Line

An implemented substrate is not automatically a canonical product surface, and a polished product surface is not backend authority.

## Killjoy Line

If an area cannot name its owner, route, defect, and next proof, its completion status is unknown.
