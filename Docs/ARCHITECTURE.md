# IronDev Architecture

## Overview

IronDev is structured as a **multi-tenant client-server system**.

The WPF desktop client currently communicates directly with local SQL services (Sprint 1 foundation). The REST API backend is being built in parallel and will eventually own all data-access paths. The WPF client will migrate to HTTP calls in Sprint 3.

---

## Layer Map

```
┌─────────────────────────────────────────────────┐
│  IronDeveloper (WPF client)                     │
│  · Project panel, chat, tickets, workbench      │
│  · Currently uses Infrastructure services       │
│    directly (local mode)                        │
│  · Will migrate to HTTP client in Sprint 3      │
└──────────────┬──────────────────────────────────┘
               │ (future: HTTP)
┌──────────────▼──────────────────────────────────┐
│  IronDev.Api (ASP.NET Core REST backend)        │
│  · Auth: POST /api/auth/login                   │
│  · Tenant selection: POST /api/tenants/select   │
│  · Domain APIs: projects, tickets, chat, memory │
│  · JWT authentication + request-scoped tenancy  │
└──────────────┬──────────────────────────────────┘
               │
┌──────────────▼──────────────────────────────────┐
│  IronDev.Infrastructure                         │
│  · Dapper SQL repositories                      │
│  · UserService, ProjectService, TicketService   │
│  · ChatHistoryService, ProjectMemoryService     │
│  · CodeIndexService, PromptContextBuilder       │
│  · DevelopmentTenantContext (local/WPF fallback)│
│  · WorkbenchGeneratorService                    │
└──────────────┬──────────────────────────────────┘
               │
┌──────────────▼──────────────────────────────────┐
│  IronDev.Core                                   │
│  · Domain models (Project, Ticket, ChatMessage) │
│  · ICurrentTenantContext                        │
│  · Auth DTOs (LoginRequest, LoginResponse, etc) │
│  · ILLMService interface                        │
└──────────────┬──────────────────────────────────┘
               │
┌──────────────▼──────────────────────────────────┐
│  SQL Server                                     │
│  · IronDeveloper (production)                   │
│  · IronDeveloper_Test (integration tests)       │
└─────────────────────────────────────────────────┘
```

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
| `DevelopmentTenantContext` | WPF client (local dev) | Always `TenantId = 1` |
| `JwtTenantContext` | ASP.NET Core API | Reads `tenant_id` claim from JWT |
| `TestTenantContext` | Integration tests | Mutable — tests switch tenants to verify isolation |

---

## Auth Flow (Sprint 2)

```
1. POST /api/auth/login  →  base JWT (userId, email — no tenant claim)
2. GET  /api/tenants     →  list of tenants user is a member of
3. POST /api/tenants/select  →  new JWT with tenant_id claim embedded
4. All subsequent API calls use the tenant-bearing JWT
```

Services resolve `ICurrentTenantContext` from the JWT claim per-request.

---

## Local vs Backend Responsibilities

### Stays local (client-side)
- Local file system access
- Code indexing of local repository paths
- Workbench sandbox preview (temp drafts, not persisted)
- Future: local build/test execution loop

### Belongs to the backend
- Authentication and session management
- Tenant resolution and enforcement
- All persistent data: projects, tickets, chat, memory, decisions, summaries
- Code index storage (indexed content stored in SQL)
- Prompt context assembly

---

## Key Design Decisions

| Decision | Detail |
|---|---|
| Dapper over EF Core | SQL-native, explicit queries, no migration complexity |
| BCrypt for password hashing | Industry standard, no extra ASP.NET dependency |
| JWT re-issue on tenant select | Tenant identity embedded in token, not a client-controlled header |
| Sequential integration tests | Tests share a real SQL Server DB — Workers=1 is required in runsettings to avoid FK/data conflicts |
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
| Service-boundary timing/errors | Explicit pipeline tracing where available; DI decorators where no pipeline boundary exists |
| Meaningful Context Agent stages | Explicit trace entries |

Tracing should prefer visible pipeline boundaries over AOP-style interception. Good pipeline candidates include REST middleware and Context Agent stages. Where no useful pipeline exists, small DI decorators are acceptable for service-boundary timing, correlation, and error logging. Heavy runtime interception should be avoided. Good decorator candidates are `ILLMService`, `ICodeIndexService`, and `ITicketBuildOrchestrator`; `IContextAgentService` should keep explicit traces for meaningful AI decisions.

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

IRONDEV-158 adds opt-in live model execution for the governed ArchitectAgent path. IRONDEV-159 extends the same pattern to CriticAgent failure review and PlannerAgent planning/intake. IRONDEV-160 extends it to RetrieverAgent context packaging and SentinelAgent insight observation. IRONDEV-161 completes the current useful live-agent pass for ResearchAgent, QualityAgent, and SupervisorAgent while keeping TesterAgent, ConscienceAgent, and ThoughtLedger deterministic. Live provider calls are traced through the agent output and fall back to deterministic behaviour when unavailable. This does not grant write authority, memory mutation, ticket creation, patch apply, ranking override, quality override, governance bypass, or self-approval.

IRONDEV-162 through IRONDEV-167 add a native governed tool loop. PlannerAgent requests named capabilities, the tool registry checks capability boundaries and runtime profiles, safe tools collect evidence, CriticAgent reviews the evidence, PlannerAgent revises the plan, and a human escalation gate records whether review or more evidence is required. .NET is the first runtime adapter, but Node and Python profiles are represented through the same capability contract.

IRONDEV-168 connects the governed loop to the product-shaped disposable build path. `build disposable run` can now take a messy goal such as `I want build solitaire`, create run-scoped intake/build/ticket artefacts, run Planner/Critic evidence collection, execute the caged BuilderAgent repair loop, run QualityAgent/Killjoy, and return a single evidence report. The generated app remains inside the disposable workspace and run-scoped docs do not become accepted memory.

IRONDEV-169 adds the promotion bridge. `ProposedChange` is the review case file; `PromotionPackage` is the evidence package. Promotion package creation classifies disposable workspace files through `ILanguageRuntimeRegistry`, attaches build/test/quality evidence, blocks generated outputs such as `bin/` and `obj/`, and keeps approval at `NeedsHumanReview`. The `csharp-dotnet` runtime profile is executable; Java, TypeScript, and Python profiles are contract-only until reviewed runtime executors are added.

IRONDEV-170 proves isolated promotion apply. `promotion apply isolated` consumes a `PromotionPackage`, creates an isolated candidate workspace outside the active repo, copies only `FilesToPromote`, rejects `FilesBlocked`, runs runtime build/test, and writes an isolated apply report. This proves the promotion bridge can become reviewable candidate code without writing main or granting self-approval.

IRONDEV-171 turns the Run Reports viewer into the promotion review cockpit. `FileRunReportService` can read promotion package and isolated apply reports as structured review data, including approval state, runtime profile, promotable files, blocked files, policy settings, and hard invariants. WPF still calls shared C# services directly rather than shelling out to ReplayRunner.

BuilderAgent remains caged. It may write only inside explicit disposable workspaces with evidence. Real repository writes remain blocked until a future reviewed write-path design exists.
