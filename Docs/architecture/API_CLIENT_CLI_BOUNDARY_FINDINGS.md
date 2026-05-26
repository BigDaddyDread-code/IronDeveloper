# API/Client/CLI Boundary Findings

Last reviewed: 2026-05-26

## Boundary Target

```text
TauriShell / Product CLI / Future Clients
  -> IronDev.Client
    -> IronDev.Api
      -> Core / Infrastructure / Memory
```

This branch is an evidence slice. It documents what is true now and where the boundary is still incomplete.

## Already Clean

- `IronDev.Client` has no `IronDev.Infrastructure` project reference.
- `tools/IronDev.Cli` has no `IronDev.Infrastructure` project reference.
- `IronDeveloper/IronDev.Agent.csproj` has no `IronDev.Infrastructure` project reference. Generated `*_wpftmp.csproj` files are ignored as build leftovers.
- `IronDev.TauriShell` source does not reference `IronDev.Infrastructure`, SQL/Dapper, Weaviate, or repository types directly.
- WPF run report UI is registered through `IronDev.Client` service adapters: `IRunReportService` and `IRunEvidenceService` resolve to `IRunReportsApiClient`.

## Partially Clean

- Product CLI commands call `IronDev.Api` through `IronDev.Client`/`IIronDevApiClient`, so current ticket persistence is API-backed rather than SQL/file/repository-backed.
- `IronDev.TauriShell` uses generated OpenAPI TypeScript types and browser API helpers, not the C# `IronDev.Client`. That keeps it away from Infrastructure but means it is not literally on the same typed client library.
- `IronDev.Client` includes product-shaped run status/report methods mapped to `/api/runs/*`; creation/lifecycle and events are still missing.

## Leaking Or Missing

- Current Product CLI ticket commands are now API/client-boundary clean; they use `IIronDevApiClient`.
- Product CLI has only four ticket commands: `ticket create`, `ticket list`, `ticket show`, and `ticket import-github-issue`.
- Product CLI lacks project, document, memory, run status, run report, ticket generation, and build run commands.
- Run event workflows still leak or do not exist. Product-shaped status/report routes now exist as `/api/runs/{runId}` and `/api/runs/{runId}/report`, backed by current report storage.
- No SSE endpoint exists for `GET /api/runs/{runId}/events`.
- No SSE run event streaming exists.
- No dedicated agent/build workflow controller exists. Build/proposal behavior is currently ticket/proposal-shaped, and internal dogfood build/test workflows live in ReplayRunner.
- `DocumentsController.ResolveDocument` exists but is stubbed.
- Document-to-ticket generation exists as an internal smoke command, not as a product API/client route.

## Counts

| Evidence area | Count |
|---|---:|
| Actual IronDev.Api routes | 86 |
| Planned endpoint gaps documented | 12 |
| HTTP-backed typed client methods, excluding overlapping product facade | 83 |
| Product `IIronDevApiClient` facade methods | 12 |
| Product CLI commands | 6 |
| ReplayRunner/dogfood commands | 76 |

## CLI Classification

| Classification | Count |
|---|---:|
| Product | 6 |
| Internal Dogfood | 50 |
| Smoke Test | 22 |
| Replay/Test Harness | 4 |
| Deprecated | 0 |
| To Be Moved | 0 |

## Boundary Violations Found

| Violation | Evidence | Severity | Follow-up |
|---|---|---|---|
| Product CLI surface is incomplete. | Ticket and run status/report commands exist; projects, documents, memory, generation, and build-run commands are missing. | High | Add API/client-backed project, document, memory, generation, and build commands. |
| Durable run lifecycle is incomplete. | `/api/runs/{runId}` and `/api/runs/{runId}/report` exist over report storage, but run creation and events do not. | High | Add run starters and later SSE `/api/runs/{runId}/events`. |
| Document resolution is stubbed. | `POST /api/projects/{projectId}/documents/{documentId}/resolve` returns not implemented. | Medium | Implement or remove from product inventory until ready. |
| Document-to-ticket product route is missing. | Only internal smoke command exists. | Medium | Add API and client method for document-version ticket generation. |
| ReplayRunner command surface is broad and product-shaped in places. | 76 internal commands, including `memory search`, `docs list`, and `build disposable run`. | Medium | Keep labelled internal; split or rename only in a later refactor ticket. |

## Recommended Follow-Up Tickets

1. Add missing Product CLI commands for projects, documents, memory search, ticket generation, and build runs.
2. Add durable run creation/lifecycle backing for build, test, memory index, and agent runs.
3. Add SSE run event streaming after durable run lifecycle exists: `GET /api/runs/{runId}/events`.
4. Add product API/client route for document-version to ticket generation.
5. Decide whether `DocumentsController.ResolveDocument` should be implemented or removed from the product route surface.
6. Split or rename ReplayRunner/dogfood commands so product-shaped internal commands cannot be mistaken for public CLI commands.
7. Collapse overlapping `IIronDevApiClient`, `IAuthApiClient`, and `ITicketsApiClient` surface into one clear public contract.
