# IronDev.Client Method Inventory

Last reviewed: 2026-05-26

This inventory documents the typed API client surface in `IronDev.Client`. The target boundary is:

```text
TauriShell / Product CLI / Future Clients
  -> IronDev.Client
    -> IronDev.Api
```

Current evidence is mixed: `IronDev.Client` has a broad typed HTTP surface and no `IronDev.Infrastructure` project reference, and `tools/IronDev.Cli` now routes its current product ticket and run commands through `IIronDevApiClient`. Ticket build run starting exists through the API/client boundary, but durable run persistence, document-to-ticket generation, and several product CLI commands remain missing.

## Summary

| Client area | HTTP-backed methods | Status |
|---|---:|---|
| Product facade `IIronDevApiClient` | 14 | Implemented for health/auth/current Product CLI ticket/build and run status/report/event operations; overlaps with narrower typed clients |
| Auth `IAuthApiClient` | 5 | Implemented |
| Projects | 7 | Implemented |
| Tickets/build/proposals | 21 | Implemented |
| Documents | 10 | Implemented, missing update/resolve wrappers |
| Memory | 15 | Implemented |
| Code index | 5 | Implemented |
| Chat | 8 | Implemented |
| Profiles | 7 | Implemented |
| Run reports | 7 | Implemented report API plus product-shaped run status/report/event methods |
| Settings | 0 | Stubbed/no-op |
| Traces | 0 | In-memory local client, not HTTP |
| Prompting | 3 | Client boundary adapter over chat/prompt services, not direct REST methods |

HTTP-backed typed operation count, excluding the overlapping product facade: **85**.

## Consumer Evidence

| Consumer | Current state |
|---|---|
| `IronDeveloper` WPF | Retired and removed; historical WPF-oriented client methods remain in inventory until the public client surface is collapsed. |
| `tools/IronDev.Cli` | References `IronDev.Client` and uses `IIronDevApiClient` for current product ticket commands. |
| `IronDev.TauriShell` | Uses generated OpenAPI TypeScript types and browser fetch helpers, not the C# `IronDev.Client`. It does not reference Infrastructure. |
| `tools/IronDev.ReplayRunner` | Internal dogfood runner; not expected to use `IronDev.Client` for every diagnostic path. |

## Auth Facade

`IIronDevApiClient`/`IronDevApiClient` is a product facade for health/auth and current Product CLI ticket commands. It overlaps with `IAuthApiClient` and `ITicketsApiClient`; that overlap should be collapsed once the single public client contract is finalized.

| Client method | HTTP method | API route | Request DTO | Response DTO | Current consumers | Status |
|---|---|---|---|---|---|---|
| `CheckHealthAsync` | GET | `/health` | None | `bool` | Legacy/general | Implemented |
| `LoginAsync` | POST | `/api/auth/login` | `LoginRequest` | `LoginResponse` | Legacy/general | Implemented |
| `GetCurrentUserAsync` | GET | `/api/auth/me` | None | `UserProfileDto` | Legacy/general | Implemented |
| `GetTenantsAsync` | GET | `/api/tenants` | None | `IReadOnlyList<TenantDto>` | Legacy/general | Implemented |
| `SelectTenantAsync` | POST | `/api/tenants/select` | `SelectTenantRequest` | `LoginResponse` | Legacy/general | Implemented |
| `LogoutAsync` | POST | `/api/auth/logout` | None | None | Legacy/general | Implemented |
| `CreateTicketAsync` | POST | `/api/projects/{projectId}/tickets` | `CreateProjectTicketRequest` | `ProjectTicket` | Product CLI | Implemented |
| `GetTicketsAsync` | GET | `/api/projects/{projectId}/tickets?take={take}` | None | `IReadOnlyList<ProjectTicket>` | Product CLI | Implemented |
| `GetProjectTicketAsync` | GET | `/api/projects/{projectId}/tickets/{ticketId}` | None | `ProjectTicket?` | Product CLI | Implemented |
| `ImportExternalTicketAsync` | POST | `/api/projects/{projectId}/tickets/import-external` | `ImportExternalTicketRequest` | `ProjectTicket` | Product CLI | Implemented |
| `StartTicketBuildRunAsync` | POST | `/api/projects/{projectId}/tickets/{ticketId}/build-runs` | `StartTicketBuildRunRequest` | `TicketBuildRunDto` | Product CLI | Implemented |
| `GetRunAsync` | GET | `/api/runs/{runId}` | None | `RunStatusDto` | Product CLI | Implemented |
| `GetRunReportAsync` | GET | `/api/runs/{runId}/report` | None | `RunReportDto` | Product CLI | Implemented |
| `StreamRunEventsAsync` | GET | `/api/runs/{runId}/events` | None | `IAsyncEnumerable<RunEventDto>` | Product CLI | Implemented; live SQL-backed stream, no report-snapshot synthesis |

## Auth

| Client method | HTTP method | API route | Request DTO | Response DTO | Current consumers | Status |
|---|---|---|---|---|---|---|
| `LoginAsync` | POST | `/api/auth/login` | `LoginRequest` | `LoginResponse` | Retired WPF history / future shells | Implemented |
| `GetTenantsAsync` | GET | `/api/tenants` | None | `IReadOnlyList<TenantDto>` | Retired WPF history / future shells | Implemented |
| `SelectTenantAsync` | POST | `/api/tenants/select` | `SelectTenantRequest` | `LoginResponse` | Retired WPF history / future shells | Implemented |
| `GetCurrentUserAsync` | GET | `/api/auth/me` | None | `UserProfileDto` | Retired WPF history / future shells | Implemented |
| `LogoutAsync` | POST | `/api/auth/logout` | None | None | Retired WPF history / future shells | Implemented |

## Projects

| Client method | HTTP method | API route | Request DTO | Response DTO | Current consumers | Status |
|---|---|---|---|---|---|---|
| `CreateProjectAsync` | POST | `/api/projects` | `Project` | `int` | Retired WPF history | Implemented |
| `GetProjectsAsync` | GET | `/api/projects` | None | `IReadOnlyList<Project>` | Retired WPF history | Implemented |
| `GetByIdAsync` | GET | `/api/projects/{projectId}` | None | `Project?` | Retired WPF history | Implemented |
| `UpdateLocalPathAsync` | PUT | `/api/projects/{projectId}/local-path` | `{ localPath }` | None | Retired WPF history | Implemented |
| `MarkIndexStaleAsync` | POST | `/api/projects/{projectId}/mark-index-stale` | `{ reason }` | None | Retired WPF history | Implemented |
| `SelectProjectAsync` | POST | `/api/projects/{projectId}/select` | None | None | Retired WPF history | Implemented |
| `ExportProjectContextPackAsync` | GET | `/api/projects/{projectId}/context-pack` | None | `string` | Retired WPF history / dogfood | Implemented |

## Tickets, Build, And Proposals

`ITicketsApiClient` also implements the core boundary service interfaces `IDraftTicketService`, `ITicketBuildOrchestrator`, `IBuilderReadinessService`, and `IBuilderProposalService`.

| Client method | HTTP method | API route | Request DTO | Response DTO | Current consumers | Status |
|---|---|---|---|---|---|---|
| `CreateTicketAsync` | POST | `/api/projects/{projectId}/tickets` | `CreateProjectTicketRequest` | `ProjectTicket` | Retired WPF history; Product CLI uses | Implemented |
| `ImportExternalTicketAsync` | POST | `/api/projects/{projectId}/tickets/import-external` | `ImportExternalTicketRequest` | `ProjectTicket` | Retired WPF history; Product CLI uses | Implemented |
| `GenerateTicketFromDiscussionAsync` | POST | `/api/projects/{projectId}/tickets/generate-from-discussion` | `GenerateTicketFromDiscussionRequest` | `ProjectTicket` | Retired WPF history | Implemented |
| `SaveTicketAsync` | POST | `/api/projects/{projectId}/tickets/legacy` | `ProjectTicket` | `long` | Retired WPF history | Implemented |
| `GetRecentTicketsAsync` | GET | `/api/projects/{projectId}/tickets?take={take}` | None | `IReadOnlyList<ProjectTicket>` | Retired WPF history; Product CLI uses | Implemented |
| `GetTicketByIdAsync` | GET | `/api/tickets/{ticketId}` | None | `ProjectTicket?` | Retired WPF history | Implemented |
| `ArchiveTicketAsync` | DELETE | `/api/tickets/{ticketId}` | None | `bool` | Retired WPF history | Implemented |
| `GenerateTicketsAsync` | POST | `/api/projects/{projectId}/tickets/generate-from-codebase` | None | `CodebaseTicketGenerationResult` | Retired WPF history / dogfood | Implemented |
| `GenerateDraftAsync` | POST | `/api/projects/{projectId}/tickets/draft` | `DraftTicketRequest` | `DraftTicket` | Retired WPF history | Implemented |
| `GeneratePlanAsync` | POST | `/api/projects/{projectId}/tickets/draft/plan` | `DraftTicket` | `DraftTicket` | Retired WPF history | Implemented |
| `RegenerateTestsAsync` | POST | `/api/projects/{projectId}/tickets/draft/tests` | `DraftTicket` | `DraftTicket` | Retired WPF history | Implemented |
| `CreateBuildPreviewAsync` | POST | `/api/projects/{projectId}/tickets/{ticketId}/build-preview` | None | `TicketBuildPreview` | Retired WPF history | Implemented |
| `EvaluateReadinessAsync` | GET | `/api/projects/{projectId}/tickets/{ticketId}/build-readiness` | None | `BuildReadinessResult` | Retired WPF history | Implemented |
| `GenerateProposalAsync` | POST | `/api/tickets/{ticketId}/proposal` | None | `BuilderProposal` | Retired WPF history | Implemented |
| `GenerateProposalFromRequestAsync` | POST | `/api/projects/{projectId}/proposal` | `{ request }` | `BuilderProposal` | Retired WPF history | Implemented |
| `ApplyProposalAsync` | POST | `/api/projects/{projectId}/proposal/apply` | `BuilderProposal` | None | Retired WPF history | Implemented |
| `ApplyAndBuildAsync` | POST | `/api/tickets/{ticketId}/apply-and-build` | `TicketBuildApproval` | `TicketBuildResult` | Retired WPF history | Implemented |
| `ValidateProposalArchitectureAsync` | POST | `/api/projects/{projectId}/proposal/validate-architecture` | `BuilderProposal` | `BuildReadinessResult` | Retired WPF history | Implemented |
| `StartTicketBuildRunAsync` | POST | `/api/projects/{projectId}/tickets/{ticketId}/build-runs` | `StartTicketBuildRunRequest` | `TicketBuildRunDto` | Product CLI/TauriShell | Implemented; workflow state persistence still planned |
| `GenerateTicketsFromDocumentVersionAsync` | POST | `/api/document-versions/{versionId}/generate-tickets` | Planned | Planned ticket result DTO | Product CLI/TauriShell | Missing |

## Documents

| Client method | HTTP method | API route | Request DTO | Response DTO | Current consumers | Status |
|---|---|---|---|---|---|---|
| `CreateDocumentAsync` | POST | `/api/projects/{projectId}/documents` | `CreateProjectDocumentRequest` | `ProjectDocument` | Retired WPF history | Implemented |
| `AddVersionAsync` | POST | `/api/documents/{documentId}/versions` | `AddProjectDocumentVersionRequest` | `ProjectDocumentVersion` | Retired WPF history | Implemented |
| `GetDocumentsAsync` | GET | `/api/projects/{projectId}/documents` | `GetProjectDocumentsRequest` query values | `IReadOnlyList<ProjectDocument>` | Retired WPF history | Implemented |
| `GetDocumentAsync` | GET | `/api/documents/{documentId}` | None | `ProjectDocument?` | Retired WPF history | Implemented |
| `GetCurrentVersionAsync` | GET | `/api/documents/{documentId}/versions/current` | None | `ProjectDocumentVersion?` | Retired WPF history | Implemented |
| `GetVersionAsync` | GET | `/api/document-versions/{versionId}` | None | `ProjectDocumentVersion?` | Retired WPF history | Implemented |
| `GetVersionHistoryAsync` | GET | `/api/documents/{documentId}/versions` | None | `IReadOnlyList<ProjectDocumentVersion>` | Retired WPF history | Implemented |
| `LinkVersionAsync` | POST | `/api/document-versions/{versionId}/links` | `LinkProjectDocumentVersionRequest` | None | Retired WPF history | Implemented |
| `GetLinksForVersionAsync` | GET | `/api/document-versions/{versionId}/links` | None | `IReadOnlyList<ProjectDocumentLink>` | Retired WPF history | Implemented |
| `ArchiveDocumentAsync` | DELETE | `/api/documents/{documentId}` | None | None | Retired WPF history | Implemented |
| `UpdateDocumentAsync` | PUT | `/api/projects/{projectId}/documents/{documentId}` | Planned wrapper | `ProjectDocument` | Product CLI/TauriShell | Missing client method |
| `ResolveDocumentAsync` | POST | `/api/projects/{projectId}/documents/{documentId}/resolve` | Planned wrapper | Planned resolution DTO | Product CLI/TauriShell | Missing client method; API route is stubbed |

## Memory

| Client method | HTTP method | API route | Request DTO | Response DTO | Current consumers | Status |
|---|---|---|---|---|---|---|
| `GetLatestSummaryAsync` | GET | `/api/projects/{projectId}/memory/summary` | None | `ProjectSummary?` | Retired WPF history | Implemented |
| `GetRecentDecisionsAsync` | GET | `/api/projects/{projectId}/memory/decisions?take={take}` | None | `IReadOnlyList<ProjectDecision>` | Retired WPF history | Implemented |
| `SaveSummaryAsync` | POST | `/api/projects/{projectId}/memory/summary` | `ProjectSummary` | `long` | Retired WPF history | Implemented |
| `GetContextDocumentsAsync` | GET | `/api/projects/{projectId}/memory/documents` | Query filters | `IReadOnlyList<ProjectContextDocument>` | Retired WPF history | Implemented |
| `GetRelevantContextDocumentsAsync` | GET | `/api/projects/{projectId}/memory/search` | Query/take | `IReadOnlyList<ProjectContextDocument>` | Retired WPF history; Product CLI uses | Implemented |
| `GetContextDocumentByIdAsync` | GET | `/api/memory/documents/{documentId}` | None | `ProjectContextDocument?` | Retired WPF history | Implemented |
| `SaveContextDocumentAsync` | POST | `/api/projects/{projectId}/memory/documents` | `ProjectContextDocument` | `long` | Retired WPF history | Implemented |
| `ArchiveContextDocumentAsync` | DELETE | `/api/memory/documents/{documentId}` | None | `bool` | Retired WPF history | Implemented |
| `GetRecentPlansAsync` | GET | `/api/projects/{projectId}/memory/plans?take={take}` | None | `IReadOnlyList<ProjectImplementationPlan>` | Retired WPF history | Implemented |
| `GetPlanByIdAsync` | GET | `/api/memory/plans/{planId}` | None | `ProjectImplementationPlan?` | Retired WPF history | Implemented |
| `GetPlanByTicketIdAsync` | GET | `/api/tickets/{ticketId}/implementation-plan` | None | `ProjectImplementationPlan?` | Retired WPF history | Implemented |
| `SavePlanAsync` | POST | `/api/projects/{projectId}/memory/plans` | `ProjectImplementationPlan` | `long` | Retired WPF history | Implemented |
| `SaveDecisionAsync` | POST | `/api/projects/{projectId}/memory/decisions` | `ProjectDecision` | `long` | Retired WPF history | Implemented |
| `GetProjectRulesAsync` | GET | `/api/projects/{projectId}/memory/rules` | None | `IReadOnlyList<ProjectRule>` | Retired WPF history | Implemented |
| `SaveProjectRuleAsync` | POST | `/api/projects/{projectId}/memory/rules` | `ProjectRule` | `long` | Retired WPF history | Implemented |

## Code Index

| Client method | HTTP method | API route | Request DTO | Response DTO | Current consumers | Status |
|---|---|---|---|---|---|---|
| `IndexDirectoryAsync` | POST | `/api/projects/{projectId}/code-index` | `{ directoryPath }` | `CodeIndexResult` | Retired WPF history / dogfood | Implemented |
| `SearchFilesAsync` | GET | `/api/projects/{projectId}/code-index/files/search` | Query/take | `IReadOnlyList<ProjectFile>` | Retired WPF history | Implemented |
| `GetIndexedFileCountAsync` | GET | `/api/projects/{projectId}/code-index/file-count` | None | `int` | Retired WPF history | Implemented |
| `GetRecentFilesAsync` | GET | `/api/projects/{projectId}/code-index/files/recent` | Query/take | `IReadOnlyList<ProjectFile>` | Retired WPF history | Implemented |
| `GetRelevantSnippetsAsync` | GET | `/api/projects/{projectId}/memory/search/snippets` | Query/take | `IReadOnlyList<CodeIndexEntry>` | Retired WPF history | Implemented |

## Chat

| Client method | HTTP method | API route | Request DTO | Response DTO | Current consumers | Status |
|---|---|---|---|---|---|---|
| `GetRecentSessionsAsync` | GET | `/api/projects/{projectId}/chat/sessions?take={take}` | None | `IReadOnlyList<ProjectChatSession>` | Retired WPF history / TauriShell | Implemented |
| `GetSessionByIdAsync` | GET | `/api/chat/sessions/{sessionId}` | None | `ProjectChatSession?` | Retired WPF history / TauriShell | Implemented |
| `SaveSessionAsync` | POST | `/api/projects/{projectId}/chat/sessions` | `ProjectChatSession` | `long` | Retired WPF history / TauriShell | Implemented |
| `DeleteSessionAsync` | DELETE | `/api/chat/sessions/{sessionId}` | None | None | Retired WPF history / TauriShell | Implemented |
| `SaveMessageAsync` | POST | `/api/projects/{projectId}/chat/sessions/{sessionId}/messages` | `ChatMessage` | `long` | Retired WPF history / TauriShell | Implemented |
| `SaveFeedbackAsync` | POST | `/api/projects/{projectId}/chat/feedback` | `ChatMessageFeedback` | `long` | Retired WPF history / TauriShell | Implemented |
| `GetRecentMessagesAsync` | GET | `/api/projects/{projectId}/chat/sessions/{sessionId}/messages?take={take}` | None | `IReadOnlyList<ChatMessage>` | Retired WPF history / TauriShell | Implemented |
| `CompleteAsync` | POST | `/api/projects/{projectId}/chat/complete` | `ChatCompletionRequest` | `ChatCompletionResponse` | Retired WPF history / TauriShell | Implemented |

## Profiles

| Client method | HTTP method | API route | Request DTO | Response DTO | Current consumers | Status |
|---|---|---|---|---|---|---|
| `GetProjectProfileAsync` | GET | `/api/projects/{projectId}/profile` | None | `ProjectProfile?` | Retired WPF history | Implemented |
| `SaveProjectProfileAsync` | POST | `/api/projects/{projectId}/profile` | `ProjectProfile` | None | Retired WPF history | Implemented |
| `GetProjectCommandsAsync` | GET | `/api/projects/{projectId}/profile/commands` | None | `List<ProjectCommand>` | Retired WPF history | Implemented |
| `SaveProjectCommandAsync` | POST | `/api/projects/{projectId}/profile/commands` | `ProjectCommand` | None | Retired WPF history | Implemented |
| `GetDefaultCommandAsync` | GET | `/api/projects/{projectId}/profile/commands/default/{commandType}` | None | `ProjectCommand?` | Retired WPF history | Implemented |
| `GetOptionsByCategoryAsync` | GET | `/api/profile/options/{category}` | None | `List<ProjectProfileOption>` | Retired WPF history | Implemented |
| `DetectAsync` | POST | `/api/profile/detect` | `{ projectRoot, projectId }` | `ProjectProfileDetectionResult` | Retired WPF history | Implemented |

## Run Reports

`IRunReportsApiClient` inherits `IRunReportService` and `IRunEvidenceService`, so retired WPF consumers used report services through the client registration. This is cleaner than direct file report reads in WPF, but it is still report-shaped rather than durable run-shaped.

| Client method | HTTP method | API route | Request DTO | Response DTO | Current consumers | Status |
|---|---|---|---|---|---|---|
| `GetRecentRunsAsync` | GET | `/api/run-reports?project={project}` | None | `IReadOnlyList<RunReportSummary>` | Retired WPF history | Implemented |
| `GetRunAsync` | GET | `/api/run-reports/{runId}` | None | `RunReportDetail?` | Retired WPF history | Implemented |
| `GetEvidenceAsync` | GET | `/api/run-reports/{runId}/evidence` | None | `IReadOnlyList<RunEvidenceItem>` | Retired WPF history | Implemented |
| `ReadEvidenceTextAsync` | GET | `/api/run-reports/{runId}/evidence/text?path={evidencePath}` | Evidence path query | `string?` | Retired WPF history | Implemented |
| `GetRunStatusAsync` | GET | `/api/runs/{runId}` | None | `RunStatusDto` | Product CLI/TauriShell | Implemented |
| `GetRunReportAsync` | GET | `/api/runs/{runId}/report` | None | `RunReportDto` | Product CLI/TauriShell | Implemented |
| `StreamRunEventsAsync` | GET | `/api/runs/{runId}/events` | None | `IAsyncEnumerable<RunEventDto>` | Product CLI/TauriShell | Implemented; live SQL-backed stream, no report-snapshot synthesis |

## Non-HTTP Boundary Helpers

| Client type | Methods | Status | Notes |
|---|---|---|---|
| `ISettingsApiClient` | None | Stubbed | Registered as `NoopSettingsApiClient`; no API operations. |
| `ITraceApiClient`/`InMemoryTraceClient` | Trace storage methods | Stubbed/local | In-memory adapter, not a REST client. |
| `ClientPromptContextBuilder` | `BuildAsync`, `BuildPacketAsync`, `BuildFullPromptForTestingAsync` | Implemented adapter | Composes prompt context through client-side service adapters, not direct HTTP routes. |
