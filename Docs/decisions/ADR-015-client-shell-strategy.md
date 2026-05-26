# ADR-015: Client And Shell Boundary Strategy

## Status

Accepted.

## Decision

`IronDev.Api` is the product boundary. Forward shells and the product CLI should use typed client APIs instead of calling Infrastructure, repositories, file-backed report storage, SQL, Weaviate, or builder services directly.

```text
WPF / Tauri / Product CLI
  -> IronDev.Client or generated OpenAPI client
  -> IronDev.Api
  -> IronDev.Infrastructure
```

## Consequences

- `IronDeveloper` WPF remains in the repo as the legacy desktop shell, but it must reference `IronDev.Client`, not `IronDev.Infrastructure`.
- `IronDev.TauriShell` is a forward shell spike. It uses API/OpenAPI calls and must not reference storage/provider internals.
- `tools/IronDev.Cli` is the public product CLI. It currently calls `IronDev.Api` directly with `HttpClient`; moving it to `IronDev.Client` is the next boundary-hardening step.
- `tools/IronDev.ReplayRunner` is internal dogfood/replay infrastructure. It may keep smoke, replay, campaign, benchmark, and diagnostics commands, but they are not public product CLI commands.
- Direct disk report reads are allowed inside dogfood/internal tooling only. Product clients should read run status, report, and events through planned `/api/runs/{runId}`, `/api/runs/{runId}/report`, and `/api/runs/{runId}/events` endpoints. Today only `/api/run-reports/*` exists.

## Verification

`ApiBoundaryTests` fails normal validation when `IronDev.Client`, `tools/IronDev.Cli`, `IronDev.TauriShell`, or the WPF shell reintroduce forbidden Infrastructure coupling. It does not yet assert that Product CLI uses `IronDev.Client`, because the current repo would fail that future-state check.
