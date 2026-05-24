# IronDev Architecture

## Overview

IronDev now uses a thin desktop-client boundary for product workflows tracked by #68.

```text
IronDeveloper WPF
  -> IronDev.Client
  -> IronDev.Api
  -> IronDev.Infrastructure
```

The WPF app is responsible for presentation, navigation, transient UI state, and desktop-only affordances such as screenshots, clipboard, windows, and local settings. Product persistence and workflow behaviour go through `IronDev.Client` and `IronDev.Api`.

## Boundary

`IronDeveloper/IronDev.Agent.csproj` references `IronDev.Client` and `IronDev.Core`. It must not reference `IronDev.Infrastructure`.

`IronDev.Client` owns:

- JWT/session handling
- typed API clients
- shared HTTP error handling
- API-facing workflow methods for WPF ViewModels

`IronDev.Api` owns:

- auth and tenant selection
- project, ticket, document, memory, chat, code-index, build, run-report, and profile endpoints
- request-scoped tenancy from JWT claims
- orchestration through `IronDev.Infrastructure`

`IronDev.Infrastructure` owns SQL, code indexing, LLM integrations, build orchestration, run reports, and other product services behind the API.

## Local-only exceptions

The WPF client keeps only desktop-local behaviour:

- `IAppSettingsService` for client presentation/settings preferences
- screenshot capture and testing companion local files
- shell/window/navigation state
- in-memory trace display state
- markdown rendering fallback for document preview
- prompt playground compatibility shims used for diagnostics, not authoritative product persistence

These exceptions must not persist product state directly to SQL or Infrastructure services.

## Guardrail

Run:

```powershell
powershell -ExecutionPolicy Bypass -File ./Scripts/Assert-WpfApiBoundary.ps1
```

The guard fails if `IronDeveloper` reintroduces forbidden WPF coupling such as `IronDev.Infrastructure`, `IronDev.Services`, or the old direct service interface names.
