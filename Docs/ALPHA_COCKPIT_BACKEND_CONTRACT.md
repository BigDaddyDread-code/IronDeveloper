# Alpha Cockpit Backend Contract

IronDev's alpha cockpit API is project-scoped by default. Any endpoint that returns ticket work, run evidence, memory, documents, or decisions must prove the route project owns the returned object.

## Contract Rules

- Prefer `/api/projects/{projectId}/...` routes for cockpit workflows.
- Do not use global run identifiers as sufficient authorization or ownership proof.
- A ticket-scoped run endpoint must verify:
  - the ticket exists;
  - the ticket belongs to the route project;
  - the run has persisted event metadata linking it to the same project and ticket.
- Missing or mismatched ownership returns `404` rather than leaking whether a run exists elsewhere.
- UI actions must remain disabled or show honest errors until backed by these routes.

## Alpha Spine

### Projects

- `GET /api/projects`
- `GET /api/projects/{projectId}`
- `POST /api/projects`
- `PATCH /api/projects/{projectId}`

### Tickets

- `GET /api/projects/{projectId}/tickets`
- `GET /api/projects/{projectId}/tickets/{ticketId}`
- `POST /api/projects/{projectId}/tickets`
- `PATCH /api/projects/{projectId}/tickets/{ticketId}`
- `POST /api/projects/{projectId}/tickets/{ticketId}/archive`

### Ticket Evidence

- `GET /api/projects/{projectId}/tickets/{ticketId}/evidence-summary`

Evidence summary is backend-backed and links runs only from trusted persisted run-event metadata.

### Ticket Build Runs

- `POST /api/projects/{projectId}/tickets/{ticketId}/build-runs`
- `POST /api/projects/{projectId}/tickets/{ticketId}/build-runs/disposable`
- `GET /api/projects/{projectId}/tickets/{ticketId}/build-runs`
- `GET /api/projects/{projectId}/tickets/{ticketId}/build-runs/{runId}`
- `GET /api/projects/{projectId}/tickets/{ticketId}/build-runs/{runId}/review`

The `/disposable` route is the alpha cockpit route. The older `POST .../build-runs` route remains as a compatibility alias for existing clients.

### Documents

- `GET /api/projects/{projectId}/documents`
- `GET /api/projects/{projectId}/documents/{documentId}`
- `POST /api/projects/{projectId}/documents`
- `PATCH /api/projects/{projectId}/documents/{documentId}` planned
- `POST /api/projects/{projectId}/documents/{documentId}/archive`
- `GET /api/projects/{projectId}/documents/{documentId}/versions`
- `GET /api/projects/{projectId}/documents/{documentId}/versions/current`

### Decisions

Project-scoped decision endpoints are planned. Existing memory decision APIs should not be expanded as a substitute for the cockpit decision contract.

### Memory And Retrieval

- `GET /api/projects/{projectId}/memory/search` exists
- `POST /api/projects/{projectId}/memory/search` planned
- `GET /api/projects/{projectId}/memory/traces/{traceId}` planned
- `POST /api/projects/{projectId}/memory/reindex` planned
- `GET /api/projects/{projectId}/memory/status` planned

### Health And Environment

- `GET /health`
- `GET /api/environment`
- `GET /api/projects/{projectId}/services/status` planned

## Legacy Surfaces

Global run endpoints still exist for compatibility and SSE consumers:

- `GET /api/runs/{runId}`
- `GET /api/runs/{runId}/report`
- `GET /api/runs/{runId}/events`
- `GET /api/run-reports`

Do not add new cockpit behavior to global run routes. New cockpit UI should use project-scoped ticket run endpoints.
