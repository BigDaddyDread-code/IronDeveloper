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

Durable run state is the source of truth for ticket build runs. Run events are child evidence records tied to the run id. File-backed run reports remain readable as projections/evidence, not the canonical lifecycle record.

Disposable ticket build execution is backend-owned. Clients may request "start a disposable run for this ticket"; they must not supply source repository paths, workspace roots, command lists, cleanup policy, timeout policy, or evidence retention policy. The backend resolves those from trusted project/configuration state, creates the durable run, creates the disposable workspace, executes the allowed command profile, persists command stdout/stderr evidence, and preserves failed workspaces or failure bundles for debugging.

### Discussion-To-Code Proposal Loop

- `POST /api/projects/{projectId}/discussions`
- `POST /api/projects/{projectId}/documents/{documentVersionId}/tickets`
- `POST /api/projects/{projectId}/tickets/{ticketId}/review`
- `POST /api/projects/{projectId}/tickets/{ticketId}/disposable-code-runs`
- `GET /api/projects/{projectId}/tickets/{ticketId}/build-runs/{runId}/review-package`

This is the backend proposal/run/review-package spine. `ICodeProposalGenerator` creates a `CodeProposal` with generated files, expected output, and a backend-owned build/run profile. `IDisposableCodeRunService` executes that proposal in a disposable workspace. `IRunReviewPackageService` assembles review evidence from run state, persisted events, generated files, command logs, code standards output, and output verification.

Hello World is scenario fixture 1. Calculator console app is scenario fixture 2. Both use the same generic pipeline and the same product services. They are not Alpha-specific product services, they are not agent debate, and they must not mutate or apply generated code to the real repository. Successful execution ends in `PausedForApproval`.

### Documents

- `GET /api/projects/{projectId}/documents`
- `GET /api/projects/{projectId}/documents/{documentId}`
- `POST /api/projects/{projectId}/documents`
- `PATCH /api/projects/{projectId}/documents/{documentId}` planned
- `POST /api/projects/{projectId}/documents/{documentId}/archive`
- `GET /api/projects/{projectId}/documents/{documentId}/versions`
- `GET /api/projects/{projectId}/documents/{documentId}/versions/current`

### Decisions

- `GET /api/projects/{projectId}/decisions`
- `GET /api/projects/{projectId}/decisions/{decisionId}`
- `POST /api/projects/{projectId}/decisions`
- `PATCH /api/projects/{projectId}/decisions/{decisionId}`
- `POST /api/projects/{projectId}/decisions/{decisionId}/supersede`
- `POST /api/projects/{projectId}/decisions/{decisionId}/archive`

Decision endpoints enforce the route project before returning, updating, superseding, or archiving a decision.

### Memory And Retrieval

- `GET /api/projects/{projectId}/memory/search` exists
- `POST /api/projects/{projectId}/memory/search`
- `GET /api/projects/{projectId}/memory/traces/{traceId}` planned
- `POST /api/projects/{projectId}/memory/reindex`
- `GET /api/projects/{projectId}/memory/status`

### Health And Environment

- `GET /health`
- `GET /api/environment`
- `GET /api/projects/{projectId}/services/status`

## Legacy Surfaces

Global run endpoints still exist for compatibility and SSE consumers:

- `GET /api/runs/{runId}`
- `GET /api/runs/{runId}/report`
- `GET /api/runs/{runId}/events`
- `GET /api/run-reports`

Do not add new cockpit behavior to global run routes. New cockpit UI should use project-scoped ticket run endpoints.
