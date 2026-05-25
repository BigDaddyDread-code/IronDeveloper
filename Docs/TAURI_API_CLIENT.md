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
- `src/api/ironDevApi.ts`: typed HTTP facade for health, auth, tenants, projects, project selection, ticket queue, selected ticket detail, build readiness, and safe ticket creation.

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
- selected ticket detail loading
- build readiness refresh for the selected ticket
- non-destructive ticket creation for the selected project

Token storage is local/dev-safe only for this spike: `localStorage` is used so the UI can prove the workflow. This is not a production credential vault.

Project state has one source of truth:

- `Project required` means no project is active and product data/actions stay blocked.
- `Fallback project` means the shell is using the configured fallback id for read-only ticket loading convenience.
- `Project selected` means the user/session has an explicit selected project context.

Fallback project context must not be rendered as a selected project, and write actions such as ticket creation require an explicit selected project.

## Ticket Create Action

The first Tauri mutation is intentionally narrow and non-destructive:

```text
Tauri UI -> typed API facade -> IronDev.Api -> ticket database
```

The shell calls:

```text
POST /api/projects/{projectId}/tickets
```

The create panel collects title, summary, optional type, optional priority, and acceptance criteria. It requires an active API connection, token, tenant context where required, and selected project. On success, the shell reloads the selected project's ticket list and selects the created ticket when the API response includes an id.

Create is blocked when the API is offline, auth is missing/invalid, tenant context is missing, project context is missing, or only fallback project context is active. The disabled command exposes the reason through `ticket.create.blockedReason`.

This slice does not add archive/delete, apply proposal, build/apply mutation, repository file mutation, promotion approval, or self-approval actions. Future ticket actions must stay behind `IronDev.Api` and must be added as explicit typed facade methods before React components consume them.

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
- Ticket edit/review/action parity with WPF.
- Safe action expansion beyond create ticket.
- Rich ticket evidence endpoints for linked documents, decisions, traces, and run/build artifacts.
- Documents/Memory, Chat, Build/Run Reports, and Testing Companion workspace parity.
- Desktop automation against the packaged Tauri window, not only the Vite shell.

## Ticket Detail Data Status

API-backed in the Tauri shell:

- `GET /api/projects/{projectId}/tickets`
- `GET /api/projects/{projectId}/tickets/{ticketId}`
- `GET /api/projects/{projectId}/tickets/{ticketId}/build-readiness`
- ticket title, status, priority, type, summary, problem, proposed-change content, acceptance criteria, technical notes, linked file paths, linked symbols, tests, context summary, generated/source metadata, and legacy UTC `createdDate`
- readiness message, warnings, blocking issues, and `isReady`

Still unavailable or represented as honest empty states until the API exposes richer context:

- linked document detail beyond `sourceDocumentVersionId`
- related decisions
- structured evidence records
- trace URLs/detail records
- full build/run report linkage
