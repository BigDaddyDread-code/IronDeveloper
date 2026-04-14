# IronDev

IronDev is an AI-assisted software development assistant. It helps developers manage project memory, plan and track work, understand their codebase, and generate implementation drafts — all within a structured, persistent workflow.

---

## What it does

- **Persistent project memory** — summaries, decisions, chat history saved to SQL Server
- **Structured tickets** — create and manage typed work items with full context fields
- **Code indexing** — indexes local source files so the AI can reason about real code
- **Code workbench** — sandbox for AI-generated implementation plans, code, and test drafts
- **Tenant-aware architecture** — multi-tenant backend with JWT auth (Sprint 2 in progress)

---

## Solution layout

```
IronDev.Api/                  ASP.NET Core REST backend
IronDev.Core/                 Shared models, interfaces, DTOs
IronDev.Infrastructure/       Dapper SQL services, auth contexts
IronDev.IntegrationTests/     DB-backed integration tests (sequential)
IronDev.IntegrationTests.Api/ API-level integration tests (WebApplicationFactory)
IronDeveloper/                WPF desktop client
IronDeveloper/Database/       SQL schema — rebuild_db.sql
Docs/                         Architecture, roadmap, testing docs
```

---

## How to run

### Prerequisites

- .NET 10 SDK
- SQL Server (local instance)
- `OPENAI_API_KEY` environment variable set

### Database setup

Run `rebuild_db.sql` against your local SQL Server to create the `IronDeveloper` database with the full schema and seed data.

```sql
-- In SSMS or sqlcmd, run:
IronDeveloper/Database/rebuild_db.sql
```

For the **test database**, run the same script with the database name replaced to `IronDeveloper_Test`. Integration tests do this automatically via the test base class seeding logic.

### Run the WPF client

```bash
cd IronDeveloper
dotnet run
```

### Run the API

```bash
cd IronDev.Api
dotnet run
```

### Run integration tests

```bash
# Infrastructure + DB tests (run sequentially — shared DB)
dotnet test IronDev.IntegrationTests --settings IronDev.IntegrationTests/integration.runsettings

# API-level tests (in-process WebApplicationFactory)
dotnet test IronDev.IntegrationTests.Api
```

See [TESTING.md](Docs/TESTING.md) for full details.

---

## Configuration

| File | Purpose |
|---|---|
| `IronDev.Api/appsettings.json` | API connection string and JWT settings |
| `IronDev.Api/appsettings.Development.json` | Dev overrides |
| `IronDev.IntegrationTests/appsettings.Test.json` | Test DB connection |
| `IronDev.IntegrationTests.Api/appsettings.Test.json` | API test DB connection and JWT key |

**Never commit secrets.** Use environment variables or user secrets for `OPENAI_API_KEY` and any production JWT keys.

---

## Docs

- [ARCHITECTURE.md](Docs/ARCHITECTURE.md) — system design and layer responsibilities
- [ROADMAP.md](Docs/ROADMAP.md) — sprint history and planned milestones
- [TESTING.md](Docs/TESTING.md) — how to run tests and the test matrix
