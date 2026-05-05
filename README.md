# IronDev

IronDev is an AI-assisted software development cockpit. It helps developers manage project memory, turn chat discussions into structured tickets, plan work, understand their codebase, and generate implementation drafts — all within a persistent workflow.

The core idea is simple:

```text
Chat → Draft Ticket → Approved Ticket → Implementation Plan → Build Proposal
```

Tickets are treated as **AI execution contracts**: they should contain requirements, acceptance criteria, code context, and tests before the AI is trusted to build from them.

---

## What it does

- **Persistent project memory** — summaries, decisions, chat history, tickets, and implementation plans saved to SQL Server
- **Chat → Draft Ticket workflow** — chat discussions can become reviewable draft tickets before being saved
- **Index-aware AI flow** — if the project is not indexed, IronDev prompts before generating code-aware tickets
- **Structured tickets** — requirements, acceptance criteria, implementation notes, linked files/symbols, and tests/validation
- **Configurable LLM providers** — OpenAI, local OpenAI-compatible endpoints, and Ollama-style local models
- **Code indexing** — indexes local source files so the AI can reason about real code
- **Code workbench / builder flow** — scaffolded path toward build proposals and controlled code changes
- **Tenant-aware architecture** — multi-tenant backend with user/tenant/project separation

---

## Solution layout

```
Database/                      SQL schema and local setup scripts
Docs/                          Architecture, roadmap, testing, and local setup docs
IronDev.Api/                   ASP.NET Core REST backend
IronDev.Core/                  Shared models, interfaces, DTOs
IronDev.Infrastructure/        Dapper SQL services and AI provider implementations
IronDev.IntegrationTests/      Infrastructure & DB integration tests
IronDev.IntegrationTests.Api/  API-level integration tests
IronDeveloper/                 WPF desktop client
IronDev.slnx                   Root solution file
```

---

## Quick local setup

### Prerequisites

- .NET 10 SDK
- SQL Server / LocalDB / SQL Express
- Visual Studio or another .NET-capable IDE
- Optional: OpenAI API key, local OpenAI-compatible endpoint, or Ollama
- `IronDeveloper.Controls` cloned locally if using the source-referenced controls project

> **Note:** Do not convert `IronDeveloper.Controls` to a NuGet package yet. The controls library is still evolving and should remain source/project referenced for now.

---

## Local database setup

For a fresh local environment, run:

```text
Database/local_dev_setup.sql
```

This script creates the local schema and seeds:

```text
Tenant:   Local Dev
User:     bob@irondev.local
Password: change-me-local-only
Project:  IronDeveloper
Status:   Needs Index
```

Recommended setup flow:

```text
1. Create SQL Server database: IronDeveloper
2. Run Database/local_dev_setup.sql
3. Update IronDeveloper/appsettings.Development.json connection string
4. Launch the WPF app
5. Login with bob@irondev.local / change-me-local-only
6. Open the seeded IronDeveloper project
7. Click Index Project before using code-aware AI features
```

For full instructions, see [Docs/local-development.md](Docs/local-development.md).

---

## AI provider configuration

Configure the AI provider in `IronDeveloper/appsettings.Development.json`.

### OpenAI

```json
"Ai": {
  "Provider": "OpenAI",
  "Model": "gpt-4o",
  "ApiKey": ""
}
```

You can also use the `OPENAI_API_KEY` environment variable instead of committing an API key.

### Local OpenAI-compatible endpoint

Useful for LM Studio, vLLM, Ollama `/v1`, or another OpenAI-compatible local/private server.

```json
"Ai": {
  "Provider": "LocalOpenAI",
  "Model": "qwen2.5-coder:latest",
  "BaseUrl": "http://localhost:11434/v1",
  "ApiKey": "local-dev-key"
}
```

### Ollama native API

```json
"Ai": {
  "Provider": "Ollama",
  "Model": "qwen2.5-coder:latest",
  "BaseUrl": "http://localhost:11434"
}
```

All app-facing AI flows should use `ILLMService`; ViewModels should not know which provider is being used.

---

## Controls library

The WPF app currently uses the separate `IronDeveloper.Controls` source project.

Expected local path:

```text
C:\Users\bob\source\repos\IronDeveloper.Controls
```

Build controls first if needed:

```powershell
dotnet build C:\Users\bob\source\repos\IronDeveloper.Controls\IronDeveloperControls\IronDeveloperControls.csproj
```

Then build IronDev:

```powershell
dotnet build IronDev.slnx
```

---

## Run the WPF client

```powershell
cd IronDeveloper
dotnet run
```

---

## Run the API

```powershell
cd IronDev.Api
dotnet run
```

---

## Run tests

```powershell
# Infrastructure + DB integration tests
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --logger "console;verbosity=minimal"

# API-level tests
dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --logger "console;verbosity=minimal"
```

For more detail, see [Docs/TESTING.md](Docs/TESTING.md).

---

## Configuration files

| File | Purpose |
|---|---|
| `IronDeveloper/appsettings.json` | WPF app default connection string and AI provider config |
| `IronDeveloper/appsettings.Development.json` | Local WPF app overrides |
| `IronDev.Api/appsettings.json` | API connection string and JWT settings |
| `IronDev.Api/appsettings.Development.json` | API dev overrides |
| `IronDev.IntegrationTests/appsettings.Test.json` | Test DB connection |
| `IronDev.IntegrationTests.Api/appsettings.Test.json` | API test DB connection and JWT key |

**Never commit secrets.** Use environment variables, user secrets, or local-only config for API keys, passwords, and production JWT keys.

---

## Docs

- [Docs/local-development.md](Docs/local-development.md) — full local setup guide
- [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md) — system design and layer responsibilities
- [Docs/ROADMAP.md](Docs/ROADMAP.md) — sprint history and planned milestones
- [Docs/TESTING.md](Docs/TESTING.md) — how to run tests and the test matrix
