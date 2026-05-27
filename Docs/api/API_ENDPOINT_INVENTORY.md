# IronDev.Api Endpoint Inventory

Last reviewed: 2026-05-26

This inventory lists the REST surface currently exposed by `IronDev.Api`. It separates routes that exist today from planned product workflow routes that are still gaps.

## Summary

| Area | Implemented/stubbed endpoints | Planned gaps called out |
|---|---:|---:|
| Health/diagnostics | 1 | 0 |
| Auth/tenants | 5 | 0 |
| Projects | 7 | 1 |
| Tickets | 20 | 1 |
| Documents | 13 | 2 |
| Memory | 20 | 2 |
| Chat | 8 | 0 |
| Profiles | 7 | 0 |
| Runs/reports | 7 | 0 |
| Agents/build workflows | 0 | 4 |
| **Total actual routes** | **88** | **10** |

Status meanings:

- `Implemented`: route calls an API service path and returns real data or a real workflow result.
- `Stubbed`: route exists but intentionally returns a not-implemented or placeholder response.
- `Planned`: product route is needed but does not exist in `IronDev.Api` yet.

Intended consumers:

- `TauriShell`: forward product shell.
- `Product CLI`: public command surface in `tools/IronDev.Cli`.
- `Legacy WPF`: retired `IronDeveloper` WPF client history. It is not an active product consumer.
- `Dogfood Runner`: internal `tools/IronDev.ReplayRunner` and dogfood harnesses.

## Health/Diagnostics

| Method | Route | Controller/action | Purpose | Status | Intended consumers | Notes/gaps |
|---|---|---|---|---|---|---|
| GET | `/health` | `Program.MapGet` | API health probe. | Implemented | TauriShell, Product CLI, Legacy WPF, Dogfood Runner | Product CLI probes through `IIronDevApiClient`. |

## Auth/Tenants

| Method | Route | Controller/action | Purpose | Status | Intended consumers | Notes/gaps |
|---|---|---|---|---|---|---|
| POST | `/api/auth/login` | `AuthController.Login` | Login and receive token/session. | Implemented | TauriShell, Product CLI, Legacy WPF | Product CLI does not expose login yet. |
| GET | `/api/auth/me` | `AuthController.Me` | Current user profile. | Implemented | TauriShell, Legacy WPF | Present in `IronDev.Client`. |
| POST | `/api/auth/logout` | `AuthController.Logout` | End client session. | Implemented | TauriShell, Legacy WPF | Present in `IronDev.Client`. |
| GET | `/api/tenants` | `TenantController.GetTenants` | List selectable tenants. | Implemented | TauriShell, Legacy WPF | Present in `IronDev.Client`. |
| POST | `/api/tenants/select` | `TenantController.SelectTenant` | Select active tenant and issue token. | Implemented | TauriShell, Legacy WPF | Present in `IronDev.Client`. |

## Projects

| Method | Route | Controller/action | Purpose | Status | Intended consumers | Notes/gaps |
|---|---|---|---|---|---|---|
| GET | `/api/projects` | `ProjectsController.GetProjects` | List projects. | Implemented | TauriShell, Product CLI, Legacy WPF | Product CLI command is planned, not present. |
| GET | `/api/projects/{projectId}` | `ProjectsController.GetProject` | Load one project. | Implemented | TauriShell, Legacy WPF | Typed client exists. |
| POST | `/api/projects` | `ProjectsController.CreateProject` | Create project. | Implemented | TauriShell, Product CLI, Legacy WPF | Product CLI command is planned, not present. |
| POST | `/api/projects/{projectId}/select` | `ProjectsController.SelectProject` | Set selected project on session. | Implemented | TauriShell, Legacy WPF | Typed client exists. |
| PUT | `/api/projects/{projectId}/local-path` | `ProjectsController.UpdateLocalPath` | Save local path metadata. | Implemented | Legacy WPF | Desktop-local sensitivity; keep API-owned persistence. |
| POST | `/api/projects/{projectId}/mark-index-stale` | `ProjectsController.MarkIndexStale` | Mark project index stale. | Implemented | Legacy WPF, Dogfood Runner | No product CLI command. |
| GET | `/api/projects/{projectId}/context-pack` | `ProjectsController.ExportContextPack` | Export prompt/context pack. | Implemented | Legacy WPF, Dogfood Runner | Product use needs clarification. |
| GET | `/api/projects/{projectId}/runs` | Planned | List project runs. | Planned | TauriShell, Product CLI | Needed for product run status/report workflows. |

## Tickets

| Method | Route | Controller/action | Purpose | Status | Intended consumers | Notes/gaps |
|---|---|---|---|---|---|---|
| GET | `/api/projects/{projectId}/tickets` | `TicketsController.GetRecentTickets` | List project tickets. | Implemented | TauriShell, Product CLI, Legacy WPF | Product CLI uses `IIronDevApiClient`. |
| GET | `/api/tickets/{ticketId}` | `TicketsController.GetTicketById` | Load ticket by id. | Implemented | Legacy WPF | Absolute ticket lookup. |
| GET | `/api/projects/{projectId}/tickets/{ticketId}` | `TicketsController.GetTicketById` | Load ticket scoped to project route. | Implemented | Product CLI, TauriShell, Legacy WPF | Product CLI uses `IIronDevApiClient`. |
| POST | `/api/projects/{projectId}/tickets` | `TicketsController.CreateTicket` | Create product ticket. | Implemented | Product CLI, TauriShell, Legacy WPF | Product CLI reads local JSON file then calls `IIronDevApiClient`. |
| POST | `/api/projects/{projectId}/tickets/legacy` | `TicketsController.SaveLegacyTicket` | Save legacy ticket model. | Implemented | Legacy WPF | Product clients should prefer create/update product DTOs. |
| POST | `/api/projects/{projectId}/tickets/import-external` | `TicketsController.ImportExternalTicket` | Import external issue as IronDev ticket. | Implemented | Product CLI, Legacy WPF | Product CLI reads local JSON file then calls `IIronDevApiClient`. |
| POST | `/api/projects/{projectId}/tickets/generate-from-discussion` | `TicketsController.GenerateFromDiscussion` | Generate ticket from discussion. | Implemented | Legacy WPF, TauriShell | Missing product CLI command. |
| DELETE | `/api/tickets/{ticketId}` | `TicketsController.ArchiveTicket` | Archive ticket. | Implemented | Legacy WPF, TauriShell | Missing product CLI command. |
| POST | `/api/projects/{projectId}/tickets/draft` | `TicketsController.GenerateDraft` | Generate draft ticket. | Implemented | Legacy WPF, TauriShell | Product workflow route. |
| POST | `/api/projects/{projectId}/tickets/draft/plan` | `TicketsController.GeneratePlan` | Generate/update draft plan. | Implemented | Legacy WPF, TauriShell | Product workflow route. |
| POST | `/api/projects/{projectId}/tickets/draft/tests` | `TicketsController.RegenerateTests` | Generate/update draft tests. | Implemented | Legacy WPF, TauriShell | Product workflow route. |
| POST | `/api/projects/{projectId}/tickets/generate-from-codebase` | `TicketsController.GenerateTicketsFromCodebase` | Generate tickets from codebase. | Implemented | Product CLI, Legacy WPF, Dogfood Runner | Product CLI command is planned, not present. |
| POST | `/api/projects/{projectId}/tickets/{ticketId}/build-preview` | `TicketsController.CreateBuildPreview` | Produce build preview for ticket. | Implemented | Legacy WPF, TauriShell | Does not start a durable run. |
| GET | `/api/projects/{projectId}/tickets/{ticketId}/build-readiness` | `TicketsController.EvaluateReadiness` | Evaluate build readiness. | Implemented | Legacy WPF, TauriShell | Product workflow route. |
| POST | `/api/tickets/{ticketId}/proposal` | `TicketsController.GenerateProposal` | Generate builder proposal for ticket. | Implemented | Legacy WPF, TauriShell | Product workflow route. |
| POST | `/api/projects/{projectId}/proposal` | `TicketsController.GenerateProposalFromRequest` | Generate proposal from request. | Implemented | Legacy WPF, TauriShell | Product workflow route. |
| POST | `/api/projects/{projectId}/proposal/apply` | `TicketsController.ApplyProposal` | Apply proposal. | Implemented | Legacy WPF, TauriShell | Needs run identity if promoted to long-running workflow. |
| POST | `/api/tickets/{ticketId}/apply-and-build` | `TicketsController.ApplyAndBuild` | Apply approved ticket build. | Implemented | Legacy WPF, TauriShell | Returns immediate build result, not `/api/runs/{runId}`. |
| POST | `/api/projects/{projectId}/proposal/validate-architecture` | `TicketsController.ValidateProposalArchitecture` | Validate proposal architecture. | Implemented | Legacy WPF, TauriShell | Product workflow route. |
| POST | `/api/projects/{projectId}/tickets/{ticketId}/build-runs` | `TicketsController.StartBuildRun` | Start ticket build workflow run. | Implemented | Product CLI, TauriShell | Returns workflow run id/status from existing workflow orchestrator; run events are persisted through `SqlRunEventStore`; resumable workflow state is still planned. |
| POST | `/api/projects/{projectId}/documents/{documentVersionId}/tickets` | Planned | Generate tickets from a document version. | Planned | Product CLI, TauriShell | Document-to-ticket smoke exists internally, product route does not. |

## Documents

| Method | Route | Controller/action | Purpose | Status | Intended consumers | Notes/gaps |
|---|---|---|---|---|---|---|
| GET | `/api/projects/{projectId}/documents` | `DocumentsController.GetDocuments` | List project documents. | Implemented | TauriShell, Product CLI, Legacy WPF | Product CLI command is planned, not present. |
| POST | `/api/projects/{projectId}/documents` | `DocumentsController.CreateDocument` | Create document. | Implemented | TauriShell, Product CLI, Legacy WPF | Product CLI command is planned, not present. |
| GET | `/api/documents/{documentId}` | `DocumentsController.GetDocument` | Load document. | Implemented | Legacy WPF, TauriShell | Absolute document lookup. |
| GET | `/api/projects/{projectId}/documents/{documentId}` | `DocumentsController.GetDocument` | Load project document. | Implemented | Legacy WPF, TauriShell | Project-scoped route shares action. |
| PUT | `/api/projects/{projectId}/documents/{documentId}` | `DocumentsController.UpdateDocument` | Update document. | Implemented | Legacy WPF, TauriShell | Client inventory lacks direct update method. |
| POST | `/api/projects/{projectId}/documents/{documentId}/resolve` | `DocumentsController.ResolveDocument` | Resolve document workflow. | Stubbed | TauriShell, Legacy WPF | Returns `not_implemented`. |
| POST | `/api/documents/{documentId}/versions` | `DocumentsController.AddVersion` | Add document version. | Implemented | Legacy WPF, TauriShell | Product CLI command is planned, not present. |
| GET | `/api/documents/{documentId}/versions/current` | `DocumentsController.GetCurrentVersion` | Get current version. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/document-versions/{versionId}` | `DocumentsController.GetVersion` | Load version. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/documents/{documentId}/versions` | `DocumentsController.GetVersionHistory` | Version history. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| POST | `/api/document-versions/{versionId}/links` | `DocumentsController.LinkVersion` | Link document version to target. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/document-versions/{versionId}/links` | `DocumentsController.GetLinksForVersion` | List version links. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| DELETE | `/api/documents/{documentId}` | `DocumentsController.ArchiveDocument` | Archive document. | Implemented | Legacy WPF, TauriShell | Product CLI command is planned, not present. |
| POST | `/api/document-versions/{versionId}/generate-tickets` | Planned | Generate tickets from document version. | Planned | Product CLI, TauriShell | Needed by requested CLI workflow. |
| GET | `/api/projects/{projectId}/documents/{documentId}/resolution` | Planned | Read document resolution workflow state. | Planned | TauriShell, Legacy WPF | Existing resolve endpoint is stubbed. |

## Memory

| Method | Route | Controller/action | Purpose | Status | Intended consumers | Notes/gaps |
|---|---|---|---|---|---|---|
| GET | `/api/projects/{projectId}/memory/summary` | `MemoryController.GetLatestSummary` | Latest project summary. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| POST | `/api/projects/{projectId}/memory/summary` | `MemoryController.SaveSummary` | Save summary. | Implemented | Legacy WPF, Dogfood Runner | Product write path. |
| GET | `/api/projects/{projectId}/memory/decisions` | `MemoryController.GetRecentDecisions` | Recent decisions. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| POST | `/api/projects/{projectId}/memory/decisions` | `MemoryController.SaveDecision` | Save decision. | Implemented | Legacy WPF, Dogfood Runner | Product write path. |
| GET | `/api/projects/{projectId}/memory/documents` | `MemoryController.GetContextDocuments` | List context documents. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/projects/{projectId}/memory/search` | `MemoryController.SearchContextDocuments` | Search context documents. | Implemented | Product CLI, TauriShell, Legacy WPF | Product CLI command is planned, not present. |
| GET | `/api/memory/documents/{documentId}` | `MemoryController.GetContextDocumentById` | Load context document. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| POST | `/api/projects/{projectId}/memory/documents` | `MemoryController.SaveContextDocument` | Save context document. | Implemented | Legacy WPF, Dogfood Runner | Product write path. |
| DELETE | `/api/memory/documents/{documentId}` | `MemoryController.ArchiveContextDocument` | Archive context document. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/projects/{projectId}/memory/plans` | `MemoryController.GetRecentPlans` | Recent implementation plans. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/memory/plans/{planId}` | `MemoryController.GetPlanById` | Load plan by id. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/tickets/{ticketId}/implementation-plan` | `MemoryController.GetPlanByTicketId` | Load plan for ticket. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| POST | `/api/projects/{projectId}/memory/plans` | `MemoryController.SavePlan` | Save implementation plan. | Implemented | Legacy WPF, Dogfood Runner | Product write path. |
| GET | `/api/projects/{projectId}/memory/rules` | `MemoryController.GetProjectRules` | Project memory rules. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| POST | `/api/projects/{projectId}/memory/rules` | `MemoryController.SaveProjectRule` | Save memory rule. | Implemented | Legacy WPF, Dogfood Runner | Product write path. |
| POST | `/api/projects/{projectId}/code-index` | `CodeIndexController.IndexDirectory` | Index local directory. | Implemented | Legacy WPF, Dogfood Runner | Exposes filesystem path in request; product UX needs care. |
| GET | `/api/projects/{projectId}/code-index/file-count` | `CodeIndexController.GetIndexedFileCount` | Indexed file count. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/projects/{projectId}/code-index/files/search` | `CodeIndexController.SearchFiles` | Search indexed files. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/projects/{projectId}/code-index/files/recent` | `CodeIndexController.GetRecentFiles` | Recently indexed files. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/projects/{projectId}/memory/search/snippets` | `CodeIndexController.GetRelevantSnippets` | Relevant code snippets. | Implemented | Legacy WPF, TauriShell | Route lives in `CodeIndexController`. |
| POST | `/api/projects/{projectId}/memory/index-runs` | Planned | Start durable memory indexing run. | Planned | TauriShell, Product CLI | Needed before long indexing can stream run events. |
| GET | `/api/projects/{projectId}/memory/index-runs/{runId}` | Planned | Read memory indexing run status. | Planned | TauriShell, Product CLI | Missing durable run model. |

## Chat

| Method | Route | Controller/action | Purpose | Status | Intended consumers | Notes/gaps |
|---|---|---|---|---|---|---|
| GET | `/api/projects/{projectId}/chat/sessions` | `ChatController.GetRecentSessions` | List chat sessions. | Implemented | TauriShell, Legacy WPF | Typed client exists. |
| GET | `/api/chat/sessions/{sessionId}` | `ChatController.GetSessionById` | Load session. | Implemented | TauriShell, Legacy WPF | Typed client exists. |
| POST | `/api/projects/{projectId}/chat/sessions` | `ChatController.SaveSession` | Save session. | Implemented | TauriShell, Legacy WPF | Typed client exists. |
| DELETE | `/api/chat/sessions/{sessionId}` | `ChatController.DeleteSession` | Delete session. | Implemented | TauriShell, Legacy WPF | Typed client exists. |
| GET | `/api/projects/{projectId}/chat/sessions/{sessionId}/messages` | `ChatController.GetRecentMessages` | List messages. | Implemented | TauriShell, Legacy WPF | Typed client exists. |
| POST | `/api/projects/{projectId}/chat/sessions/{sessionId}/messages` | `ChatController.SaveMessage` | Save message. | Implemented | TauriShell, Legacy WPF | Typed client exists. |
| POST | `/api/projects/{projectId}/chat/complete` | `ChatController.Complete` | Run chat completion. | Implemented | TauriShell, Legacy WPF | Long-running behavior is not represented as `/api/runs`. |
| POST | `/api/projects/{projectId}/chat/feedback` | `ChatController.SaveFeedback` | Save message feedback. | Implemented | TauriShell, Legacy WPF | Typed client exists. |

## Profiles

| Method | Route | Controller/action | Purpose | Status | Intended consumers | Notes/gaps |
|---|---|---|---|---|---|---|
| GET | `/api/projects/{projectId}/profile` | `ProfilesController.GetProjectProfile` | Load project profile. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| POST | `/api/projects/{projectId}/profile` | `ProfilesController.SaveProjectProfile` | Save project profile. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/projects/{projectId}/profile/commands` | `ProfilesController.GetProjectCommands` | List project commands. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| POST | `/api/projects/{projectId}/profile/commands` | `ProfilesController.SaveProjectCommand` | Save project command. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/projects/{projectId}/profile/commands/default/{commandType}` | `ProfilesController.GetDefaultCommand` | Get default command. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| GET | `/api/profile/options/{category}` | `ProfilesController.GetOptionsByCategory` | List profile options. | Implemented | Legacy WPF, TauriShell | Typed client exists. |
| POST | `/api/profile/detect` | `ProfilesController.Detect` | Detect profile from project root. | Implemented | Legacy WPF, Dogfood Runner | Request includes local path. |

## Runs/Reports

| Method | Route | Controller/action | Purpose | Status | Intended consumers | Notes/gaps |
|---|---|---|---|---|---|---|
| GET | `/api/run-reports` | `RunReportsController.GetRecentRuns` | List file-backed run reports. | Implemented | Legacy WPF, Dogfood Runner | This is report inventory, not a durable run status endpoint. |
| GET | `/api/run-reports/{runId}` | `RunReportsController.GetRun` | Read one run report. | Implemented | Legacy WPF, Dogfood Runner | Route is report-shaped, not `/api/runs/{runId}`. |
| GET | `/api/run-reports/{runId}/evidence` | `RunReportsController.GetEvidence` | List report evidence. | Implemented | Legacy WPF, Dogfood Runner | File-backed storage hidden behind API for WPF. |
| GET | `/api/run-reports/{runId}/evidence/text` | `RunReportsController.ReadEvidenceText` | Read evidence text by path query. | Implemented | Legacy WPF, Dogfood Runner | Query still exposes evidence path concept. |
| GET | `/api/runs/{runId}` | `RunsController.GetRun` | Product-shaped run status over durable run state and SQL-backed event history. | Implemented | Product CLI, TauriShell | Run state is canonical; events are child evidence records; file-backed reports are projections/evidence. |
| GET | `/api/runs/{runId}/report` | `RunsController.GetRunReport` | Final run report by run id. | Implemented | Product CLI, TauriShell | Hides `/api/run-reports/*` from product CLI. |
| GET | `/api/runs/{runId}/events` | `RunsController.GetRunEvents` | SSE run event stream. | Implemented | Product CLI, TauriShell | Streams live events backed by SQL event history. Does not synthesize events from file-backed reports. |

## Agents/Build Workflows

No dedicated `AgentsController`, `BuildRunsController`, or durable workflow controller exists today. Build/proposal behavior is currently exposed through ticket/proposal endpoints and internal dogfood commands.

| Method | Route | Controller/action | Purpose | Status | Intended consumers | Notes/gaps |
|---|---|---|---|---|---|---|
| POST | `/api/projects/{projectId}/agent-runs` | Planned | Start an agent workflow run. | Planned | TauriShell, Product CLI | Needed to avoid product clients invoking dogfood commands. |
| GET | `/api/projects/{projectId}/agent-runs/{runId}` | Planned | Read agent workflow run status. | Planned | TauriShell, Product CLI | Should map to durable run model. |
| POST | `/api/projects/{projectId}/build-runs` | Planned | Start project-level build workflow. | Planned | TauriShell, Product CLI | Existing build commands are internal dogfood. |
| POST | `/api/projects/{projectId}/test-runs` | Planned | Start project-level test workflow. | Planned | TauriShell, Product CLI | Existing test plan runner is internal dogfood/replay. |
