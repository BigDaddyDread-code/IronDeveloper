# IronDev Architecture

## Overview

IronDev is structured as a **multi-tenant client-server system**.

The product boundary is the REST API. The desktop UI is a cockpit, not the owner of persistence, tenancy, memory, tickets, or AI workflow state.

The previous WPF-local mode, where `IronDeveloper` called `IronDev.Infrastructure` services directly, is now classified as **critical migration debt**. It is not an endorsed architecture and must not be expanded. New UI work must route through `IronDev.Client` and `IronDev.Api` unless there is an explicit, documented local-only exception.

The immediate migration goal is to remove the WPF client's direct dependency on `IronDev.Infrastructure`. The first boundary project is `IronDev.Client`, which provides typed HTTP access to `IronDev.Api`.

---

## Layer Map

```text
IronDeveloper WPF client
  -> IronDev.Client typed HTTP client
  -> IronDev.Api REST backend
  -> IronDev.Infrastructure services/repositories/providers
  -> SQL Server / Weaviate / OpenAI / file/build adapters
```

`IronDeveloper` owns view composition, view model state, selection, dirty editor state, keyboard shortcuts, local path selection, local preview state, and calls to `IronDev.Client`.

`IronDeveloper` must not directly own SQL, Dapper repositories, Weaviate, OpenAI/provider calls, tenant enforcement, prompt context assembly, persistent ticket/document/memory mutations, or build workflow state mutation.

---

## Boundary Rules

### Hard rule

`IronDeveloper` must not add new direct calls to `IronDev.Infrastructure`.

The existing direct Infrastructure reference is a migration seam only. Every screen migrated behind the API client must remove the corresponding direct service dependency.

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

### Local-only exceptions

Some operations may still execute on the local machine because they need local filesystem/process access:

- Selecting a local repository path
- Reading local files for preview
- Local build/test process execution
- Local disposable workspace operations

These exceptions still need a product boundary. Results that become persistent product state must be sent through `IronDev.Api`.

---

## Tenancy Model

Every domain entity carries a `TenantId` foreign key. All service reads filter by `TenantId`. All service writes validate project ownership before insert.

| Table | TenantId column | Enforced by |
|---|---|---|
| Projects | ✅ | ProjectService WHERE clause |
| ChatMessages | ✅ | ChatHistoryService ownership guard |
| ProjectSummaries | ✅ | ProjectMemoryService ownership guard |
| ProjectDecisions | ✅ | ProjectMemoryService ownership guard |
| ProjectTickets | ✅ | TicketService ownership guard |
| ProjectFiles | ✅ | CodeIndexService ownership guard |

**Ownership guard pattern:** before any insert, services verify `SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId`. Cross-tenant writes throw `UnauthorizedAccessException`.

---

## ICurrentTenantContext

The tenancy abstraction is interface-based and scoped per request/operation.

| Context | Used in | Returns |
|---|---|---|
| `JwtTenantContext` | ASP.NET Core API | Reads `tenant_id` claim from JWT |
| `TestTenantContext` | Integration tests | Mutable; tests switch tenants to verify isolation |
| `DevelopmentTenantContext` | Legacy local migration seam only | Always `TenantId = 1`; remove from WPF paths as they migrate to API |

---

## Auth Flow

```text
1. POST /api/auth/login        -> base JWT (userId, email, no tenant claim)
2. GET  /api/tenants           -> tenants the user can access
3. POST /api/tenants/select    -> tenant-bearing JWT with tenant_id claim
4. Subsequent API calls use the tenant-bearing JWT
```

Services resolve `ICurrentTenantContext` from the JWT claim per request.

---

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

---

## Key Design Decisions

| Decision | Detail |
|---|---|
| API as product boundary | UI shells are replaceable; backend owns product behaviour |
| `IronDev.Client` first | Minimal client abstraction before any UI framework change |
| No second UI stack until boundary is sealed | WPF stays until the direct Infrastructure dependency is removed or explicitly caged |
| Dapper over EF Core | SQL-native, explicit queries, no migration complexity |
| BCrypt for password hashing | Industry standard, no extra ASP.NET dependency |
| JWT re-issue on tenant select | Tenant identity embedded in token, not a client-controlled header |
| Sequential integration tests | Tests share a real SQL Server DB; Workers=1 is required in runsettings |
| No ASP.NET Core Identity | Too heavy for current phase; plain `UserService` + Dapper is sufficient |
| Keyword-first context | Prompt builder prioritizes technical keywords extracted from user prompts |

---

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

---

## Governed Autonomy Control Plane

IronDev's agent layer is governed autonomy, not free autonomy.

The current control-plane architecture is:

```text
Human / Codex request
  -> RetrieverAgent weighted context
  -> ConscienceAgent safety review
  -> ThoughtLedger visible reasoning summary
  -> bounded agent action
  -> TesterAgent / QualityAgent validation
  -> trace and evidence report
```

Model profiles are runtime-configurable and support `OpenAI`, `LocalOpenAI`, and `Ollama`. Provider configuration does not grant tool authority. Tool authority remains controlled by `AgentDefinition.AllowedTools`, ConscienceAgent, and the workflow boundary.

Live provider calls are traced through the agent output and fall back to deterministic behaviour when unavailable. This does not grant write authority, memory mutation, ticket creation, patch apply, ranking override, quality override, governance bypass, or self-approval.

The governed tool loop allows PlannerAgent to request named capabilities, the tool registry to check capability boundaries and runtime profiles, safe tools to collect evidence, CriticAgent to review evidence, PlannerAgent to revise the plan, and a human escalation gate to record whether review or more evidence is required.

BuilderAgent remains caged. It may write only inside explicit disposable workspaces with evidence. Real repository writes remain blocked unless the reviewed promotion path supplies trace, promotion package, proposed change, approval, build/test/quality, ConscienceAgent, and ThoughtLedger evidence.