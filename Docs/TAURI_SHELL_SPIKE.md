# Tauri Shell Spike

IronDev is evaluating a future desktop shell built with Tauri, React, TypeScript, and Playwright.
This spike proves the shell direction without replacing WPF or moving product logic out of `IronDev.Api`.

## Why Tauri

Tauri gives IronDev a small desktop packaging surface while keeping the product brain in the existing .NET API.
React and TypeScript can make the cockpit UI faster to iterate, and Playwright keeps future workflows testable by stable selectors instead of screenshots.

## What This Proves

- A Tauri/Vite/React shell can start as a desktop-oriented frontend.
- The shell checks `GET /health` on `IronDev.Api`.
- The shell has a typed fetch wrapper for API-backed ticket data.
- The shell renders a minimal Tickets cockpit with stable `data-testid` selectors.
- Playwright can smoke-test the Vite-hosted shell surface.
- WPF remains in place while the future shell is evaluated.

## What This Does Not Prove

- It does not replace WPF.
- It does not delete WPF code.
- It does not migrate all workspaces.
- It does not move backend/product logic into TypeScript.
- It does not bypass `IronDev.Api`.
- It does not implement production auth, desktop packaging, or updater support.

## API Boundary

The shell follows the product boundary:

```text
Tauri / React UI
  -> typed TypeScript fetch wrapper
  -> IronDev.Api
  -> services / database / memory / tickets / agents
```

The current spike uses a tiny typed fetch wrapper. The intended production path is a generated TypeScript client from the `IronDev.Api` OpenAPI/Swagger contract once auth/project selection and endpoint coverage settle.

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

Ticket data is loaded through:

```text
GET /api/projects/{projectId}/tickets
```

That route is authenticated. For this spike, pass a token through environment configuration or browser local storage:

```powershell
$env:VITE_IRONDEV_DEV_TOKEN = "<tenant-jwt>"
$env:VITE_IRONDEV_PROJECT_ID = "1"
$env:VITE_IRONDEV_API_BASE_URL = "http://localhost:5000"
```

or set:

```js
localStorage.setItem("irondev.token", "<tenant-jwt>");
localStorage.setItem("irondev.projectId", "1");
localStorage.setItem("irondev.apiBaseUrl", "http://localhost:5000");
```

If no token is configured, the shell shows an unauthenticated state instead of faking ticket data.

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
npm run test:e2e
```

The smoke test verifies:

- `app.shell`
- `app.apiStatus`
- `shell.header`
- `shell.nav.tickets`
- `tickets.workspace`
- `ticket.list`
- `ticket.row` when API data is available
- `ticket.detail`
- `ticket.inspector`
- `ticket.command.refresh`
- `api.status.connected`
- `api.status.disconnected`
- `api.status.unauthenticated`

## Tauri Desktop Check

If Rust and Cargo are installed:

```powershell
npm run tauri:dev
npm run tauri:build
```

This PR does not implement updater support. Future desktop update support should use the Tauri updater plugin after the shell direction is accepted.

## Migration Rule

WPF remains the production desktop UI until the replacement shell proves workflow parity, API-boundary compliance, testability, packaging, auth, update behavior, and workspace-by-workspace user value.
