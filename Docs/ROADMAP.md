# IronDev Roadmap

## Phase 0 — Prototype → Real Architecture

**Status: Complete**

Established the core product shape. WPF client with working chat, persistent memory, decisions, summaries, tickets, code indexing, and a workbench sandbox for AI-generated drafts.

---

## Sprint 1 — Tenancy Foundation

**Status: Complete ✅**

Goal: establish multi-tenant schema and tenant-safe service layer without breaking the WPF app.

### Delivered
- `Tenants`, `Users`, `TenantUsers` tables in schema
- `TenantId` column on all domain tables (`Projects`, `ChatMessages`, `ProjectSummaries`, `ProjectDecisions`, `ProjectTickets`, `ProjectFiles`)
- `ICurrentTenantContext` abstraction (scoped, not singleton)
- `DevelopmentTenantContext` for WPF local dev continuity
- `ProjectService` (new) — tenant-scoped CRUD
- `TicketService` — ownership guard + tenant filter
- `ChatHistoryService` — ownership guard + tenant filter
- `ProjectMemoryService` — ownership guard + tenant filter
- `CodeIndexService` — tenant stamp on insert, tenant-aware uniqueness checks
- `TestTenantContext` (switchable) for integration tests
- `TenantIsolationIntegrationTests` — 8 isolation + 2 ownership guard tests
- `integration.runsettings` — sequential execution for shared DB tests

### Baseline: 30 integration/workflow tests passing as of 2026-04-17 ✅
- Verified on branch `chore/stabilize-baseline` before Sprint 2 kickoff.
- Tag: `milestone-sprint1_5-stabilized`

---

## Sprint 1.5 — Stabilization & Tidy

**Status: Complete ✅**

Goal: put the project in good stead before auth sprint.

### Delivered
- Removed redundant DB scripts; `rebuild_db.sql` is the single source of truth.
- Normalized configuration: generic `appsettings.json` with machine-specific `appsettings.Development.json` for API and WPF.
- Updated `README.md`, `ROADMAP.md`, `ARCHITECTURE.md`, `TESTING.md`.
- Milestone: baseline project stability reached with workbench and code awareness.

---

## Sprint 2 — Auth, Login, JWT, Request-Scoped Tenancy

**Status: Planned 🔄**

Goal: replace `DevelopmentTenantContext` in the API path with real JWT-authenticated, request-scoped tenant context.

### Backlog
- [ ] **Auth Schema**: Add `Users`, `Tenants`, `TenantUsers` to production schema & seed dev user.
- [ ] **Password Validation**: Implementation BCrypt hashing and user credential checks.
- [ ] **Login Endpoint**: `POST /api/auth/login` to issue base JWT.
- [ ] **Tenant Selection**: `POST /api/tenants/select` to re-issue JWT with `tenant_id` claim.
- [ ] **Request Context**: Implement `JwtTenantContext` reading from token claims.
- [ ] **Auth Integration Tests**: Verify login flow, tenant isolation, and blocked unauthenticated access.
- [ ] **WPF Login Flow**: Add login dialog and token storage in the client.

### WPF stays on `DevelopmentTenantContext` this sprint
Migration to HTTP calls is Sprint 3.

---

## Sprint 3 — API Domain Endpoints + WPF HTTP Migration

**Status: Planned**

Goal: expose domain data through REST, begin migrating the WPF client off direct service calls.

### Planned
- REST endpoints: projects, tickets, chat, summaries, decisions
- WPF `HttpApiClient` wrapper replacing direct service injection
- WPF login screen → calls `/api/auth/login`
- WPF tenant selection → calls `/api/tenants/select`
- Session/token storage in WPF client

---

## Sprint 4 — Code-Aware Generation Loop

**Status: Planned**

Goal: make IronDev actually act on a project — not just store memory.

### Planned
- Ticket → implementation plan → target file selection
- Controlled code generation within workbench
- Local build/test execution from within the workbench
- Result feedback loop (build errors → re-generation)

---

## Guiding rules

1. Every new backend function gets one happy-path + one isolation/guard integration test.
2. Every new endpoint gets one success test, one auth test, one tenant test.
3. WPF stays working at all times — no breaking the client to serve backend work.
4. Secrets stay out of source. Always.

## Self-Improvement Campaign 157

**Status: Delivered**

Goal: mature the governed autonomy control plane while preserving the safety boundary.

Delivered in this campaign:

- `Docs/AGENTS.md` as the current agent-layer source of truth.
- Runtime-configurable agent profiles for OpenAI, LocalOpenAI, and Ollama.
- Governed ArchitectAgent review path.
- Campaign smoke that reports child-ticket maturity evidence for IRONDEV-144 through IRONDEV-156.

Still blocked:

- Real repository writes.
- Ungated autonomy.
- Agent self-approval.
- ResearchAgent overriding accepted project memory.
- SentinelAgent creating tickets or patches.

## Live Governed Agent Execution 158

**Status: Delivered**

Goal: prove one agent can make an opt-in live model call through configured profiles while preserving deterministic fallback and hard governance boundaries.

Delivered in this slice:

- `ArchitectAgent` can attempt live model execution only when explicitly enabled.
- `AgentLlmClient` maps configured profiles to OpenAI, LocalOpenAI, and Ollama services.
- `campaign live-governed-agent-158` records fallback, live attempt, and missing-evidence behaviour.

Still blocked:

- Real repository writes.
- Memory mutation.
- Ticket creation.
- Patch application.
- Agent self-approval.

## Live Critic And Planner Agents 159

**Status: Delivered**

Goal: extend opt-in live model execution to CriticAgent and PlannerAgent while preserving deterministic fallback and hard governance boundaries.

Delivered in this slice:

- `CriticAgent` can attempt live model execution only when explicitly enabled during failure-package review.
- `PlannerAgent` can attempt live model execution only when explicitly enabled during product-spike intake or test-plan drafting.
- `campaign live-critic-planner-159` records deterministic fallback, live-provider attempts, and blocked mutation authority.

Still blocked:

- Real repository writes.
- Memory mutation.
- Ticket creation.
- Patch application.
- Agent self-approval.

## Live Retriever And Sentinel Agents 160

**Status: Delivered**

Goal: extend opt-in live model execution to RetrieverAgent and SentinelAgent while preserving deterministic memory ranking, project scope, insight classification, and hard governance boundaries.

Delivered in this slice:

- `RetrieverAgent` can attempt live model execution only when explicitly enabled after deterministic memory search and weighted context packaging.
- `SentinelAgent` can attempt live model execution only when explicitly enabled after deterministic insight classification.
- `campaign live-retriever-sentinel-160` records deterministic fallback, live-provider attempts, and blocked mutation/ranking authority.

Still blocked:

- Memory ranking override.
- Cross-project context mixing.
- Real repository writes.
- Memory mutation.
- Ticket creation.
- Patch application.
- Agent self-approval.

## Live Remaining Governed Agents 161

**Status: Delivered**

Goal: complete the current useful opt-in live governed agent pass for ResearchAgent, QualityAgent, and SupervisorAgent.

Delivered in this slice:

- `ResearchAgent` can attempt live model execution only after explicit external evidence is packaged.
- `QualityAgent` can attempt live model execution only after deterministic quality evidence is produced.
- `SupervisorAgent` can attempt live model execution only after deterministic orchestration state is known.
- `campaign live-remaining-agents-161` records deterministic fallback, live-provider attempts, and blocked mutation/override authority.

Still blocked:

- Real repository writes.
- Memory mutation.
- Ticket creation.
- Patch application.
- Quality gate override.
- ConscienceAgent bypass.
- ThoughtLedger bypass.
- Agent self-approval.

## Governed Planner/Critic Tool Loop 162-167

**Status: Delivered**

Goal: make agents evidence-seeking instead of single-response helpers by adding a native governed tool contract, trace output, human escalation, evidence validation, and language-agnostic runtime profiles.

Delivered in this slice:

- `AgentToolCapability`, `AgentToolRequest`, `AgentToolResult`, `AgentLoopTrace`, `EvidenceValidationResult`, `HumanEscalationGate`, and `PlannerCriticLoopResult` contracts.
- `GovernedToolRegistry` with safe read/test/report capabilities.
- `GovernedPlannerCriticLoopService` for Planner -> tools -> Critic -> revised plan.
- `agent loop plan-review` product command.
- `campaign governed-tool-loop-162-167` regression command.
- Runtime profile contracts for `.NET`, Node, and Python.

Still blocked:

- Raw command execution directly by agents.
- Real repository writes.
- Memory mutation.
- Ticket creation without review.
- Patch application.
- ConscienceAgent bypass.
- ThoughtLedger bypass.
- Agent self-approval.

## Loop-Gated Disposable Build 168

**Status: Delivered**

Goal: make `build disposable run` earn its living by turning a messy product prompt into run-scoped docs, governed Planner/Critic evidence, a caged BuilderAgent repair build, QualityAgent evidence, and one final report.

Delivered in this slice:

- `build disposable run --project Solitaire --goal "I want build solitaire"` product-shaped command.
- `campaign loop-gated-disposable-build-168` dogfood regression command.
- Run-scoped intake, build brief, and ticket draft documents.
- Governed Planner/Critic tool loop before build execution.
- Trace-backed BuilderAgent repair loop inside the disposable workspace.
- QualityAgent/Killjoy gate after the caged build.
- Final evidence envelope with real repo mutation count, disposable file count, evidence refs, and recommendation.

Still blocked:

- Real repository writes.
- Accepted memory mutation from the command.
- Ticket acceptance.
- Promotion approval.
- ConscienceAgent or ThoughtLedger bypass.
- BuilderAgent self-approval.

## Promotion Package And Language Runtime Spine 169

**Status: Delivered**

Goal: create the bridge from a successful disposable build to reviewed promotable work without applying anything.

Delivered in this slice:

- `ProposedChange` case-file model.
- `PromotionPackage` review evidence model.
- `ILanguageRuntimeRegistry` and `LanguageRuntimeProfile`.
- executable `csharp-dotnet` runtime profile.
- contract-only `java-maven`, `typescript-node`, and `python-pytest` profiles marked `NotExecutableYet`.
- `promotion package create` command.
- `campaign promotion-package-169` dogfood regression command.
- promotable/blocked file classification for disposable Solitaire output.

Still blocked:

- Real repository writes.
- Branch/worktree apply.
- Accepted memory mutation.
- Ticket acceptance.
- Promotion approval.
- Non-C# runtime execution.
- Agent self-approval.

## Isolated Promotion Apply Proof 170

**Status: Delivered**

Goal: consume a `PromotionPackage` and prove it can become an isolated candidate workspace without writing main.

Delivered in this slice:

- `IsolatedPromotionApplyReport` model.
- `promotion apply isolated --package-run-id <run> --run-id <apply-run> --json`.
- `campaign isolated-promotion-apply-170 --run-id <run> --json`.
- isolated candidate workspace outside the active repo.
- isolated candidate branch marker.
- copy only `FilesToPromote`.
- reject `FilesBlocked`.
- C#/.NET build/test in the isolated workspace.
- active repo mutation count proof.
- JSON/Markdown isolated apply reports.

Still blocked:

- Real repository writes.
- Main branch writes.
- Pull request creation.
- Auto-merge.
- Accepted memory mutation.
- Ticket acceptance.
- Promotion approval.
- Agent self-approval.

## Promotion Review Cockpit 171

**Status: Active**

Goal: make promotion package and isolated apply evidence inspectable in the WPF Run Reports workspace before any real write-path design.

Delivered in this slice:

- promotion review fields in `RunReportDetail`.
- promotable and blocked file lists.
- approval state and recommendation display.
- runtime profile, target language, and target stack display.
- configurable review policy visibility.
- hard safety invariant visibility.
- service parsing for `promotion-package.json` and `isolated-promotion-apply-report.json`.
- Run Reports view sections for promotion review and policy.

Still blocked:

- Promotion approval.
- Real repository writes.
- Main branch writes.
- Pull request creation.
- Auto-merge.
- Accepted memory mutation.
- Agent self-approval.

## Controlled Real Repo Write Path Design 172

**Status: Active**

Goal: define the locked door from promotion review into a future isolated branch/worktree apply path before any real repository write command exists.

Delivered in this slice:

- `Docs/CONTROLLED_REAL_REPO_WRITE_PATH_DESIGN_172.md`.
- dogfood memory mirror and retrieval smoke plan.
- settings-first policy shape for runtime adapters, command templates, branch naming, worktree roots, reviewer roles, evidence rules, and retention.
- hard invariant list that cannot be configured away.
- scoped human approval meaning for a future branch/worktree apply.
- future evidence requirements for branch/worktree apply and PR package creation.

Still blocked:

- Real repository writes.
- Main branch writes.
- Active developer working tree writes.
- Branch/worktree apply command.
- PR package command.
- Promotion approval execution.
- Accepted memory mutation.
- Ticket acceptance.
- Auto-merge.
- Agent self-approval.

## Controlled Write Policy Settings 173

**Status: Delivered**

Goal: resolve configurable controlled-write settings into a run-scoped effective policy while keeping hard invariants non-configurable.

Delivered in this slice:

- `ControlledWritePolicySettings` model.
- `HardSafetyInvariant` model.
- `ControlledWriteEffectivePolicy` model.
- `promotion policy effective --project IronDev --run-id <run> --json`.
- `campaign controlled-write-policy-173 --run-id <run> --json`.
- evidence files under `tools/dogfood/runs/{runId}`.
- ignored unsafe invariant override evidence.

Still blocked:

- Real repository writes.
- Main branch writes.
- Active developer working tree writes.
- Branch/worktree apply.
- PR creation.
- Accepted memory mutation.
- Ticket acceptance.
- Agent self-approval.

## Controlled Write Approval Record 174

**Status: Delivered**

Goal: define a scoped approval record for one promotion package and controlled worktree dry-run only.

Delivered in this slice:

- `ControlledWriteApprovalRecord` model.
- `promotion approval create --package-run-id <run> --run-id <approval-run> --json`.
- `campaign controlled-write-approval-174 --run-id <run> --json`.
- approval evidence bound to package id, proposed change id, source run id, and source trace id.
- dry-run-only approval state.
- explicit blocked actions for main write, active worktree write, PR creation, auto-merge, accepted memory mutation, ticket acceptance, and self-approval.

Still blocked:

- Real repository writes.
- Branch/worktree apply.
- PR creation.
- Auto-merge.
- Accepted memory mutation.
- Ticket acceptance.
- Approval reuse for future packages.
- Agent self-approval.

## Controlled Worktree Dry-Run 175

**Status: Delivered**

Goal: validate the future controlled worktree apply path without creating a worktree or copying files.

Delivered in this slice:

- `ControlledWorktreeDryRunReport` model.
- `promotion apply worktree-dry-run --package-run-id <package-run> --approval-run-id <approval-run> --target-worktree <path> --run-id <run> --json`.
- `campaign controlled-worktree-dry-run-175 --run-id <run> --json`.
- target path explicit proof.
- target outside active repository proof.
- non-main branch proof.
- package/approval/policy matching.
- promotable file list and blocked file rejection.
- active repo mutation count zero.
- dry-run target not created.

Still blocked:

- Worktree creation.
- File copy.
- Real repository writes.
- PR creation.
- Auto-merge.
- Accepted memory mutation.
- Ticket acceptance.
- Agent self-approval.
