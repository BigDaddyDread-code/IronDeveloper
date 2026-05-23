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

**Status: Active**

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
