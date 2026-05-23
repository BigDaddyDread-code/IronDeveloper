# IronDev

IronDev is a governed AI software development cockpit.

It combines persistent project memory, code-aware ticketing, governed agents, safe tool-use, caged build/repair loops, test execution, trace evidence, and WPF review surfaces so developers can move from messy intent to reviewed engineering work without giving AI uncontrolled write access.

IronDev is currently a **Private Technical Alpha**.

---

## Current core loop

```text
Messy intent
  -> project memory / code context
  -> planning, ticket review, or governed agent loop
  -> safe read/test/report tool use
  -> caged disposable build/repair when allowed
  -> test and quality evidence
  -> run report / WPF review surface
  -> future promotion package
```

The original ticket workflow still matters:

```text
Chat -> Draft Ticket -> Approved Ticket -> Implementation Plan
```

But the project has moved beyond a ticket generator. IronDev now treats tickets, plans, traces, reports, and tool evidence as part of a governed execution contract.

---

## What works today

- **Persistent project memory** — summaries, decisions, chat history, tickets, implementation plans, and dogfood knowledge saved through SQL-backed/project-aware services.
- **Chat -> Draft Ticket workflow** — chat discussions can become reviewable draft tickets before being saved.
- **Codex/codebase ticket review** — generated tickets can show context quality, confidence, affected files, affected symbols, build order, and grounding warnings before import.
- **Index-aware AI flow** — if the project is not indexed, IronDev prompts before generating code-aware tickets.
- **Code indexing and semantic context** — local source files and Roslyn-style symbol context are used to ground tickets and builder context.
- **Governed agent layer** — agents operate under explicit roles, model profiles, allowed tools, ConscienceAgent gating, and ThoughtLedger visible reasoning summaries.
- **Opt-in live governed agents** — OpenAI, LocalOpenAI, and Ollama profiles can be used for advisory live model evidence while deterministic fallbacks remain authoritative.
- **Governed Planner/Critic tool loop** — PlannerAgent can request named safe capabilities, the registry validates them, tools collect evidence, CriticAgent reviews sufficiency, PlannerAgent revises, and a human escalation gate records review requirements.
- **Caged BuilderAgent repair loop** — BuilderAgent can build, break, repair, and retest generated code only inside explicit disposable workspaces.
- **C# dogfood/Test Agent runner** — `test run-plan`, `dogfood run-plan`, and `agent tester run-plan` now execute through the C# ReplayRunner path. PowerShell remains as compatibility/fallback only.
- **Run Reports WPF viewer** — the WPF app reads trace/report/evidence files through shared C# services. It does not shell out to the CLI or parse stdout.
- **Clean CLI control surface** — product-shaped commands and explicit dogfood commands give Codex, humans, and future CI a predictable command language.
- **Tenant-aware architecture** — the application keeps user, tenant, project, and memory separation as a first-class design concern.

---

## Hard safety boundaries

IronDev uses **governed autonomy, not free autonomy**.

Current hard boundaries:

- No real repository writes by agents.
- No autonomous accepted-memory mutation.
- No live ticket creation without review.
- No patch apply outside explicit disposable workspaces.
- No ConscienceAgent / ThoughtLedger bypass.
- No self-approval.
- Live model output is advisory evidence only.
- ResearchAgent evidence cannot override accepted project memory.
- RetrieverAgent live output cannot override deterministic ranking or project scoping.
- QualityAgent / KilljoyAgent cannot override deterministic quality gates.
- BuilderAgent may repair only inside the disposable workspace cage.

A future real-repository write path will require a separate reviewed design, promotion package, isolated branch/worktree proof, and human approval flow.

---

## Current Alpha status

IronDev is a **Private Technical Alpha**.

It can:

- turn messy product/project intent into structured planning and ticket review
- generate and import grounded Codex tickets
- retrieve weighted project/code context
- run governed Planner/Critic tool loops using safe read/test/report tools
- run caged disposable BuilderAgent build/repair loops
- execute dogfood/test plans through a C# runner
- show trace-backed run reports in WPF
- attempt opt-in live model calls as advisory evidence

It cannot yet:

- write generated work to the real repository
- promote disposable output into a reviewed branch or pull request
- mutate accepted memory autonomously
- create live tickets without review
- approve its own work
- act as an unsupervised autonomous developer

---

## Agent layer

IronDev's agent layer is intentionally governed.

| Agent | Current role |
| --- | --- |
| `SupervisorAgent` | Coordinates governed loops after deterministic orchestration state exists. |
| `PlannerAgent` | Drafts plans, product-spike intake packages, and revisions. |
| `ArchitectAgent` | Reviews architecture proposals against weighted context and boundaries. |
| `BuilderAgent` | Builds/repairs only inside explicit disposable workspaces. |
| `TesterAgent` | Executes plans and reports. Does not fix. |
| `QualityAgent` / `KilljoyAgent` | Runs gates and reports debt; advisory live notes only. |
| `RetrieverAgent` | Packages weighted project memory/context and rejected evidence. |
| `CriticAgent` | Reviews failures, risks, and evidence sufficiency. |
| `SentinelAgent` | Observes internal campaign/failure/test evidence. |
| `ResearchAgent` | Packages explicit external evidence only. |
| `ConscienceAgent` | Returns `Allow`, `Block`, or `NeedsMoreEvidence`. Does not execute. |
| `ThoughtLedger` | Produces visible reasoning summaries without hidden chain-of-thought. |

Model profiles are runtime-configurable and currently support:

- `OpenAI`
- `LocalOpenAI`
- `Ollama`

Provider configuration does not grant authority. Tool authority remains controlled by agent definitions, governed tool capabilities, ConscienceAgent, ThoughtLedger, and workflow boundaries.

See [Docs/AGENTS.md](Docs/AGENTS.md) for the current source of truth.

---

## Governed tool loop

The current governed Planner/Critic loop uses a named capability model instead of raw command execution.

Typical shape:

```text
PlannerAgent request
  -> allowed capability request
  -> GovernedToolRegistry validation
  -> safe tool execution / evidence collection
  -> CriticAgent evidence review
  -> PlannerAgent plan revision
  -> human escalation gate
  -> trace/report output
```

Current safe capability examples include:

- `memory.search`
- `code.search`
- `trace.read`
- `failure.latest`
- `test.run-plan`
- `quality.run-gate`
- `project.build` profile resolution

This path is read/test/report-oriented. It does not grant raw command execution, real repo writes, memory mutation, ticket creation, patch apply, or self-approval.

---

## Caged build and repair

BuilderAgent is currently caged.

It may create, modify, build, test, and repair generated files only inside explicit disposable workspaces.

The current disposable repair loop can:

- generate a small Solitaire WPF/Core/Tests solution inside a temp workspace
- intentionally inject a build failure
- record and repair a missing project reference
- intentionally inject a rule-test failure
- record and repair a Klondike rule bug
- rerun build/tests to final pass
- emit trace/report/evidence with real repository mutation count zero

This proves the build/repair loop, not permission to write to the real repo.

---

## Run Reports viewer

The WPF app includes a service-backed Run Reports viewer.

It reads file-backed run evidence through shared C# services:

```text
WPF UI
  -> RunReportsViewModel
  -> IRunReportService / IRunEvidenceService
  -> file-backed run reports
```

It does **not** shell out to `IronDev.ReplayRunner`, parse CLI stdout, or couple WPF view models to command names.

Run reports are currently read from:

```text
tools/dogfood/runs/{runId}/
```

Common run output includes:

- `report.json`
- `test-agent-report.json`
- `trace.json`
- `report.md`
- `build-run-report.json`
- `builder-repair-loop-report.json`
- `evidence/`
- `logs/`

---

## CLI control surface

The CLI is the external control surface for Codex, dogfood plans, CI-style validation, and human debugging.

Common commands:

```powershell
# Validate CLI/docs/test-plan inventory consistency
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- inventory validate --run-id InventoryCheck --json

# Run a test/dogfood plan through the C# runner
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- test run-plan --plan .\tools\dogfood\test-agent-plans\main-alpha-regression-pack.json --run-id MainAlpha --json

# Run the caged disposable BuilderAgent repair loop
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- build disposable repair --project Solitaire --run-id BuilderRepair --json

# Run the governed Planner/Critic tool-loop campaign
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- campaign governed-tool-loop-162-167 --run-id GovernedToolLoop --json
```

Important command groups include:

- `memory ...`
- `agent ...`
- `govern ...`
- `build ...`
- `test ...`
- `trace ...`
- `run-report ...`
- `inventory ...`
- `campaign ...`
- `dogfood ...`

See [Docs/CLI_COMMAND_INVENTORY.md](Docs/CLI_COMMAND_INVENTORY.md) for the current command surface.

---

## Solution layout

```text
Database/                      SQL schema and local setup scripts
Docs/                          Architecture, roadmap, agent, testing, and local setup docs
IronDev.Api/                   ASP.NET Core REST backend
IronDev.Core/                  Shared models, interfaces, DTOs, agent/run-report contracts
IronDev.Infrastructure/        Dapper SQL services, AI providers, agent/runtime services
IronDev.IntegrationTests/      Infrastructure, DB, agent, runner, and WPF/service tests
IronDev.IntegrationTests.Api/  API-level integration tests
IronDeveloper/                 WPF desktop client
Scripts/                       Local setup and development scripts
tools/IronDev.ReplayRunner/    CLI/dogfood runner and governed campaign surface
tools/dogfood/                 Dogfood plans, run evidence, knowledge mirror, fixtures
IronDev.slnx                   Root solution file
```

---

## Quick local setup

### Prerequisites

- .NET 10 SDK
- SQL Server / LocalDB / SQL Express
- Visual Studio or another .NET-capable IDE
- Optional: OpenAI API key, local OpenAI-compatible endpoint, or Ollama
- Optional: Docker Desktop for Weaviate semantic-memory dogfooding
- `IronDeveloper.Controls` cloned locally if using the source-referenced controls project

> **Note:** Do not convert `IronDeveloper.Controls` to a NuGet package yet. The controls library is still evolving and should remain source/project referenced for now.

---

## Quick bootstrap script

For a local dev machine with .NET and Docker Desktop installed:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\setup-local-dev.ps1
```

This restores packages, starts and smoke-tests Weaviate, builds the WPF app, and runs a small stabilisation smoke test set.

Database setup is opt-in:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\setup-local-dev.ps1 -RunDatabaseSetup
```

The script intentionally does not run DB setup by default, so it will not reseed or change an existing database unless you ask it to.

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

## Optional Weaviate semantic memory

Local Weaviate is used as a rebuildable semantic index. SQL Server remains the source of truth.

Start it with:

```powershell
.\Scripts\weaviate-dev.ps1 up
```

If PowerShell blocks local scripts:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\weaviate-dev.ps1 up
```

Smoke test it with:

```powershell
.\Scripts\weaviate-dev.ps1 smoke
```

For settings and troubleshooting, see [Docs/weaviate-local-setup.md](Docs/weaviate-local-setup.md).

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

All app-facing AI flows should use `ILLMService` or the governed agent/model services. ViewModels should not know which provider is being used.

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

## Run tests and dogfood checks

```powershell
# Infrastructure + DB integration tests
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --logger "console;verbosity=minimal"

# API-level tests
dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --logger "console;verbosity=minimal"

# Main dogfood regression pack through the C# runner
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- test run-plan --plan .\tools\dogfood\test-agent-plans\main-alpha-regression-pack.json --run-id MainAlpha --json

# CLI/docs/test-plan inventory validation
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- inventory validate --run-id InventoryCheck --json
```

For more detail, see [Docs/TESTING.md](Docs/TESTING.md) and [Docs/TEST_AGENT_SPEC.md](Docs/TEST_AGENT_SPEC.md).

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

## Current roadmap

Near-term work is focused on turning caged execution into reviewable real-work output:

1. **Solitaire Product Test Campaign** — prove the disposable Solitaire output is usable, scoped, traceable, and honestly reviewed, not just build-green.
2. **IDA Review Room / External Review Sessions** — add read-only review sessions for run reports, code evidence, product spikes, and promotion candidates.
3. **Disposable Build Promotion Package** — package disposable output into a reviewable candidate with files, risks, tests, and human checklist.
4. **Isolated Branch / Worktree Apply Proof** — apply a reviewed package to an isolated branch/worktree, not `main`.
5. **Promotion Review UI** — extend Run Reports into a review surface for evidence, risks, diffs, and approval/rejection.
6. **IronDev self-dogfood ticket** — use the full loop on a small IronDev improvement.

---

## Docs

- [Docs/AGENTS.md](Docs/AGENTS.md) — current agent roles, maturity, authority, and boundaries
- [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md) — system design and layer responsibilities
- [Docs/CLI_COMMAND_INVENTORY.md](Docs/CLI_COMMAND_INVENTORY.md) — current ReplayRunner command surface
- [Docs/ROADMAP.md](Docs/ROADMAP.md) — sprint history and planned milestones
- [Docs/TEST_AGENT_SPEC.md](Docs/TEST_AGENT_SPEC.md) — C# dogfood runner and Test Agent contract
- [Docs/RUN_REPORT_VIEWER_SERVICE_144.md](Docs/RUN_REPORT_VIEWER_SERVICE_144.md) — WPF Run Reports viewer/service boundary
- [Docs/local-development.md](Docs/local-development.md) — full local setup guide
- [Docs/TESTING.md](Docs/TESTING.md) — how to run tests and the test matrix
