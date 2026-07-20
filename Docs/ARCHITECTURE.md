# IronDev Architecture

> **Architecture authority:** Use the [Canonical Architecture Index](architecture/CANONICAL_ARCHITECTURE_INDEX.md) to resolve current authority by domain and to distinguish canonical contracts from supporting, historical, superseded, and parking-lot documents.

## Overview

IronDev is structured as a multi-tenant client-server system.

The product boundary is the REST API. The desktop UI is a thin product client, not the owner of persistence, tenancy, memory, tickets, documents, or AI workflow state.

IronDev uses a thin product-client boundary. New product work targets `IronDev.Api`, `IronDev.Client`, `tools/IronDev.Cli`, and the Tauri shell path.

```text
TauriShell / Product CLI / Future Clients
  -> IronDev.Client
  -> IronDev.Api
  -> IronDev.Infrastructure
```

`IronDeveloper` WPF has been retired and removed from the product build. It must not be restored as a supported shell; any replacement UI work belongs in the API/client/Tauri path.

Product persistence and workflow behaviour goes through `IronDev.Client` and `IronDev.Api`. The current forward boundary is sealed for Product CLI and TauriShell source; remaining gaps are missing product features, not approved client-side Infrastructure bypasses.

IronDev tickets are canonical for implementation work. GitHub issues may be linked or backfilled as external references, but they are not the source of truth unless a task explicitly says to create or use a GitHub issue.

## Layer Map

```text
TauriShell / Product CLI / Future Clients
  -> IronDev.Client typed HTTP client
  -> IronDev.Api REST backend
  -> IronDev.Infrastructure services/repositories/providers
  -> SQL Server / Weaviate / OpenAI / file/build adapters
```

No forward-facing client project may reference `IronDev.Infrastructure`.

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

Forward clients must not reference or call `IronDev.Infrastructure` directly.

## Backend Spine Boundary Matrix

The backend workflow spine must stay explicit and reviewable:

| Stage | Primary owner | Owned API contracts | Owned responsibilities | Forbidden coupling | Required handoff |
|---|---|---|---|---|---|
| Discussion | `DiscussionCodeLoopController` + `IDiscussionDocumentService` | `POST /api/projects/{projectId}/discussions`, `POST /api/projects/{projectId}/documents/{documentVersionId}/tickets` | Capture, normalize, and version discussion artifacts | Direct ticket execution, proposal generation, run execution, or workspace actions | `discussion -> document -> ticket` |
| Chat | `ChatController` + `IProjectStateReviewService` | `POST /api/projects/{projectId}/chat/complete`, `GET /api/projects/{projectId}/chat/sessions/*` | User context review + memory trace composition only | Proposal/build/run APIs, ticket mutation, or workspace execution | `chat -> context summary only` |
| Proposal | `TicketsController` + `IBuilderProposalService` | `POST /api/projects/{projectId}/proposal`, `POST /api/tickets/{ticketId}/proposal`, `POST /api/projects/{projectId}/proposal/validate-architecture` | Transform review outcome into a `BuilderProposal` and validate architecture constraints | Run control, workspace execution, or repository writes | `proposal -> build run request` |
| Build | `TicketsController` + `ITicketBuildOrchestrator` + `ITicketBuildRunService` | `POST /api/projects/{projectId}/tickets/{ticketId}/build-preview`, `POST .../build-runs`, `GET .../build-runs*` | Build readiness, disposable run scheduling, and durable run state transitions | Proposal mutation, proposal-only editing, unapproved command/root overrides | `build -> run event stream` |
| Run | `IDisposableCodeRunService` + `IRunReviewPackageService` + `RunReportsController` | `POST /api/projects/{projectId}/tickets/{ticketId}/disposable-code-runs`, `GET .../build-runs/{runId}/review`, `GET .../review-package` | Execute backend-owned run profile, collect command/verification evidence, produce review package | Client-supplied workspace paths, stdout parsing as API authority, or real-repo mutation | `run -> review package (PausedForApproval)` |

## Workbench V2 Business Analyst operation boundary

PR-02A introduced the durable control plane for repository-independent Business Analyst turns. `POST /api/workbench/projects/{projectId}/agent-runs` is the submission authority: one serializable transaction validates the exact Workbench session and lease epoch, writes the user message, creates a `Pending` run, completes the scoped client operation, and emits a typed `AgentRunRequested` outbox event. The first accepted turn binds that Workbench session to one same-project chat conversation. Later turns must use that exact binding, and a filtered database invariant permits at most one `Pending` or `Running` run for the Workbench session; an idempotent replay is resolved before either conflict check.

External invocation is always outside the submission and materialization transactions. The worker claims `Pending` work (or an expired `Running` claim), records a distinct attempt, and persists one immutable server-owned context snapshot and hash. PR-02B resolves the exact recorded agent, prompt, tool-policy, context, and output-version tuple before preparing a stateless invocation through the existing `Analyst` role. Preparation has no model call: it reads only fixed project-identity, captured-understanding, and bounded trusted-conversation projections from that snapshot, builds the code-owned prompt, and records sanitized provider/profile/prompt/tool hashes without raw prompts or private reasoning. PR-02C-A sends that prepared request through a provider-neutral role envelope: code-owned policy is `System`, configured agent guidance is advisory `Developer`, and project/user material is untrusted `User`. One versioned aggregate UTF-8/token budget covers the entire request before provider resolution or invocation. Legacy string-only providers fail closed rather than flattening those roles.

Immediately after preparation, the processor atomically rechecks the active user and tenant/project membership, exact active Workbench session, holder, lease epoch, revocation/expiry, and current non-expired claim. It renews the five-minute claim and then performs no work except the external provider call. Provider execution is cancellation-aware and must finish within the configured timeout below the claim budget; timeout or provider failure produces bounded diagnostics and no assistant response. New requests and expired claims share globally oldest-ready ordering so a request backlog cannot starve crash recovery.

`WorkbenchAgentRunService.MaterializeAsync` is the exactly-once assistant writer. It locks the current lease before the run, verifies the claim/context/fence, writes one assistant message, completes the attempt/run, and emits the terminal event in one transaction. Cancelled, superseded, stale, invalid, and late responses can retain bounded hashes and codes in run/attempt diagnostics but cannot create assistant messages or other visible domain output. A lease takeover supersedes old-epoch `Pending`/`Running` work and requests cancellation in the same transaction as the new fence.

The hosted outbox worker remains explicitly feature-gated outside isolated environments. LocalTest may enable it with an explicit context-aware deterministic Analyst provider; that provider is test-only and is never a fallback for an unknown or failed production provider. Unverifiable legacy assistant rows are display history only and are excluded from provider context rather than misattributed to the user. Successful and failed attempts may record only bounded status, duration, usage when reported, correlation/provider request IDs, and hashes; raw prompts, completions, secrets, and private reasoning are not invocation telemetry. Once a provider has returned, audit and terminal validation/materialization form a non-cancellable commit phase; worker shutdown cannot make an already-completed provider call disappear and then be invoked again without evidence.

PR-02C-A makes the V2 Workshop composer an AgentRun client when `WorkbenchV2:ConversationAuthorityEnabled` is explicitly true. The shell performs a no-chat readiness preflight, creates or reuses the chat session through a durable idempotent operation, submits one fenced AgentRun command, polls backend state, recovers the active and latest terminal run after reload, requests cancellation through its own idempotent operation, and reloads server-owned messages after terminal materialization. It does not call the legacy synchronous chat-completion endpoint or write user/assistant messages directly. New-session lease validation/renewal, client-operation creation, and session insertion share one serializable transaction; a stale first-create fence therefore writes neither the session nor its operation ledger row, while a completed ambiguous retry replays before fencing. The client treats only explicit validation/access/domain rejections and structured AgentRun unavailability as definitive; generic or malformed HTTP failures and invalid success envelopes retain the exact operation receipt and payload for replay. The backend rejects compatibility mutations plus legacy session update/delete while the flag is active, enforces active project membership and project scope on parent and child chat routes, and rejects unavailable worker or deterministic provider configuration before any first-session or turn write. Late client responses are generation-fenced to the exact project, Workbench session, and lease so navigation cannot install an old run in a new context. The V1/compatibility path remains unchanged while the flag is false.

The first accepted AgentRun still binds the active Workbench session to exactly one direct chat session. The V2 shell fences new/switch-direct-conversation controls while submission is in flight, while create, submit, or cancellation delivery remains ambiguous, and after the binding. Ambiguous receipts are retained by full authority/operation key; only the unchanged message or exact cancellation may replay them, and recovery does not silently discard or reinterpret the receipt. Reopening the project resumes the same Workbench session and binding; this preview intentionally has no close/new-Workbench-session action, so it does not claim that reopening starts a different direct conversation. Document attachments, structured BA draft metadata, and ticket-command formalization are not silently synthesized by this slice and remain later contracts. Real-provider/default-on enablement additionally requires a model-specific capability contract and a server-owned context compaction or Workbench-session rollover corridor; the global request budget alone is not model capability evidence.

PR-02C-B makes Project Understanding a typed, revisioned Workbench authority rather than opaque chat metadata. Output schema version 2 may propose bounded product-intent fact changes and one project rename, but it cannot directly mutate the project name, choose a user lock, or write operational truth. Materialization validates every cited user message against the frozen same-project conversation, merges against the latest understanding under the current lease, preserves confirmed or locked values, records contrary values as explicit conflicts, and commits the understanding revision, rename proposal, assistant message, AgentRun terminal state, and outbox evidence atomically. Schema version 1 remains executable for already-persisted runs.

The Project Context read model combines those editable intent facts with authority-labelled, read-only lifecycle, execution-readiness, and repository projections. Direct fact decisions and rename acceptance are separate fenced and idempotent Workbench mutations. A fact edit is explicit user evidence and therefore becomes `Confirmed`; locking never grants the user authority to assert build, index, repository, or readiness evidence. Accepting a rename proposal updates only the canonical `Project.Name` inside the proposal transaction. Repository setup, ticket commands, and Builder authority remain outside this slice.

PR-03 adds the server-authoritative Workbench input router. A slash is a command only at the first non-whitespace composer position; the exact case-insensitive allowlist is /help and /ticket, with no aliases or prefix matches. The input service classifies before any BA availability or AgentRun call. /help and the PR-03 /ticket handoff return deterministic results. Unknown commands atomically validate and renew the exact lease, complete an idempotent client operation, append a `WorkbenchCommandRejection` plus outbox/attribution evidence, and return a bounded rejection without writing a chat session, Workbench message, or AgentRun. The rejection persists only the raw token and a SHA-256 payload hash; the full composer text stays client-side. The compatibility AgentRun submit route rejects leading-slash input, so clients cannot bypass deterministic command routing. Prose such as `create tickets` and `We may use /ticket later` remains ordinary BA conversation and receives no artifact authority.

PR-04A turns the trusted /ticket command into a distinct Business Analyst AgentRun purpose. Its frozen schema-version-3 context and output contract can materialize either one to five ordered, source-backed proposals or a zero-proposal `NeedsInput` set with at least one open question or potential conflict. The AgentRun terminal state, assistant response, current proposal set, first immutable full-snapshot revision, attribution, and outbox evidence commit atomically. A replay cannot create a second set or revision. Proposal generation creates no `ProjectTicket`, repository binding, execution-readiness claim, or Builder authorization.

`WorkbenchTicketProposalsController` exposes current/set/history reads plus explicit edit, reorder, dependency-safe remove, issue-resolution, and regeneration mutations. Reads and writes validate the exact active Workbench holder and lease epoch; writes additionally require the current proposal-set revision and an idempotent client operation. Each accepted human change appends a full canonical snapshot with actor, operation, fence, change kind, hash, and outbox evidence. Regeneration submits a new trusted proposal-purpose AgentRun against the exact set revision instead of calling the model or writing proposals in the controller transaction. The Workshop review panel reloads this durable aggregate independently of chat rendering, links provenance to persisted source messages, and states that proposals are not permanent tickets.

Allowed transitions (only):

```text
Discussion -> Document -> Ticket -> Review/Plan -> Proposal -> Build Run -> Evidence -> Review Package -> PausedForApproval
Chat -> Context Summary (or Ticket via documented route only)
```

Disallowed transitions:

- Chat directly calling proposal/build/run services
- Discussion path calling CLI or dogfood services
- Run profile, command list, timeout, or cleanup policy coming from client-supplied request data
- Run outputs writing directly to the active repository or non-disposable workspace

Any refactor touching one stage must update this matrix in this section before merge.

## Workshop Chat Modes

Chat completion must never assume governance intent. Mode authority is explicit and single-owner:

```text
User message
  -> IContextAgentRouteJudge / IContextAgentService  (context hints only)
  -> LlmChatModeClassifier                          (only governance-mode authority)
  -> LlmChatClarificationClassifier                  (only clarification authority)
  -> ProjectChatResponseService composer             (answers using selected mode)
  -> ChatGovernanceGate                              (single source for UI permissions)
  -> ChatTurnEnvelope / ChatMessage.Tags             (client replay bridge)
  -> ChatTurnGovernance / ChatTurnClarifications / ChatTurnTraces
                                                        (durable audit storage)
```

Hard rule: only `IChatModeClassifier` decides `Exploration`, `Formalization`, or `Confirmation`.
Hard rule: only `IChatClarificationClassifier` decides `ChatClarificationState`.

Allowed responsibilities:

- `IContextAgentRouteJudge` may emit request kind, confidence, evidence, risk, and `ContextModeHint` as a route hint.
- `IProjectChatResponseService` is the chat governance spine. It may validate mode classifier output by failing closed to `Confirmation`, run clarification classification, call the gate, call the composer, and attach metadata; it must not own context retrieval, LLM response composition, or trace formatting details.
- `ProjectChatContextPipeline` owns project/context lookup, route judging, context-agent execution, and route-signal assembly. It may fetch a broader context-agent slice and expose a smaller summary slice, but that split must be named and explicit.
- `ProjectChatResponseComposer` owns LLM composition, mode instruction injection, non-prose fallback text, and natural exploration clarification responses. It may pass selected mode instructions into the model, but the prompt must forbid leaking classifier names, confidence, route hints, gates, or internal policy machinery to the user unless explicitly asked.
- `ProjectChatResponseMetadataBuilder` owns context summaries, linked source projection, reasoning trace lines, reasoning summaries, and disambiguation text.
- Clarification fallback must be conservative: preserve explicit context questions as `GeneralScope`, return `None` when no clarification evidence exists, and use `GovernanceIntent` only when the selected mode has already failed closed to `Confirmation`.
- Clarification fallback must not mutate the selected governance mode or `ChatGovernanceGate`. Fallback-looking prose is trace text only; fallback evidence must be a typed audit field, not inferred by scanning `modeReason` or clarification reason text. Debt ticket `CHAT-AUDIT-FALLBACK-TYPED-001` tracks persisting a first-class fallback evidence column/source once fallback evidence becomes durable audit data.
- `ChatGovernanceGate` is the single source for action visibility.
- Tauri may render only from the backend gate payload and the local `chatGovernanceGate.ts` projection.
- Replay may restore the persisted envelope.

Forbidden responsibilities:

- `ChatController` must not infer, override, or translate route hints into governance mode.
- `ChatController` must not accept explicit governance mode as a bypass around `LlmChatModeClassifier`.
- Request kind values such as `CreateTicket`, `BuildTicket`, or `CreateTicketsFromDiscussion` must not become governance mode directly.
- `ContextRequiresClarification` must not force `Confirmation`; clarification is a separate classifier output.
- The response composer must not return a different mode while answering.
- The clarification classifier must not mutate the selected governance mode.
- React components must not contain their own mode/action policy checks.
- Legacy string tags such as `projectQuestion` must not be treated as replay mode authority.

Backend mode contract:

- `projectStateReview`: project review text and state summary.
- `projectQuestion` (default): classifier-led mode selection using context-route hints:
  - `Exploration` (default),
  - `Formalization` (explicit commitment language),
  - `Confirmation` (mixed or ambiguous intent).
- Route inference must add hint trace signals for UI and diagnostics, including confidence,
  request kind, context resolver flags, evidence use, and risk summary.

Response envelope rules:

- `Exploration`: no governance actions in UI, full reasoning trace/sum available.
- `Formalization`: governance gate allows handoff actions and the handoff path remains available.
- `Confirmation`: asks for lane confirmation before enabling formalization actions.
- Invalid or unparsable classifier output fails closed to `Confirmation`.

Client-facing UX rules:

- `Copy Markdown`, `Save Discussion`, and `View Sources` are only shown when `ChatGovernanceGate` allows them.
- `Exploration` and `Confirmation` suppress formalization actions.
- Reasoning visibility must remain honest and inspectable, including:
  - `modeReason`
  - `reasoningTrace` list
  - `reasoningSummary`
  - optional `disambiguationQuestion`
  - optional dogfood trace references.
- Chat history replay must not infer mode from an empty default. Persisted assistant messages must carry mode, clarification, and gate metadata in `ChatMessage.Tags` as a versioned JSON envelope (`v:1`) and UI mapping must reconstruct `mode`, `clarification`, and governance affordances from this envelope without backend recompute. Backend persistence also normalizes saved assistant envelopes into `ChatTurnGovernance`, `ChatTurnClarifications`, and `ChatTurnTraces`; tags are a replay bridge, not the permanent audit design.
- Chat history inspection must prefer durable audit rows from `ChatTurnGovernance`, `ChatTurnClarifications`, and `ChatTurnTraces`. `ChatMessage.Tags` may be used only as a clearly labeled replay fallback when durable audit rows are absent; UI must not present Tags fallback as durable audit.
- Chat turn audit reads are project/session/message scoped through the API and must not recompute mode, clarification, or gate on replay.
- Chat turn audit tables are schema-owned, not runtime-created. `Database/migrate_chat_turn_audit.sql`, `Database/local_dev_setup.sql`, and `Database/rebuild_db.sql` own creation of `ChatTurnGovernance`, `ChatTurnClarifications`, and `ChatTurnTraces`; `ChatTurnPersistenceService` assumes the schema exists and fails loudly if it does not.
- `ChatHistoryService.SaveMessageAsync` owns the chat persistence transaction boundary. Assistant message insert, session timestamp update, and normalized audit writes commit or roll back together. Delete/reinsert audit refresh is allowed only inside that transaction.
- Slice 4 UI replay may hydrate durable audit rows per assistant message only within the bounded current history page. Debt ticket `CHAT-AUDIT-BATCH-001` must replace this with `GET /api/projects/{projectId}/chat/sessions/{sessionId}/audit` before the replay surface expands beyond the current page.
- Audit source and fallback labels belong in trace/inspection surfaces such as reasoning details and side panels. Normal chat body content must not be flooded with audit implementation labels.

Mode inference invariants:

- The backend must drive mode from `IChatModeClassifier` only.
- `IChatClarificationClassifier` owns `ChatClarificationState`; clarification must not mutate `ChatGovernanceMode`.
- Clarification fallback preserves evidence conservatively and cannot change the gate. A `Confirmation` fallback may produce `GovernanceIntent` only to ask the lane question, never to enable governance actions.
- The context router is a scout. Its `ContextModeHint` value is a hint, not authority.
- A `CreateTicket`/`CreateTicketsFromDiscussion` route hint without explicit lane-lock language must not trigger governance actions.
- Missing mode is treated as unknown for reconstruction; UI must still behave in conservative exploration mode until explicit mode metadata is present.
- Missing durable audit is treated as an audit gap, not permission to infer. UI may display versioned `ChatMessage.Tags` as "Tags replay fallback"; legacy string tags remain opaque and cannot enable governance actions.

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
- API-facing workflow methods for product clients

`IronDev.Api` owns:

- auth and tenant selection
- project, ticket, document, memory, chat, code-index, build, run-report, and profile endpoints
- API-backed report endpoints, product-shaped run status/report/event endpoints, a ticket build-run starter, and SQL-backed live run event history; durable workflow state beyond event history is still planned
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

Forward clients may keep desktop-local behaviour only when it does not persist product state:

- `IAppSettingsService` for client presentation/settings preferences
- screenshot capture and testing companion local files
- shell/window/navigation state
- in-memory trace display state
- markdown rendering fallback for document preview
- prompt playground or diagnostics shims only when they remain non-authoritative

These exceptions must not persist product state directly to SQL or Infrastructure services. Results that become persistent product state must be sent through `IronDev.Api`.

## Current Boundary Verification

- `IronDev.Client` has no `IronDev.Infrastructure` project reference.
- `tools/IronDev.Cli` references `IronDev.Client`, does not reference `IronDev.Infrastructure`, and does not construct direct `HttpClient` calls.
- `IronDev.TauriShell` is the IronDev desktop app. It calls API routes and generated OpenAPI types; it does not reference Infrastructure, SQL, repositories, or Weaviate directly.
- `IronDeveloper` WPF is removed from the repository and from `IronDev.slnx`.

The executable boundary checks live in `IronDev.IntegrationTests/ApiBoundaryTests.cs`.

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
| `DevelopmentTenantContext` | Legacy local/test migration seam only | Always `TenantId = 1`; do not use from forward clients |

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
| `IronDev.Client` first | Typed HTTP abstraction between forward clients and API; current Product CLI ticket/run commands use it |
| WPF retired | The WPF project is removed; new product shell work targets API/client/CLI/Tauri |
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

The governed tool loop allows PlannerAgent to request named capabilities, the tool registry to check capability boundaries and runtime profiles, safe tools to collect evidence, deterministic validators to review evidence sufficiency, PlannerAgent to revise the plan, and a human escalation gate to record whether review or more evidence is required. Passive review agents are not on this path unless an ADR explicitly justifies them.

BuilderAgent remains caged. It may write only inside explicit disposable workspaces with evidence. Real repository writes remain blocked unless the reviewed promotion path supplies trace, promotion package, proposed change, approval, build/test/quality, ConscienceAgent, and ThoughtLedger evidence.

## Governed Tools

Governed tools are typed, deterministic capabilities behind `IGovernedToolRegistry`. They are not agents, and they are not generic wrappers around agent behaviour.

Rules:

- Tool requests use `GovernedToolRequest<TInput>` with a concrete input type.
- Tools return `GovernedToolResult<TOutput>` with structured output.
- Tool policy is evaluated by `GovernedToolPolicyEvaluator` before the tool body runs.
- Unknown tools, disallowed callers, mutation-capable tools, and nested tool calls fail closed.
- Individual tools must not call agents or other tools.
- Review/check behaviour defaults to a governed tool or service, not a new passive agent.
- Passive agents are not added without an ADR.

The first governed tool is `code_standards.analyse_patch`. It is a read-only Code Standards tool, not a `CodeStandardsAgent`. BuilderAgent and TesterAgent may request it through the registry. The tool does not write files, run commands, mutate tickets, mutate memory, use the network, approve changes, or execute nested tool calls.

The active Alpha execution order is tracked in [ALPHA_RELEASE_EXECUTION_ORDER.md](ALPHA_RELEASE_EXECUTION_ORDER.md). Passive-agent containment for the governed tool path is recorded in [ADR_018_PASSIVE_AGENT_CONTAINMENT.md](ADR_018_PASSIVE_AGENT_CONTAINMENT.md).
