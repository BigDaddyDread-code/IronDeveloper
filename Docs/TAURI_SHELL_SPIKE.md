# Tauri Shell Spike

IronDev is evaluating a future desktop shell built with Tauri, React, TypeScript, and Playwright.
This spike proves the shell direction without moving product logic out of `IronDev.Api`.

## Why Tauri

Tauri gives IronDev a small desktop packaging surface while keeping the product brain in the existing .NET API.
React and TypeScript can make the cockpit UI faster to iterate, and Playwright keeps future workflows testable by stable selectors instead of screenshots.

## What This Proves

- A Tauri/Vite/React shell can start as a desktop-oriented frontend.
- The shell checks `GET /health` on `IronDev.Api`.
- The shell has generated OpenAPI types plus a typed HTTP facade for auth, tenants, projects, and tickets.
- The shell renders a Tickets cockpit with selected-ticket detail, readiness refresh, and stable `data-testid` selectors.
- Playwright can smoke-test the Vite-hosted shell surface.
- WPF has been retired; the future shell must stay API/OpenAPI-bound while it is evaluated.

## What This Does Not Prove

- It does not own product persistence or workflow logic.
- It does not delete WPF code.
- It does not migrate all workspaces.
- It does not move backend/product logic into TypeScript.
- It does not bypass `IronDev.Api`.
- It does not implement production credential vaulting, full desktop automation, or updater support.

## API Boundary

The shell follows the product boundary:

```text
Tauri / React UI
  -> generated OpenAPI types + typed HTTP facade
  -> IronDev.Api
  -> services / database / memory / tickets / agents
```

The shell checks in an OpenAPI snapshot and generated TypeScript types. See `Docs/TAURI_API_CLIENT.md` for regeneration and drift detection.

The shell assumes `IronDev.Api` is already running locally at:

```text
http://localhost:5000
```

Start the API separately:

```powershell
dotnet run --project IronDev.Api --urls http://localhost:5000
```

The shell checks:

```text
GET /health
```

Auth and project context use:

```text
POST /api/auth/login
GET /api/auth/me
GET /api/tenants
POST /api/tenants/select
GET /api/projects
POST /api/projects/{projectId}/select
```

Ticket data and safe workflow actions are routed through:

```text
GET /api/projects/{projectId}/tickets
GET /api/projects/{projectId}/tickets/{ticketId}
POST /api/projects/{projectId}/tickets
POST /api/projects/{projectId}/tickets/legacy
GET /api/tickets/{ticketId}/implementation-plan
GET /api/projects/{projectId}/tickets/{ticketId}/build-readiness
```

Ticket/project routes are authenticated. For this spike, either sign in through the shell, paste a token, or pass a token through environment configuration/browser local storage:

```powershell
$env:VITE_IRONDEV_DEV_TOKEN = "<tenant-jwt>"
$env:VITE_IRONDEV_PROJECT_ID = "1"
$env:VITE_IRONDEV_API_BASE_URL = "http://localhost:5000"
$env:IRONDEV_API_PROXY_TARGET = "https://localhost:7000"
```

or set:

```js
localStorage.setItem("irondev.token", "<tenant-jwt>");
localStorage.setItem("irondev.selectedProjectId", "1");
localStorage.setItem("irondev.apiBaseUrl", "http://localhost:5000");
```

If no token/project context is configured, the shell shows product states instead of faking ticket data.

In Vite dev mode, default localhost API calls go through `/irondev-api`. The proxy targets `https://localhost:7000` by default to avoid browser-visible CORS failures when the .NET API redirects HTTP to HTTPS. Override `IRONDEV_API_PROXY_TARGET` if your local API uses a different profile.

## How To Run

From `IronDev.TauriShell`:

```powershell
npm install
npm run dev
```

Then open:

```text
http://127.0.0.1:5173
```

## How To Test

From `IronDev.TauriShell`:

```powershell
npm run build
npm run test:e2e:install
npm run test
```

The smoke test verifies:

- `app.shell`
- `app.header`
- `app.apiStatus`
- `app.authState`
- `auth.form`
- `auth.email`
- `auth.password`
- `auth.submit`
- `auth.tokenInput`
- `auth.saveToken`
- `tenant.selector`
- `tenant.option`
- `project.selector`
- `project.option`
- `shell.nav.tickets`
- `tickets.workspace`
- `tickets.header`
- `ticket.list`
- `ticket.row` when API data is available
- `ticket.detail`
- `ticket.detail.header`
- `ticket.detail.brief`
- `ticket.detail.plan`
- `ticket.detail.context`
- `ticket.detail.tests`
- `ticket.detail.build`
- `ticket.detail.acceptanceCriteria`
- `ticket.detail.readiness`
- `ticket.inspector`
- `ticket.inspector.evidence`
- `ticket.inspector.linkedDocuments`
- `ticket.inspector.decisions`
- `ticket.inspector.affectedFiles`
- `ticket.inspector.affectedSymbols`
- `ticket.inspector.buildReadiness`
- `ticket.inspector.warnings`
- `ticket.inspector.traceLinks`
- `ticket.command.refresh`
- `ticket.command.refreshReadiness`
- `ticket.command.generatePlan`
- `ticket.command.create`
- `ticket.command.edit`
- `ticket.command.save`
- `ticket.command.cancel`
- `ticket.edit.form`
- `ticket.edit.dirtyState`
- `api.status.connected`
- `api.status.disconnected`
- `api.status.authRequired`
- `project.status.selected`
- `project.status.missing`
- `project.status.fallback`

## Tauri Desktop Check

If Rust and Cargo are installed:

```powershell
npm run tauri:dev
npm run tauri:build
```

This PR does not implement updater support. Future desktop update support should use the Tauri updater plugin after the shell direction is accepted.

## Ticket Workflow Parity Status

The Tauri shell now uses API-backed ticket detail for the selected ticket. The detail surface includes Brief, Plan, Context, Tests, and Build sections. Ticket creation, ticket edit/save, implementation-plan refresh, and readiness refresh all go through `IronDev.Api`.

Real data:

- ticket title, status, priority, type, summary, problem, content/proposed change, acceptance criteria, technical notes, linked files/symbols, test notes, context summary, generated/source metadata, and created UTC metadata
- editable selected-ticket fields saved through the existing API ticket save endpoint
- implementation-plan title, goal, scope, steps, affected context, risks, status, and UTC metadata where exposed
- build readiness status, message, warnings, blocking issues, and ready flag

Unavailable until richer API endpoints exist:

- structured related decisions
- structured linked document detail beyond source document version id
- trace URL/detail records
- run/build report evidence packages
- destructive/archive/build/apply workflows
- production edit conflict/version handling

## Migration Rule

The replacement shell is not production until it proves workflow parity, API-boundary compliance, testability, packaging, auth, update behavior, and workspace-by-workspace user value.
