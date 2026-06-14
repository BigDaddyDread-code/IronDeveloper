# IronDev

IronDev is a governed AI software engineering system under active release-track construction.

The current backend foundation is a governed substrate for project memory, authority boundaries, workflow evaluation, safe dry-run review material, and candidate workflow review packages. It is **not** a real autonomous workflow execution engine yet.

The design goal is simple:

> Help developers move from messy intent to reviewable engineering work without giving AI uncontrolled authority over source code, memory, tools, workflow state, or approvals.

---

## Current status

IronDev is currently in **release-track backend construction**.

The project has moved beyond the original ticket-generator framing. The current backend focus is durable governance, explicit authority boundaries, governed memory proposals, and a governed runner substrate.

The important status line:

> IronDev can describe, validate, halt, dry-run, and package review material. It still cannot autonomously execute real workflow work, mutate source, promote memory, dispatch agents, invoke tools, or approve itself.

---

## Current governed substrate

The current backend foundation includes:

- **Durable governance substrate** — append-only governance events, read models, and receipt-style proof boundaries.
- **Project authority model** — project-scoped authority and approval policy boundaries.
- **A2A handoff contract spine** — reference-only handoff material and validation boundaries.
- **Durable workflow run substrate** — workflow run and step records as source-of-truth state, not hidden orchestration.
- **Governed memory proposal substrate** — staged memory proposal, duplicate/stale/conflict detection, promotion request packaging, and tests proving memory cannot promote itself.
- **Governed runner substrate** — typed step contracts, evaluation-only runner checks, policy preflight, ThoughtLedger traceability, A2A validation, approval-required halt, safe dry-run execution, boxed route labels, and tests proving workflow cannot grant authority.

SQL remains the source of truth. Vector/search systems are rebuildable retrieval aids, not authority.

---

## Block L governed runner substrate

Block L establishes the governed runner foundation:

| Area | What exists | Boundary |
| --- | --- | --- |
| Typed workflow step contract | Steps can be represented with required evidence | Contract is not execution |
| Runner skeleton | Supplied step contracts can be evaluated | Evaluation only |
| Policy preflight | Sensitive steps can be blocked before future execution | Check is not approval |
| ThoughtLedger reference | Steps require traceability references | Traceability is not authority |
| A2A validation | Supplied handoff snapshots can be validated | Validation is not dispatch |
| Approval halt | Approval-required state can be reported explicitly | Halt is not approval |
| Safe dry-run | Eligible snapshots can produce deterministic review material | Dry-run is not real execution |
| Boxed route adapter | Supplied runner/dry-run snapshots can become advisory labels | Route label is not decision ownership |
| Authority test pack | Workflow artifacts are proven unable to mint authority | Evidence is not approval |

Block L is a substrate. It does not mean IronDev can drive the workflow car yet.

---

## Hard invariants

These are not slogans. They are boundaries the tests are meant to protect.

```text
Evidence is not approval.
Traceability is not authority.
Validation is not dispatch.
Halt is not approval.
Dry-run is not execution.
Route label is not decision ownership.
Receipt is not capability.
SQL remains source of truth.
```

Provider configuration does not grant authority. Live model output is advisory evidence only. Tool authority remains controlled by governed capability definitions, policy gates, ThoughtLedger traceability, approval boundaries, and workflow invariants.

---

## What IronDev can do today

Current backend capabilities include:

- store project, governance, workflow, and memory-proposal records through explicit contracts;
- represent typed workflow steps and required evidence;
- evaluate supplied workflow steps for missing evidence and boundary blockers;
- require ThoughtLedger traceability references on workflow steps;
- validate supplied A2A handoff snapshots without sending handoffs;
- report approval-required halt state without granting approval;
- run deterministic non-mutating dry-run review checks from supplied eligible snapshots;
- map supplied runner/dry-run snapshots to boxed advisory route labels;
- stage memory proposals and promotion request packages without allowing memory to promote itself;
- prove through regression tests that workflow artifacts cannot grant authority;
- run focused backend validation bands through the .NET test suite.

---

## What IronDev cannot do yet

IronDev currently cannot:

- execute real workflow steps;
- transition workflow state as part of real execution;
- complete workflow steps;
- create, grant, deny, or satisfy approvals;
- satisfy policy;
- dispatch agents;
- send A2A handoffs;
- invoke tools as workflow authority;
- call models as workflow authority;
- build prompts as workflow execution;
- mutate the real repository;
- apply patches to source;
- promote memory into accepted memory autonomously;
- activate retrieval as authority;
- write SQL as part of workflow execution;
- expose API/CLI/UI runtime execution for governed workflows;
- act as an unsupervised autonomous developer.

A future source-apply path still requires a separate controlled design: review package, isolated branch/worktree proof, explicit approval, dry-run/apply boundary, and human-owned promotion.

---

## Candidate workflows

Block M candidate workflows sit on top of the Block L substrate.

Candidate workflows are allowed to produce safe review material from supplied evidence. They are not allowed to mutate source, dispatch agents, invoke tools, promote memory, activate retrieval, transition workflow state, or claim root-cause certainty.

The first candidate workflow is the Test Failure Review candidate: it turns supplied test failure evidence into review material only. It does not run tests or fix code.

---

## Memory direction

IronDev uses staged, governed memory movement.

Current memory direction:

- project-specific truth remains isolated;
- proposed memory is staged before promotion;
- duplicate, stale, and conflicting memory candidates can be detected;
- promotion requests are review packages, not automatic acceptance;
- memory cannot promote itself;
- cross-project learning belongs in sanitized portable engineering memory, not leaked project facts.

Accepted memory and confidential project facts must not be mixed with generalized engineering lessons.

---

## Agent and model boundary

IronDev keeps agents governed rather than free-running.

Agent/model output may become advisory evidence or review material. It must not become authority by itself.

Current boundary:

- no self-approval;
- no hidden chain-of-thought storage;
- no ConscienceAgent or ThoughtLedger bypass;
- no raw prompt/raw completion/raw tool output as durable proof;
- no whole-patch payloads as durable hidden evidence;
- no model/provider configuration as permission;
- no tool use without governed capability and policy boundaries;
- no source mutation without a future controlled source-apply design.

See `Docs/AGENTS.md` for agent details when current.

---

## CLI and validation surface

The repository includes a CLI/dogfood runner surface used for validation and proof campaigns.

Common validation shape:

```powershell
# Focused workflow/governance style tests
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~Workflow" --logger "console;verbosity=minimal"

# Governance regression band
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~Governance" --logger "console;verbosity=minimal"

# API/CLI contract gates
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~ApiCliContract|FullyQualifiedName~ApiCliReleaseGate" --logger "console;verbosity=minimal"

# Build
dotnet build IronDev.slnx --no-restore -v:minimal
```

CLI and dogfood commands are useful validation/control surfaces. They do not override governance boundaries.

---

## Solution layout

```text
Database/                      SQL schema and local setup scripts
Docs/                          Architecture, ADRs, receipts, roadmap, agent, testing, and setup docs
IronDev.Api/                   ASP.NET Core REST backend
IronDev.Core/                  Core contracts, workflow/governance models, agent/run/report contracts
IronDev.Infrastructure/        SQL services, AI provider adapters, infrastructure implementations
IronDev.IntegrationTests/      Governance, workflow, memory, CLI, and boundary tests
IronDev.IntegrationTests.Api/  API-level integration tests
IronDev.TauriShell/            Client shell spike
Scripts/                       Local setup and development scripts
tools/IronDev.ReplayRunner/    CLI/dogfood runner and campaign surface
tools/dogfood/                 Dogfood plans, run evidence, knowledge mirror, fixtures
IronDev.slnx                   Root solution file
```

---

## Quick local setup

### Prerequisites

- .NET SDK used by the solution
- SQL Server / LocalDB / SQL Express
- Visual Studio or another .NET-capable IDE
- Optional: OpenAI API key, local OpenAI-compatible endpoint, or Ollama
- Optional: Docker Desktop for local Weaviate semantic-memory dogfooding

### Bootstrap script

For a local dev machine with .NET and Docker Desktop installed:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\setup-local-dev.ps1
```

Database setup is opt-in:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\setup-local-dev.ps1 -RunDatabaseSetup
```

The script intentionally does not run DB setup by default, so it will not reseed or change an existing database unless explicitly requested.

---

## Local database setup

For a fresh local environment, run:

```text
Database/local_dev_setup.sql
```

Recommended setup flow:

```text
1. Create SQL Server database: IronDeveloper
2. Run Database/local_dev_setup.sql
3. Update IronDev.Api/appsettings.Development.json connection string
4. Launch the API
5. Login with the seeded local dev user
6. Open the seeded IronDeveloper project
7. Index the project before using code-aware features
```

For full instructions, see `Docs/local-development.md`.

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

For settings and troubleshooting, see `Docs/weaviate-local-setup.md`.

---

## AI provider configuration

Configure the AI provider in `IronDev.Api/appsettings.Development.json`.

Supported provider profiles have included:

- `OpenAI`
- `LocalOpenAI`
- `Ollama`

Provider setup enables model access. It does not grant tool, workflow, source, approval, memory, or retrieval authority.

---

## Current roadmap posture

Near-term work should stay boring and sequential:

1. keep candidate workflows review-material-only;
2. prove candidate workflow boundaries before adding controlled source apply;
3. introduce source apply only through explicit approval, dry-run, and review package boundaries;
4. keep UI/API/CLI surfaces thin consumers of backend truth;
5. keep SQL as source of truth and retrieval as rebuildable support.

No fake autonomy. No fake receipts. No hidden authority paths.
