# Cockpit Backend Contract

IronDev's cockpit API is project-scoped by default. Any endpoint that returns ticket work, run evidence, memory, documents, or decisions must prove the route project owns the returned object.

## Contract Rules

### Workbench V2 agent runs

- `POST /api/workbench/projects/{projectId}/inputs` is the authoritative slash-command boundary. It parses the first meaningful composer token before BA readiness or AgentRun submission. Only `/help` and `/ticket` are accepted, case-insensitively and without aliases; ordinary prose remains conversation.
- `/help` and unknown slash tokens create no chat session, message, AgentRun, attempt, or provider call. An unknown token returns `400 workbench_command_unknown` after a fenced, idempotent `WorkbenchCommandRejection` write. The durable rejection/outbox payload contains the raw token and SHA-256 payload hash, never the full rejected composer text.
- `/ticket` is a trusted proposal-purpose Business Analyst AgentRun, not a client-selected role or a permanent-ticket write. Its frozen schema-version-3 output is valid only as one to five ordered, source-backed `Ready` proposals or zero proposals plus at least one open question or potential conflict in `NeedsInput`. Generation atomically materializes the terminal run, assistant response, current proposal aggregate, immutable full-snapshot revision, attribution, and outbox evidence. Replay creates no duplicate visible output.
- Proposal reads and review mutations require the exact active Workbench holder and lease epoch. Edit, reorder, dependency-safe remove, issue resolution, and regeneration also require the expected proposal-set revision and an idempotent client operation. Every accepted review change appends a complete canonical snapshot; regeneration submits a new trusted AgentRun against the exact set revision. None of these routes creates a `ProjectTicket`, repository binding, readiness claim, execution authorization, or Builder run.
- The legacy AgentRun submit endpoint rejects any leading-slash candidate with `400 workbench_input_route_required` so command handling cannot be bypassed.

- `POST /api/workbench/projects/{projectId}/agent-runs` accepts only `workbenchSessionId`, `leaseEpoch`, `clientOperationId`, `chatSessionId`, and the user `message`. The client cannot supply prompt context, understanding state, repository context, role, model output, or an assistant message.
- Submission returns `202 Accepted` with the durable run and source-user-message identities. Reusing the same scoped operation and payload returns the same identities; changed payload returns `409 operation_id_payload_mismatch`.
- `GET /api/workbench/projects/{projectId}/agent-runs/current?workbenchSessionId=...&leaseEpoch=...&chatSessionId=...` validates the exact current fence and optional chat binding, reports project-scoped worker/profile/provider readiness without making a provider request, returns the bound chat identity, and returns both the active run and latest bound run when present. Omitting `chatSessionId` is the repository- and conversation-independent preflight used before first-session creation. The latest-run snapshot preserves bounded terminal failure/cancellation state across reload.
- `GET /api/workbench/projects/{projectId}/agent-runs/{agentRunId}` exposes state/progress plus only a bounded failure category and retryability decision. It must not expose raw prompts, context snapshots, provider output, diagnostic codes/hashes, secrets, or restricted diagnostics.
- `POST /api/workbench/projects/{projectId}/agent-runs/{agentRunId}/cancel` requires the current session and lease epoch and is operation-journal idempotent.
- A conflicting submit returns `409 workbench_agent_run_active` with the bounded active run ID so the client can recover instead of creating a duplicate. Worker or deterministically invalid effective Analyst profile/provider/credential/model/role-adapter configuration returns `503 workbench_agent_run_unavailable` before the lease is renewed or any chat session, user message, run, or outbox event is written. Live provider reachability remains a bounded run-time outcome.
- Terminal failure is not automatically replayed in this slice. `retryable=false` means the failed immutable attempt has no retry endpoint; the user may author a new turn after readiness is restored.
- Allowed run states are `Pending`, `Running`, `NeedsInput`, `Completed`, `Failed`, `Cancelled`, `Superseded`, and `Stale`; `NeedsInput` and `Completed` are terminal for that invocation.
- Every agent-authored materialization carries the initiating actor, run ID, agent version, prompt version, tool-policy version, output-schema version, captured understanding revision, and server context hash.
- At-least-once agent invocation is allowed. The unique assistant-message relationship plus the locked materialization transaction provides exactly-once visible output.
- Takeover supersedes old-epoch pending/running work and requests cancellation atomically with the new lease. A late result is restricted diagnostic evidence only.
- Prepared provider requests preserve authority roles: code-owned contract and safety policy are `System`, configured profile instructions are advisory `Developer`, and project context/conversation text are untrusted `User`. One versioned aggregate request budget is enforced before provider selection and invocation; a single user message is limited to 20,000 characters and provider output is capped at the reserved 16,000-token/UTF-8-byte/assistant-character boundary. Providers that cannot preserve the role envelope fail closed.
- Attempt telemetry is append-only and contains only safe metadata such as status, duration, reported usage, correlation/provider request IDs, and hashes. Raw prompts, completions, secrets, and private reasoning are forbidden.
- When `WorkbenchV2:ConversationAuthorityEnabled=true`, V2 Workshop conversation writes go only through AgentRun. The legacy synchronous completion route, direct chat-message mutation, existing-session update, and session deletion return `409 workbench_conversation_authority_required`; project-scoped chat/session reads and idempotent session creation remain available. Session create uses its scoped `clientOperationId` as a durable operation receipt, so a post-commit transport retry returns the same session ID. When the flag is false, compatibility behavior is unchanged.
- A durable Workshop mutation discards its client operation receipt only after a definitive application rejection: HTTP `400`, `401`, `403`, or `404`; a recognized domain `409`; or structured `503 workbench_agent_run_unavailable` for submission. Network errors, generic or malformed `5xx`, timeouts/throttling, unknown conflicts, truncated bodies, and invalid success envelopes are ambiguous. They retain the exact operation ID and normalized payload, fence altered work/navigation, and permit only exact authoritative replay. This applies independently to conversation creation, AgentRun submission, and cancellation.
- Project Understanding is the revisioned authority for product intent. Agent output schema version 2 can propose only bounded `Inferred` or explicitly evidenced `Confirmed` fact changes plus an optional rename proposal. Domain materialization validates source messages, preserves locked/confirmed values, creates explicit conflicts, and commits all visible output exactly once with the AgentRun. Persisted schema-version-1 runs remain executable but cannot mutate understanding.
- Project Context may display lifecycle, readiness, and repository state beside understanding only as authority-labelled read-only projections. User fact edits, locks, conflict resolutions, and rename acceptance are current-session, lease-fenced, expected-revision, idempotent Workbench mutations. Rename acceptance changes only the canonical project name; it grants no repository or execution authority.
- `POST /api/workbench/projects/{projectId}/builder/work-packages` accepts only the current Workbench session/lease, one idempotency key, and ordered permanent ticket IDs. The server requires Delivery plus current nine-gate readiness and freezes the exact Work Item contracts, acceptance criteria, permitted files, repository/readiness/index/model/sandbox authority, and code-owned Builder contract versions into canonical hash-bound context. `POST .../builder/authorizations` accepts only that package ID and expected hash, revalidates currentness, and creates a fifteen-minute single-use authorization. These PR-07A routes create no AgentRun, prompt, provider request, sandbox execution, patch, or repository write; PR-07B owns atomic authorization consumption and prompt preparation.
- `POST /api/workbench/projects/{projectId}/builder/agent-runs` accepts only the current Workbench session/lease, one idempotency key, the exact single-use authorization ID, work-package core ID, and expected core hash. In one serializable transaction it revalidates current authority, creates a durable prepared Builder AgentRun, freezes the effective profile and exact role context, records prompt/tool/context/provider-input hashes, and consumes the authorization. Success permits a later provider invocation but does not perform one; `ProviderInvokedAtUtc` remains null and no Builder tool or repository write occurs.

#### Real-provider and default-on gates

`WorkbenchV2:ConversationAuthorityEnabled` remains default-off. The aggregate 128k/16k request budget is a fail-closed envelope, not proof that an arbitrarily configured model supports that capacity. Purchased-token or default-on use is blocked until the enabling slice owns both of these contracts:

- a reviewed model-capability record that pins provider, model, context window, maximum output, tokenizer policy, supported authority roles, and required structured-output capability, with request budgets derived from that record; and
- server-owned conversation compaction/summary with provenance, or a Workbench-session rollover that carries forward server-owned Project Understanding, so context exhaustion has a recovery corridor.

PR-02C-A retains ambiguous receipts only for the current mounted Workshop client instance. Persistence across a page reload, client remount, or complete browser-process loss is not claimed by this feature-gated preview and must be resolved before default-on use if uninterrupted replay across client restart is required.

- Prefer `/api/projects/{projectId}/...` routes for cockpit workflows.
- Do not use global run identifiers as sufficient authorization or ownership proof.
- Run proposals must be validated first: `CodeProposalValidator` must pass before any disposable run executes build/run commands.
- Chat completion is mode-aware:
  - `projectQuestion` defaults to classifier-based `Exploration`, `Formalization`, or `Confirmation`.
  - The controller accepts only `projectQuestion` and `projectStateReview`; explicit governance modes are not API request kinds.
  - Context routing runs through `IContextAgentRouteJudge` and `IContextAgentService` for hints, evidence, and trace diagnostics only.
  - `LlmChatModeClassifier` is the single authority for the final governance mode.
  - `LlmChatClarificationClassifier` is the single authority for clarification kind/questions.
  - Mode and clarification use prompt-constrained classifier JSON with strict validation, not provider-enforced structured output.
  - Debt ticket `CHAT-GOV-STRUCTURED-OUTPUT-001` tracks replacing brace-extracted JSON with provider-enforced JSON/schema output when `ILLMService` supports it.
  - Clarification is separate from governance mode and must not force `Confirmation`.
  - `ProjectChatResponseService` is the orchestration spine only: context lookup/routing lives in `ProjectChatContextPipeline`, answer text lives in `ProjectChatResponseComposer`, and trace/context projection lives in `ProjectChatResponseMetadataBuilder`.
  - `ProjectChatContextPipeline` keeps the broad context-agent slice separate from the smaller response-summary slice; duplicate context fetches should not hide inside collaborator methods.
  - `ProjectChatResponseComposer` composes the answer using the classifier-selected mode and must not reclassify the turn or leak internal governance/classifier details into user-facing prose unless explicitly asked.
  - `ChatGovernanceGate` is the single source for UI permissions.
  - `projectStateReview` remains explicit project-review behavior.
  - Backend returns full response metadata in `ChatCompletionResponse` for persisted reconstruction and UI replay:
    - `mode` (Exploration | Formalization | Confirmation)
    - `modeConfidence`
    - `modeReason`
    - `clarification`
    - `gate`
    - `reasoningTrace`
    - `reasoningSummary`
    - `disambiguationQuestion`
    - `dogfoodTraceId`
    - `dogfoodTracePath`
    - `contextSummary`
    - `linkedFilePaths`
    - `linkedSymbols`
  - `Exploration` may not return governance actions.
  - `Confirmation` requires explicit lane choice before exposing governance affordances.
  - `Formalization` may return governance affordances.
    - `reasoningTrace`, `reasoningSummary`, `disambiguationQuestion`, and route signals must be present for inspectability.
  - `CreateTicket` or `CreateTicketsFromDiscussion` route hints without explicit formalization language must not trigger governance actions.
  - Mode may be `Exploration`/`Formalization`/`Confirmation`; unknown mode values are treated as conservatively non-governance in clients.
- Chat history persistence uses the `ChatMessage.Tags` column to store assistant metadata:
  - The shell stores assistant mode, clarification, gate, and trace metadata as a versioned JSON envelope in `Tags` (`{ "v": 1, ... }`).
  - Replays may parse this envelope only as a labeled fallback when durable audit rows are unavailable.
  - Legacy non-JSON `tags` should be treated as opaque and must not infer mode.
- Chat turn audit persistence is normalized:
  - `ChatHistoryService.SaveMessageAsync` persists assistant envelope state into `ChatTurnGovernance`, `ChatTurnClarifications`, and `ChatTurnTraces`.
  - These tables are the durable audit path for governance mode, clarification, gate, and trace pointers.
  - `GET /api/projects/{projectId}/chat/sessions/{sessionId}/messages/{messageId}/audit` returns the durable audit snapshot for a specific chat turn and proves tenant/project/session/message scope.
  - UI trace/sidebar surfaces must prefer this durable audit endpoint and clearly label `ChatMessage.Tags` as replay fallback when used.
  - Audit tables are created by `Database/migrate_chat_turn_audit.sql`, `Database/local_dev_setup.sql`, or `Database/rebuild_db.sql`; runtime services must not create them.
  - Assistant message insert, session timestamp update, and normalized turn writes share one transaction.
  - Clarification fallback may be described in trace text, but fallback evidence must be typed audit data. The backend and UI must not scan `modeReason` or clarification reason prose to infer fallback state.
  - `ChatMessage.Tags` remains a client replay bridge, not the permanent storage design.
  - Slice 4 client replay may hydrate durable audit rows per assistant message only while bounded to the current history page. Follow-up `CHAT-AUDIT-BATCH-001` owns a session-scoped batch audit endpoint.
  - Follow-up `CHAT-AUDIT-FALLBACK-TYPED-001` owns durable fallback evidence persistence once fallback evidence needs to survive as auditable state.
  - Audit source/fallback labels belong in trace and inspection panels, not normal chat answer text.
- A valid `dogfoodTraceId` is the single pointer to route/trace records for route decisions and reasoning.
- A ticket-scoped run endpoint must verify:
  - the ticket exists;
  - the ticket belongs to the route project;
  - the run has persisted event metadata linking it to the same project and ticket.
- Missing or mismatched ownership returns `404` rather than leaking whether a run exists elsewhere.
- UI actions must remain disabled or show honest errors until backed by these routes.

## Alpha Spine

### Projects

- `GET /api/projects`
- `GET /api/projects/{projectId}`
- `POST /api/projects`
- `PATCH /api/projects/{projectId}`

### Tickets

- `GET /api/projects/{projectId}/tickets`
- `GET /api/projects/{projectId}/tickets/{ticketId}`
- `POST /api/projects/{projectId}/tickets`
- `PATCH /api/projects/{projectId}/tickets/{ticketId}`
- `POST /api/projects/{projectId}/tickets/{ticketId}/archive`

### Ticket Evidence

- `GET /api/projects/{projectId}/tickets/{ticketId}/evidence-summary`

Evidence summary is backend-backed and links runs only from trusted persisted run-event metadata.

### Ticket Build Runs

- `POST /api/projects/{projectId}/tickets/{ticketId}/build-runs`
- `POST /api/projects/{projectId}/tickets/{ticketId}/build-runs/disposable`
- `GET /api/projects/{projectId}/tickets/{ticketId}/build-runs`
- `GET /api/projects/{projectId}/tickets/{ticketId}/build-runs/{runId}`
- `GET /api/projects/{projectId}/tickets/{ticketId}/build-runs/{runId}/review`

The `/disposable` route is the alpha cockpit route. The older `POST .../build-runs` route remains as a compatibility alias for existing clients.

Durable run state is the source of truth for ticket build runs. Run events are child evidence records tied to the run id. File-backed run reports remain readable as projections/evidence, not the canonical lifecycle record.

Disposable ticket build execution is backend-owned. Clients may request "start a disposable run for this ticket"; they must not supply source repository paths, workspace roots, command lists, cleanup policy, timeout policy, or evidence retention policy. The backend resolves those from trusted project/configuration state, creates the durable run, creates the disposable workspace, executes the allowed command profile, persists command stdout/stderr evidence, and preserves failed workspaces or failure bundles for debugging.

### Discussion-To-Code Proposal Loop

- `POST /api/projects/{projectId}/discussions`
- `POST /api/projects/{projectId}/documents/{documentVersionId}/tickets`
- `POST /api/projects/{projectId}/tickets/{ticketId}/review`
- `POST /api/projects/{projectId}/tickets/{ticketId}/disposable-code-runs`
- `GET /api/projects/{projectId}/tickets/{ticketId}/build-runs/{runId}/review-package`
- `GET /api/projects/{projectId}/code-scenarios`

This is the backend proposal/run/review-package spine. `ICodeProposalGenerator` creates a `CodeProposal` with generated files, expected output, and a backend-owned build/run profile. `IDisposableCodeRunService` executes that proposal in a disposable workspace. `IRunReviewPackageService` assembles review evidence from run state, persisted events, generated files, command logs, code standards output, and output verification.

Hardening rule:

- Proposal and proposal-evidence gates are first-class behavior:
  - `IDisposableCodeRunService` must persist `ticket-review.json`, `code-proposal.json`, `code-proposal-validation.json` before executing any build command.
  - A proposal that fails `ICodeProposalValidator` must emit `CodeProposalRejected`, transition the run to `Failed`, and never run `dotnet` commands or governed code standards checks.
  - Evidence evidence should remain in the durable run evidence root for failed runs; this is the source of truth for hardening and audit.

Proposal generation is configurable:

- `CodeProposal:Mode=Deterministic` keeps LocalTest and repeatable scenario smoke runs stable.
- `CodeProposal:Mode=ModelAssisted` uses `ModelAssistedCodeProposalGenerator` behind the same `ICodeProposalGenerator` contract.

Model-assisted output is not trusted. It must deserialize into `CodeProposal`, use an allow-listed runtime profile, pass `ICodeProposalValidator`, and then execute through the same disposable run service. The model cannot supply arbitrary commands, absolute paths, workspace roots, cleanup policy, or apply behavior. Invalid model output creates a durable failed run with persisted validation evidence.

Backend-owned runtime profiles currently include:

- `dotnet.console`: allows `StdoutContains` and `CommandExitZero` verification.
- `dotnet.aspnet`: allows `HttpGetEquals` verification.

#### No More Fake Shit Rule

A scenario only counts if it proves the reusable spine:

`discussion/chat -> document -> ticket -> review/plan -> code proposal -> disposable workspace -> build/run/test -> evidence -> review package -> PausedForApproval`

Scenario-specific services, endpoints, or orchestrators are not allowed. If a scenario requires `HelloWorldCodeRunService`, `CalculatorCodeRunService`, or `HealthApiCodeRunService`, the design is failing.

The only acceptable scenario-specific parts are discussion text, runtime profile, verification rules, expected output, acceptance checks, and seed files. Everything else must use the same product spine.

Current scenario fixtures:

- `console.hello-world`: builds and runs a tiny console app, verifies stdout contains `Hello from IronDev Alpha`.
- `console.calculator`: builds and runs one console app, verifies `add 2 3` prints `5` and `subtract 10 4` prints `6`.
- `aspnet.health-api`: builds an ASP.NET Core app, starts a disposable server process, verifies `GET /health` returns `healthy`, and stops the process.

These scenarios are not Alpha-specific product services, they are not agent debate, and they must not mutate or apply generated code to the real repository. Successful execution ends in `PausedForApproval`.

The CLI scenario runner must use the same project-scoped spine:

- `irondev scenario list --project-id <id>`
- `irondev scenario run <scenario-id> --project-id <id>`
- `irondev scenario report <run-id> --project-id <id> --ticket-id <id>`

`scenario run` is a convenience wrapper over the real discussion/document/ticket/review/proposal/disposable-run/review-package path. It must not call scenario-specific services.

### Documents

- `GET /api/projects/{projectId}/documents`
- `GET /api/projects/{projectId}/documents/{documentId}`
- `POST /api/projects/{projectId}/documents`
- `PATCH /api/projects/{projectId}/documents/{documentId}` planned
- `POST /api/projects/{projectId}/documents/{documentId}/archive`
- `GET /api/projects/{projectId}/documents/{documentId}/versions`
- `GET /api/projects/{projectId}/documents/{documentId}/versions/current`

### Decisions

- `GET /api/projects/{projectId}/decisions`
- `GET /api/projects/{projectId}/decisions/{decisionId}`
- `POST /api/projects/{projectId}/decisions`
- `PATCH /api/projects/{projectId}/decisions/{decisionId}`
- `POST /api/projects/{projectId}/decisions/{decisionId}/supersede`
- `POST /api/projects/{projectId}/decisions/{decisionId}/archive`

Decision endpoints enforce the route project before returning, updating, superseding, or archiving a decision.

### Memory And Retrieval

- `GET /api/projects/{projectId}/memory/search` exists
- `POST /api/projects/{projectId}/memory/search`
- `GET /api/projects/{projectId}/memory/traces/{traceId}` planned
- `POST /api/projects/{projectId}/memory/reindex`
- `GET /api/projects/{projectId}/memory/status`

### Health And Environment

- `GET /health`
- `GET /api/environment`
- `GET /api/projects/{projectId}/services/status`

## Legacy Surfaces

Global run endpoints still exist for compatibility and SSE consumers:

- `GET /api/runs/{runId}`
- `GET /api/runs/{runId}/report`
- `GET /api/runs/{runId}/events`
- `GET /api/run-reports`

Do not add new cockpit behavior to global run routes. New cockpit UI should use project-scoped ticket run endpoints.
