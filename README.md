# IronDev

IronDev is a governed AI software engineering workspace. It turns project-scoped conversation and work items into traceable build, review, approval, continuation, and controlled-apply journeys without allowing model output or client state to grant authority.

The current product is usable through the React/Tauri shell against the ASP.NET Core API. It is under active development, but working routes are described by their actual capability rather than by maturity labels.

## Product model

```text
Board | Chat | Work Item | Library
```

- **Board** is the project cockpit: readiness, attention, work-item stages, and the run queue.
- **Chat** holds direct IronDev sessions and project channels, with source and document context plus reviewed ticket-draft handoff.
- **Work Item** carries one item through Shape, Ticket, Build, Review, and Done.
- **Library** contains Explorer, Documents, Tools, Members, Governance, Project setup, Audit, and Settings.

Entry is explicit: sign in, resolve tenant access, choose or connect a project, complete setup when required, then enter the Board. Tenant choice appears only when more than one accessible tenant needs a decision.

See [Docs/product/README.md](Docs/product/README.md) for the current product contracts and [Docs/product/CURRENT_PRODUCT_CAPABILITIES.md](Docs/product/CURRENT_PRODUCT_CAPABILITIES.md) for the maintained capability matrix.

## Capability truth

| Area | Current status | Boundary |
| --- | --- | --- |
| Authentication and tenant resolution | Supported | JWT identity and backend tenant access remain authoritative. |
| Project choose, connect, and setup | Supported | Readiness and remedies come from the API; the client does not infer them. |
| Board and work-item navigation | Supported | Board columns currently derive from ticket status; richer backend read models are the next product work. |
| Direct Chat and project channels | Supported | Conversation can form work; it cannot approve, continue, or apply work. |
| Ticket draft and confirmation | Supported | A ticket is created only through the backend confirmation contract. |
| Governed run, build, test, critic review, and finding disposition | Supported | Refusals, evidence, and stage transitions are backend-owned. |
| Human approval and continuation | Supported | Approval is actor- and hash-bound; recording approval is separate from continuation. |
| Controlled apply | Supported with explicit configuration | Applies reviewed changes only through disposable or isolated workspace boundaries. It does not write directly to `main`, commit, push, merge, release, or deploy. |
| Documents and immutable versions | Supported | Upload, processing, detail, version, and exact-version routes are project-scoped. |
| Governed tool catalogue | Read-only supported | Registration, connection, health, declared scope, and evidence are visible. General product-side tool invocation/configuration is not implemented. |
| Tenant-user and channel membership | Supported | Tenant and channel eligibility is re-evaluated by the backend; project-specific membership is not implemented. |
| Project-specific membership | Not implemented | Current project visibility is tenant-scoped. |
| Unified audit ledger | Not implemented | Existing governance and run evidence remains available through current viewers. |
| User invitation lifecycle | Not implemented | Direct tenant-user administration exists; invite/pending/accept does not. |
| Notifications, presence, and unread collaboration state | Not implemented | Shared channels persist messages but do not claim realtime collaboration completeness. |
| Production shared-host offering | Not implemented | Multi-user contracts exist; deployment, operations, and production security posture are not yet a supported product offering. |

## Governance boundaries

These are enforced design boundaries, not product slogans:

```text
Evidence is not approval.
Traceability is not authority.
Validation is not dispatch.
Recording approval is not continuation.
Continuation is not apply permission.
Retrieval is not source of truth.
The client requests actions; the backend decides.
SQL remains the durable source of truth.
```

Provider configuration enables model access. It does not grant tool, workflow, source, approval, memory, or retrieval authority. Live model output is advisory material until a governed backend contract accepts it.

No agent may approve itself, silently mutate accepted memory, or write the active repository as autonomous authority. The controlled worktree and skeleton apply paths require explicit policy, evidence, and human approval, and remain separate from commit, push, merge, release, and deployment.

## Runtime posture

### Local development and LocalTest

The supported development posture is local:

- ASP.NET Core API;
- isolated SQL Server or LocalDB database;
- React/Vite in a browser or the Tauri desktop shell;
- optional configured model provider;
- optional Weaviate as a rebuildable retrieval index.

Start the complete LocalTest stack used for product and PR checks with:

```powershell
.\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly
```

Workbench previews carry a programme version from `workbench-version.json`, the exact Git commit, and a short preview ID. Give each PR its own ID so its database, workspace, and logs are isolated:

```powershell
.\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset -PreviewId workbench-pr00a
```

The example owns `IronDeveloper_Test_workbench_pr00a`, `C:\IronDevTestWorkspaces\workbench-pr00a`, and `C:\IronDevTestLogs\workbench-pr00a`. Reusing the command resets only those targets. Use different `-ApiBaseUrl` and `-UiPort` values when running two previews at the same time.

The V1 fallback uses the same isolated preview data and is selected explicitly:

```powershell
.\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -PreviewId workbench-pr00a -UseV1
```

Use `-Reset` only when disposable LocalTest data should be rebuilt. The launcher starts the API, verifies the seeded login and environment contract, then starts the browser or Tauri shell. The UI reports the actual `LocalTest` environment and deterministic model mode.

Normal LocalTest sessions prove workflow behavior but do not claim they can finish project feature work. For a deliberately destructive disposable-sandbox journey, use the explicit project-work mode:

```powershell
.\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset -EnableSandboxApply
```

That switch is LocalTest-only. It binds the API, browser, test database, contracted sandbox root, disposable project marker, and launcher session identity. It enables no automatic apply, commit, push, pull request, release, or browser-side authority.

LocalTest credentials are seeded for local use only:

```text
bob@irondev.local
change-me-local-only
```

For development setup details, see [Docs/local-development.md](Docs/local-development.md) and [Docs/testing/LOCAL_MANUAL_TEST_PLAN.md](Docs/testing/LOCAL_MANUAL_TEST_PLAN.md).

### Shared hosting

IronDev has tenant, member, channel, attribution, and authority contracts that support multi-user behavior. That does not make this repository a supported production-hosted service. Production identity integration, deployment topology, secret management, backup/restore, observability, service-level objectives, and security operations are not yet a published shared-host contract.

## API contract

The running API's Swagger document is the transport source of truth. The checked-in OpenAPI snapshot and TypeScript types make drift reviewable:

```powershell
.\tools\contracts\update-openapi-contract.ps1 -Check
.\Scripts\ci\run-frontend-contract-ci.ps1
```

API-changing PRs must update the generated artifacts in the same PR. CI starts an isolated API, regenerates both artifacts, and fails if the generated tree becomes dirty.

## Validation

Common validation lanes:

```powershell
# Fast unit tests
dotnet test .\IronDev.UnitTests\IronDev.UnitTests.csproj --no-restore

# Frontend type-check and live API contract drift
.\Scripts\ci\run-frontend-contract-ci.ps1

# Full solution build
dotnet build .\IronDev.slnx --no-restore -v:minimal
```

SQL-backed and long-running lanes are selected separately in CI. A passing test is evidence for the behavior it exercised; it is not approval or release authority.

## Repository layout

```text
Database/                      SQL schema, migrations, and local setup
Docs/                          Product, architecture, ADR, testing, and historical evidence
IronDev.Api/                   ASP.NET Core API
IronDev.Core/                  Core workflow, governance, agent, and evidence contracts
IronDev.Infrastructure/        SQL stores, providers, runners, and controlled mutation services
IronDev.UnitTests/             Fast core tests
IronDev.IntegrationTests*/     Boundary, API, SQL, and governed-journey tests
IronDev.TauriShell/            React client and Tauri desktop shell
Scripts/                       CI, local setup, and compatibility scripts
tools/                         Contract, LocalTest, replay, and dogfood tooling
```

Historical receipts and milestone documents retain the language that was true when they were produced. Current capability claims belong in this README and the current product specifications.
