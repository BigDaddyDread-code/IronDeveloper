# ADR-015: Client And Shell Boundary Strategy

## Status

Accepted.

## Decision

`IronDev.Api` is the product boundary. Forward shells and the product CLI should use typed client APIs instead of calling Infrastructure, repositories, file-backed report storage, SQL, Weaviate, or builder services directly.

```text
Tauri / Product CLI / Future Clients
  -> IronDev.Client or generated OpenAPI client
  -> IronDev.Api
  -> IronDev.Infrastructure
```

## Consequences

- `IronDeveloper` WPF is retired and removed from the repo/product solution. It must not be restored as a supported shell.
- `IronDev.TauriShell` is the forward IronDev desktop app. It uses API/OpenAPI calls and must not reference storage/provider internals.
- `tools/IronDev.Cli` is the public product CLI. Its current ticket/run commands route through `IronDev.Client`/`IIronDevApiClient`; future product commands must follow the same boundary.
- `tools/IronDev.ReplayRunner` is internal dogfood/replay infrastructure. It may keep smoke, replay, campaign, benchmark, and diagnostics commands, but they are not public product CLI commands.
- Direct disk report reads are allowed inside dogfood/internal tooling only. Product clients read run status, report, and events through `/api/runs/{runId}`, `/api/runs/{runId}/report`, and `/api/runs/{runId}/events`. Live run events are persisted through SQL-backed event history; durable resumable workflow state remains a follow-up.

## Verification

`ApiBoundaryTests` fails normal validation when `IronDev.Client`, `tools/IronDev.Cli`, or `IronDev.TauriShell` reintroduce forbidden Infrastructure coupling. It also fails if the Product CLI reintroduces direct HTTP calls instead of using `IronDev.Client`, or if the retired WPF project is restored to the product build.
