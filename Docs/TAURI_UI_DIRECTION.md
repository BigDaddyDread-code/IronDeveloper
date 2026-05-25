# IronDev Tauri UI Direction

IronDev's Tauri shell is a premium AI developer cockpit. It is not a generic dark web app, a CRUD admin panel, or a replacement for WPF yet.

## Product Boundary

The shell is an operational client of `IronDev.Api`.

Allowed path:

```text
Tauri / React UI -> TypeScript API client or HTTP -> IronDev.Api -> services / database / memory / agents
```

The shell must not call SQL, .NET Infrastructure services, GitHub issues as canonical tickets, or product persistence logic directly.

## Visual Language

IronDev should feel calm, technical, controlled, and evidence-first.

Use:

- dark technical surfaces
- restrained accent colour
- compact metadata
- visible readiness/status
- traceable actions
- strong hierarchy between queue, selected object, and evidence
- directional empty states with one useful next action

Avoid:

- decorative gradients
- random dashboard widgets
- large marketing hero sections
- equal-weight panels everywhere
- noisy cards
- default web controls
- hardcoded colours in components
- fake metrics that do not come from `IronDev.Api`

## Cockpit Layout

Use the same workspace model unless a workflow truly needs a variation:

- Header: product/workspace identity, API/auth/project state, summary, primary action.
- Left: queue/list/navigation optimized for scanning.
- Center: selected object narrative and workflow content. This is primary.
- Right: `ContextInspector` for evidence, provenance, traces, linked records, risks, and status.
- Bottom/sticky area: contextual next actions when the workflow needs them.

Do not make all panels visually equal. The selected workflow object owns the screen; evidence supports it.

## Components

The first Tauri shell component vocabulary is:

- `AppShell`
- `WorkspaceHeader`
- `WorkspaceLayout`
- `WorkspaceListPane`
- `WorkspaceListItem`
- `ContextInspector`
- `InspectorSection`
- `SurfacePanel`
- `StatusBadge`
- `CommandButton`
- `EmptyState`
- `ApiStatusBadge`
- `AuthRequiredState`
- `MetadataRow`
- `EvidenceCard`

Add new components only when they remove real repetition or define a reusable workflow pattern.

## Tokens

All styling should use CSS variables from `src/styles/tokens.css`.

Token categories:

- app/shell/surface backgrounds
- subtle/strong borders
- primary/secondary/muted text
- accent colours
- status colours
- spacing scale
- radius scale
- typography scale
- shadows/elevation
- state opacity
- focus rings

Do not scatter colour literals through component CSS or JSX.

## Density And Spacing

IronDev is a daily developer tool, so density should be compact but not cramped.

Rules:

- metadata is compact
- workflow content has breathing room
- section headings stay small and precise
- selected objects have stronger hierarchy than supporting panels
- do not use oversized cards for routine workflow surfaces
- use deliberate gaps from the spacing scale, not one-off margins

## State Handling

States must look like product states, not raw warnings.

Required API/auth states:

- Connected
- Disconnected
- Auth required
- Tenant required
- Project required
- Loading
- Error
- Selected ticket loading
- Readiness loading
- Readiness unavailable
- Missing context/evidence

Ticket actions must separate safe creation from destructive or repository-changing work. Create/refresh actions can appear in the cockpit command area. Archive, delete, apply, build, promotion approval, and repository mutation actions must not appear until their API path, validation, and confirmation model are explicit.

Auth-required copy should explain that `IronDev.Api` is reachable or expected locally, but product data needs a token. Use calm badges and clear actions:

- Retry connection
- Configure token

Use banners only for genuinely blocking states. Prefer contextual state panels inside the affected workflow area.

Disconnected API state should be explicit and operational:

- Title: `IronDev.Api is offline`
- Body: `Start the backend with: dotnet run --project IronDev.Api`
- Actions: `Retry connection`

Tenant and project states are first-class product states. Do not hide them as generic auth failures:

- `Tenant required`: show `tenant.selector`.
- `Project required`: show `project.selector` and `project.status.missing`.
- `Project selected`: show `project.status.selected`.

## Responsive Behaviour

The shell must work in narrow desktop windows.

Rules:

- no horizontal page scrollbar
- grid children must use `min-width: 0`
- inspector may stack below primary content
- queue may shrink but must remain scannable
- selected content must remain readable
- responsive changes must be intentional and covered by Playwright

## Testability

Every important workflow control or region needs a stable `data-testid`.

Rules:

- IDs are semantic, not visual
- IDs must survive layout changes
- do not use generated/random IDs
- do not use styling-coupled selectors in journey tests
- Playwright should assert regions, state, and overflow before screenshots are used

Current required selectors:

- `app.shell`
- `app.header`
- `app.apiStatus`
- `app.authState`
- `app.authState.configureToken`
- `app.authState.retry`
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
- `ticket.row`
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
- `ticket.command.create`
- `ticket.create.panel`
- `ticket.create.title`
- `ticket.create.summary`
- `ticket.create.type`
- `ticket.create.priority`
- `ticket.create.acceptanceCriteria`
- `ticket.create.submit`
- `ticket.create.cancel`
- `ticket.create.success`
- `ticket.create.error`
- `api.status.connected`
- `api.status.disconnected`
- `api.status.authRequired`
- `project.status.selected`
- `project.status.missing`

## What Not To Do

Do not use the Tauri spike to hide missing product boundaries. If data is not available through `IronDev.Api`, show a polished API/auth/empty state and document the missing endpoint. Do not fabricate product state in TypeScript.
