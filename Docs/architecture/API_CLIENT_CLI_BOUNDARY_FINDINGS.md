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
- `IronDev.Client` includes product-shaped run status/report/event methods mapped to `/api/runs/*`, plus a ticket build-run starter. Workflow state persistence is still missing, and events are currently report-backed snapshots rather than live durable events.

## Leaking Or Missing

- Current Product CLI ticket commands are now API/client-boundary clean; they use `IIronDevApiClient`.
- Product CLI has four ticket commands, one ticket build-run starter, and run status/report/stream commands.
- Product CLI lacks project, document, memory, ticket generation, and build run commands.
- Run event workflow shape now exists as `/api/runs/{runId}/events`, but it emits report-backed snapshot events because there is still no durable live event store.
- No dedicated agent/build workflow controller exists. Build/proposal behavior is currently ticket/proposal-shaped, and internal dogfood build/test workflows live in ReplayRunner.
- `DocumentsController.ResolveDocument` exists but is stubbed.
- Document-to-ticket generation exists as an internal smoke command, not as a product API/client route.

## Counts

| Evidence area | Count |
|---|---:|
| Actual IronDev.Api routes | 88 |
| Planned endpoint gaps documented | 10 |
| HTTP-backed typed client methods, excluding overlapping product facade | 85 |
| Product `IIronDevApiClient` facade methods | 14 |
| Product CLI commands | 8 |
| ReplayRunner/dogfood commands | 76 |

## CLI Classification

| Classification | Count |
|---|---:|
| Product | 8 |
| Internal Dogfood | 50 |
| Smoke Test | 22 |
| Replay/Test Harness | 4 |
| Deprecated | 0 |
| To Be Moved | 0 |

## Boundary Violations Found

| Violation | Evidence | Severity | Follow-up |
|---|---|---|---|
| Product CLI surface is incomplete. | Ticket, ticket build-run, and run status/report/stream commands exist; projects, documents, memory, and generation commands are missing. | High | Add API/client-backed project, document, memory, and generation commands. |
| Durable run lifecycle is incomplete. | `/api/runs/{runId}`, `/api/runs/{runId}/report`, `/api/runs/{runId}/events`, and ticket build-run start exist, but workflow state is not durably persisted and events are not live-published. | High | Add durable workflow/run state and a durable event store behind the SSE endpoint. |
| Document resolution is stubbed. | `POST /api/projects/{projectId}/documents/{documentId}/resolve` returns not implemented. | Medium | Implement or remove from product inventory until ready. |
| Document-to-ticket product route is missing. | Only internal smoke command exists. | Medium | Add API and client method for document-version ticket generation. |
| ReplayRunner command surface is broad and product-shaped in places. | 76 internal commands, including `memory search`, `docs list`, and `build disposable run`. | Medium | Keep labelled internal; split or rename only in a later refactor ticket. |

## Recommended Follow-Up Tickets

1. Add missing Product CLI commands for projects, documents, memory search, and ticket generation.
2. Add durable run creation/lifecycle backing for build, test, memory index, and agent runs.
3. Replace report-backed SSE snapshot events with durable live run event storage and publishing.
4. Add product API/client route for document-version to ticket generation.
5. Decide whether `DocumentsController.ResolveDocument` should be implemented or removed from the product route surface.
6. Split or rename ReplayRunner/dogfood commands so product-shaped internal commands cannot be mistaken for public CLI commands.
7. Collapse overlapping `IIronDevApiClient`, `IAuthApiClient`, and `ITicketsApiClient` surface into one clear public contract.
