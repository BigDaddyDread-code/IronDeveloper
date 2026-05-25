# Tauri API Client

The Tauri shell is an API client. Product behaviour stays behind `IronDev.Api`.

```text
Tauri / React UI
  -> generated OpenAPI types + typed HTTP facade
  -> IronDev.Api
  -> services / database / memory / tickets / agents
```

## Source Of Truth

The OpenAPI source is the development Swagger document exposed by `IronDev.Api`:

```text
http://localhost:5000/swagger/v1/swagger.json
```

Start the backend separately:

```powershell
dotnet run --project IronDev.Api --urls http://localhost:5000
```

The Tauri shell does not start, supervise, or manage `IronDev.Api`.

## Regeneration

From `IronDev.TauriShell`:

```powershell
npm run api:generate
```

This command:

1. Fetches Swagger from `IRONDEV_API_BASE_URL` or `VITE_IRONDEV_API_BASE_URL`.
2. Falls back to `http://localhost:5000`.
3. Writes `openapi/irondev-api.openapi.json`.
4. Generates `src/api/generated/ironDevApiTypes.ts` with `openapi-typescript`.

The checked-in OpenAPI snapshot and generated TypeScript file make drift visible in PR diffs.

## Client Shape

React components must not own raw endpoint strings.

Current layers:

- `src/api/generated/ironDevApiTypes.ts`: generated schema/path types.
- `src/api/types.ts`: shell-facing aliases and state types.
- `src/api/ironDevApi.ts`: typed HTTP facade for health, auth, tenants, projects, project selection, and tickets.

This is intentionally small. Future slices can replace the typed facade with a fuller generated client, but the boundary must remain the same.

## Auth And Project Context

The shell supports:

- missing token
- sign in with email/password
- token paste for local/dev use
- tenant selection
- project selection
- configured fallback project id
- ticket loading for the selected project

Token storage is local/dev-safe only for this spike: `localStorage` is used so the UI can prove the workflow. This is not a production credential vault.

## Local Configuration

Default API base URL:

```text
http://localhost:5000
```

Supported overrides:

```powershell
$env:VITE_IRONDEV_API_BASE_URL = "http://localhost:5000"
$env:IRONDEV_API_PROXY_TARGET = "https://localhost:7000"
$env:VITE_IRONDEV_PROJECT_ID = "1"
$env:VITE_IRONDEV_DEV_TOKEN = "<tenant-jwt>"
```

In Vite dev mode, the shell proxies default localhost API calls through `/irondev-api`. The proxy targets `https://localhost:7000` by default because the local .NET API commonly redirects `http://localhost:5000` to HTTPS. Override `IRONDEV_API_PROXY_TARGET` if your local API uses a different HTTPS port or a pure HTTP profile.

Browser storage fallback:

```js
localStorage.setItem("irondev.apiBaseUrl", "http://localhost:5000");
localStorage.setItem("irondev.token", "<tenant-jwt>");
localStorage.setItem("irondev.tenantId", "1");
localStorage.setItem("irondev.selectedProjectId", "1");
```

## Remaining Before WPF Parity

- Production-safe token storage.
- Full auth UX polish.
- Generated client coverage beyond the Tickets shell path.
- Ticket detail parity with WPF.
- Documents/Memory, Chat, Build/Run Reports, and Testing Companion workspace parity.
- Desktop automation against the packaged Tauri window, not only the Vite shell.
