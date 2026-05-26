# IronDev Architecture

## Overview

IronDev is structured as a multi-tenant client-server system.

The product boundary is the REST API. The desktop UI is a cockpit, not the owner of persistence, tenancy, memory, tickets, documents, or AI workflow state.

IronDev is moving toward a thin desktop-client boundary; the current boundary is not sealed.

```text
IronDeveloper WPF
  -> IronDev.Client
  -> IronDev.Api
  -> IronDev.Infrastructure
```

`IronDeveloper` owns presentation, view composition, view model state, navigation, selection, dirty editor state, keyboard shortcuts, local path selection, local preview state, and desktop affordances such as screenshots, clipboard, and windows.

Product persistence and workflow behaviour should go through `IronDev.Client` and `IronDev.Api`. Current boundary gaps are tracked in `Docs/architecture/API_CLIENT_CLI_BOUNDARY_FINDINGS.md`.

IronDev tickets are canonical for implementation work. GitHub issues may be linked or backfilled as external references, but they are not the source of truth unless a task explicitly says to create or use a GitHub issue.

## Layer Map

```text
IronDeveloper WPF client
  -> IronDev.Client typed HTTP client
  -> IronDev.Api REST backend
  -> IronDev.Infrastructure services/repositories/providers
  -> SQL Server / Weaviate / OpenAI / file/build adapters
```

`IronDeveloper/IronDev.Agent.csproj` references `IronDev.Client` and `IronDev.Core`. It must not reference `IronDev.Infrastructure`.

`IronDeveloper` must not directly own SQL, Dapper repositories, Weaviate, OpenAI/provider calls, tenant enforcement, prompt context assembly, persistent ticket/document/memory mutations, or build workflow state mutation.

## CLI/API Boundary

The CLI is an operational client of the API.

Correct product write paths:

```text
Codex -> CLI -> IronDev.Client/HTTP -> IronDev.Api -> services -> DB
UI -> IronDev.Client/HTTP -> IronDev.Api -> services -> DB
```

Wrong product write paths:

```text
UI -> API -> CLI -> services
Codex -> CLI -> Infrastructure -> DB
API endpoint -> ReplayRunner command -> stdout -> response
```

The product CLI must use `IronDev.Client`; direct HTTP construction belongs inside `IronDev.Client` only. Today `tools/IronDev.Cli` routes its current ticket commands through `IIronDevApiClient`. The CLI must not call SQL directly, Dapper repositories, Infrastructure services, `TicketService` directly, or GitHub issues as canonical ticket storage.

`tools/IronDev.Cli` is the product CLI. `tools/IronDev.ReplayRunner` is internal dogfood/replay tooling and may keep smoke plans, campaign checks, replay harnesses, memory spine tests, benchmark scripts, Weaviate smoke checks, SQL smoke checks, and lower-level diagnostics. Dogfood commands must be labelled internal and must not be presented as normal product commands.

The API may call application/domain services, Infrastructure services, repositories, and providers. The API must not call `IronDev.ReplayRunner`, `tools/IronDev.Cli` command handlers, PowerShell wrappers, shell commands for product persistence, or stdout-parsed command results.

Do not put the CLI behind API endpoints. The API is the product boundary; the CLI is only one client of that boundary.

## Boundary Rules

### Hard rule

`IronDeveloper` must not reference or call `IronDev.Infrastructure` directly.

### Allowed UI responsibilities

- View composition
- ViewModel state
- Selection and filtering state
- Local editor dirty state
- Keyboard shortcuts
- Local filesystem path selection
- Local-only preview state
- Calling `IronDev.Client`

### Forbidden UI responsibilities

- Direct SQL access
- Direct Dapper repository access
- Direct Weaviate access
- Direct OpenAI/provider calls
- Direct ticket/document/memory persistence
- Direct tenant enforcement
- Direct prompt context assembly
- Direct build workflow mutation without an API/workflow boundary

## Boundary Ownership

`IronDev.Client` owns:

- JWT/session handling
- typed API clients
- shared HTTP error handling
- API-facing workflow methods for WPF ViewModels

`IronDev.Api` owns:

- auth and tenant selection
- project, ticket, document, memory, chat, code-index, build, run-report, and profile endpoints
- API-backed report endpoints today, with durable run status/report/event endpoints planned
- request-scoped tenancy from JWT claims
- orchestration through `IronDev.Infrastructure`

`IronDev.Infrastructure` owns:

- SQL persistence
- code indexing
- LLM integrations
- build orchestration
- run reports
- product services behind the API

## Local-Only Exceptions

The WPF client keeps only desktop-local behaviour:

- `IAppSettingsService` for client presentation/settings preferences
- screenshot capture and testing companion local files
- shell/window/navigation state
- in-memory trace display state
- markdown rendering fallback for document preview
- prompt playground compatibility shims used for diagnostics, not authoritative product persistence
- local test compatibility adapters for legacy integration test construction only

These exceptions must not persist product state directly to SQL or Infrastructure services. Results that become persistent product state must be sent through `IronDev.Api`.

## Current Boundary Verification

- `IronDev.Client` has no `IronDev.Infrastructure` project reference.
- `tools/IronDev.Cli` references `IronDev.Client`, does not reference `IronDev.Infrastructure`, and does not construct direct `HttpClient` calls.
- `IronDev.TauriShell` is a TypeScript shell spike that calls API routes and generated OpenAPI types; it does not reference Infrastructure, SQL, repositories, or Weaviate directly.
- `IronDeveloper` WPF is legacy but remains mostly on the client boundary: it references `IronDev.Client`, and local behaviours are presentation/testing compatibility only.

The executable boundary checks live in `IronDev.IntegrationTests/ApiBoundaryTests.cs`.

## Guardrail

Run:

```powershell
powershell -ExecutionPolicy Bypass -File ./Scripts/Assert-WpfApiBoundary.ps1
```

The guard fails if `IronDeveloper` reintroduces forbidden WPF coupling such as `IronDev.Infrastructure`, `IronDev.Services`, or the old direct service interface names.

## UI Testability Gate

Future UI shell work must be testable by contract before it becomes serious product surface.

No serious new UI shell work starts until the Playwright harness exists, the `data-testid` convention exists, JSON and markdown reports exist, and at least one journey can run or is explicitly marked pending against a missing shell. Future UI PRs must add or update journey coverage for changed workflows.

The UI testing contract lives in `Docs/UI_TESTING_CONTRACT.md`. The Codex-readable result schema lives in `Docs/UI_TEST_RESULT_SCHEMA.md`.

## UTC Timestamp Contract

All persisted product timestamps are UTC-first. This applies to project memory, tickets, documents, traces, build/test runs, imports, provenance, CLI output, and API DTOs.

The full engineering standard lives in `Docs/DATETIME_UTC_STANDARD.md`.

Rules:

- Store timestamps as UTC.
- Prefer `DateTimeOffset.UtcNow` or `DateTime.UtcNow`; do not use `DateTime.Now` for product or audit timestamps.
- API and client DTO timestamp properties should make UTC semantics explicit with names such as `CreatedUtc`, `UpdatedUtc`, `ArchivedUtc`, `VersionCreatedUtc`, `LastIndexedUtc`, `StartedUtc`, `FinishedUtc`, `ImportedUtc`, `SourceCreatedUtc`, and `SourceUpdatedUtc`.
- JSON responses and CLI JSON output preserve ISO 8601 UTC values.
- UI surfaces must not display ambiguous local-only timestamps such as `25/05/2026`.
- UI primary display may use friendly local time, but secondary metadata or tooltip text must expose UTC clearly.
- Shared formatting helpers must be used instead of one-off timestamp formatting in view models or XAML.
- Stale/currentness warnings must be calculated from UTC timestamps.

Recommended UI pattern:

```text
Primary: 25 May 2026, 14:32
Tooltip: 2026-05-25T02:32:00Z UTC
Compact: Updated 12m ago - 2026-05-25 02:32 UTC
```

## Tenancy Model

Every domain entity carries a `TenantId` foreign key. All service reads filter by `TenantId`. All service writes validate project ownership before insert.

| Table | TenantId column | Enforced by |
|---|---|---|
| Projects | yes | ProjectService WHERE clause |
| ChatMessages | yes | ChatHistoryService ownership guard |
| ProjectSummaries | yes | ProjectMemoryService ownership guard |
| ProjectDecisions | yes | ProjectMemoryService ownership guard |
| ProjectTickets | yes | TicketService ownership guard |
| ProjectFiles | yes | CodeIndexService ownership guard |

Ownership guard pattern: before any insert, services verify `SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId`. Cross-tenant writes throw `UnauthorizedAccessException`.

## ICurrentTenantContext

The tenancy abstraction is interface-based and scoped per request/operation.

| Context | Used in | Returns |
|---|---|---|
| `JwtTenantContext` | ASP.NET Core API | Reads `tenant_id` claim from JWT |
| `TestTenantContext` | Integration tests | Mutable; tests switch tenants to verify isolation |
| `DevelopmentTenantContext` | Legacy local migration seam only | Always `TenantId = 1`; remove from WPF paths as they migrate to API |

## Auth Flow

```text
1. POST /api/auth/login        -> base JWT (userId, email, no tenant claim)
2. GET  /api/tenants           -> tenants the user can access
3. POST /api/tenants/select    -> tenant-bearing JWT with tenant_id claim
4. Subsequent API calls use the tenant-bearing JWT
```

Services resolve `ICurrentTenantContext` from the JWT claim per request.

## Local vs Backend Responsibilities

### Stays local client-side

- Local filesystem picker and path validation
- Local code/workspace preview where no persistent state is mutated
- Local process execution where the OS requires it
- Temporary disposable workspace files

### Belongs to the backend

- Authentication and session management
- Tenant resolution and enforcement
- All persistent data: projects, tickets, chat, memory, decisions, summaries
- Code index storage
- Prompt context assembly
- Ticket generation state
- Document version state
- Trace and evidence persistence
- Approval state
- Build/test run records

## Key Design Decisions

| Decision | Detail |
|---|---|
| API as product boundary | UI shells are replaceable; backend owns product behaviour |
| `IronDev.Client` first | Typed HTTP abstraction between forward clients and API; current Product CLI ticket commands use it |
| No second UI stack until boundary is proven | WPF stays; direct Infrastructure dependency is removed, while CLI/client gaps are documented |
| Dapper over EF Core | SQL-native, explicit queries, no migration complexity |
| BCrypt for password hashing | Industry standard, no extra ASP.NET dependency |
| JWT re-issue on tenant select | Tenant identity embedded in token, not a client-controlled header |
| Sequential integration tests | Tests share a real SQL Server DB; Workers=1 is required in runsettings |
| No ASP.NET Core Identity | Too heavy for current phase; plain `UserService` + Dapper is sufficient |
| Keyword-first context | Prompt builder prioritizes technical keywords extracted from user prompts |

## Observability And Tracing

IronDev keeps LLM tracing separate from standard application logging because they answer different questions.

`ILlmTraceService` is the AI workflow record. It should capture prompts, raw model responses, parsed summaries, context packets, route decisions, sufficiency checks, evidence/proof gates, trace groups, warnings, and model errors. These entries are product/debugging evidence and power the LLM Console.

Serilog is the operational log. It should capture app startup/shutdown, service failures, exception stacks, indexing progress/failures, skipped files/folders, timing, health signals, and configuration/provider issues. Full prompts and raw model responses should not be written to standard log files; only compact LLM trace summaries should be logged for correlation.

Recommended boundary:

| Concern | Tool |
|---|---|
| AI workflow evidence | `ILlmTraceService` |
| Runtime health and failures | Serilog |
| Service-boundary timing/errors | REST middleware, explicit pipeline tracing, or small DI decorators |
| Meaningful Context Agent stages | Explicit trace entries |

Tracing should prefer visible pipeline boundaries over AOP-style interception. Good pipeline candidates include REST middleware and Context Agent stages. Where no useful pipeline exists, small DI decorators are acceptable for service-boundary timing, correlation, and error logging. Heavy runtime interception should be avoided.

## Governed Autonomy Control Plane

IronDev's agent layer is governed autonomy, not free autonomy.

Live provider calls are traced through the agent output and fall back to deterministic behaviour when unavailable. This does not grant write authority, memory mutation, ticket creation, patch apply, ranking override, quality override, governance bypass, or self-approval.

The governed tool loop allows PlannerAgent to request named capabilities, the tool registry to check capability boundaries and runtime profiles, safe tools to collect evidence, CriticAgent to review evidence, PlannerAgent to revise the plan, and a human escalation gate to record whether review or more evidence is required.

BuilderAgent remains caged. It may write only inside explicit disposable workspaces with evidence. Real repository writes remain blocked unless the reviewed promotion path supplies trace, promotion package, proposed change, approval, build/test/quality, ConscienceAgent, and ThoughtLedger evidence.
