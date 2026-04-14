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

### Test baseline at Sprint 1 close
- `IronDev.IntegrationTests`: **21 passed, 0 failed**

---

## Sprint 1.5 — Stabilization & Tidy

**Status: Complete ✅**

Goal: put the project in good stead before auth sprint.

### Delivered
- Removed dead `IronDev.Memory` project and temp build scripts
- Initialized git with `.gitignore` (bin/obj, secrets, NuGet artifacts)
- `README.md` — what it is, how to run
- `Docs/ARCHITECTURE.md` — layer map, tenancy model, local vs backend split
- `Docs/ROADMAP.md` — this file
- `Docs/TESTING.md` — test matrix and running instructions
- Baseline git tag: `milestone/memory-workbench-tenancy`

---

## Sprint 2 — Auth, Login, JWT, Request-Scoped Tenancy

**Status: In Progress 🔄**

Goal: replace `DevelopmentTenantContext` in the API path with real JWT-authenticated, request-scoped tenant context.

### Planned
- `POST /api/auth/login` — BCrypt credential validation, issues base JWT
- `GET /api/auth/me` — reads claims, returns user profile
- `GET /api/tenants` — returns assigned tenants for the current user
- `POST /api/tenants/select` — verifies membership, re-issues JWT with `tenant_id` claim
- `JwtTenantContext` — request-scoped `ICurrentTenantContext` reading from JWT claims
- `UserService` — credential validation and tenant membership queries
- `IronDev.IntegrationTests.Api` — WebApplicationFactory-based API integration tests
- Auth, harness, and tenant controller test suites

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
