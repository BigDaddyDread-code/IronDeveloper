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
Database/                      SQL schema — rebuild_db.sql
Docs/                          Architecture, roadmap, testing docs
IronDev.Api/                   ASP.NET Core REST backend
IronDev.Core/                  Shared models, interfaces, DTOs
IronDev.Infrastructure/        Dapper SQL services (Chat, Project, Ticket, Auth)
IronDev.IntegrationTests/      Infrastructure & DB integration tests (30 tests)
IronDev.IntegrationTests.Api/  API-level integration tests (Auth, Tenants)
IronDeveloper/                 WPF desktop client
IronDev.slnx                   Root solution file
```

---

## How to run

### Prerequisites

- .NET 10 SDK
- SQL Server (local instance)
- `OPENAI_API_KEY` environment variable set

### Database setup

For a fresh local environment, run `Database/local_dev_setup.sql`. This script is idempotent and safely initializes the database with the current schema and default developer seed data.

```bash
# See docs/local-development.md for full instructions
```

For the **test database**, ensure a database named `IronDeveloper_Test` exists. Integration tests manage schema and data synchronization automatically via the test bootstrap logic.

See [local-development.md](docs/local-development.md) for a complete onboarding guide.

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
