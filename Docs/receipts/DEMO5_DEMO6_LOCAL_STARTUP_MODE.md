# DEMO-5 / DEMO-6 Receipt - Local Startup and Model-Mode Honesty

## Purpose

DEMO-5:

```text
Make demo startup boring.
```

DEMO-6:

```text
Decide what the demo honestly says about model mode.
```

This slice chooses the DEMO-6 deterministic path:

```text
Demo is deterministic-only local alpha preview and says so visibly.
```

## Files Changed

- `Scripts/demo/start-v0.1-demo.ps1`
- `IronDev.IntegrationTests/Demo/DemoStartupScriptContractTests.cs`
- `IronDev.TauriShell/src/flow/FlowShell.tsx`
- `IronDev.TauriShell/tests/skeleton-run-stages.spec.ts`
- `Docs/receipts/DEMO5_DEMO6_LOCAL_STARTUP_MODE.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

## Command Shape

Check-only diagnostic:

```powershell
Scripts/demo/start-v0.1-demo.ps1 -CheckOnly -Json
```

Default demo startup:

```powershell
Scripts/demo/start-v0.1-demo.ps1
```

Open the app after successful startup:

```powershell
Scripts/demo/start-v0.1-demo.ps1 -OpenBrowser
```

Explicitly blocked live-mode request:

```powershell
Scripts/demo/start-v0.1-demo.ps1 -ModelMode Live -CheckOnly -Json
```

## DEMO-5 Behavior

`start-v0.1-demo.ps1` coordinates the existing local demo pieces:

- finds the repository root
- checks the planned demo output root
- checks required local tools
- rejects non-loopback API/UI URLs for the local demo path
- checks local SQL readiness through `Scripts/local/sql-local.ps1 -CheckOnly`
- verifies or starts `IronDev.Api`
- verifies or starts the Tauri/Vite UI
- delegates demo data seeding to `Scripts/demo/demo-seed.ps1`
- prints the app URL
- opens the app only when `-OpenBrowser` is supplied
- reports one primary next safe action when blocked

The script starts services only outside `-CheckOnly` and `-NoStart`.

If any earlier startup stage is `Blocked` or `Failed`, later API start/check,
UI start/check, seed delegation, and browser-open stages are skipped with the
first next safe action preserved. A blocked startup that still starts services
is not blocked.

## DEMO-6 Behavior

The demo is visibly deterministic-only local alpha:

- startup output includes `Deterministic-only local alpha preview. This is not a live model run.`
- the flow shell shows `Model mode: Deterministic-only local alpha preview`
- `-ModelMode Live` blocks with `DemoStartupLiveModelUnsupported`
- `liveModelFallbackAllowed` is always `false`

No silent fallback from live to deterministic is allowed.

The viewer always knows whether the run is deterministic or live.

## Boundaries

- No direct SQL final-state insert.
- No fake approval.
- No fake policy satisfaction.
- No fake continuation.
- No fake apply receipt.
- No remote API/UI target for the local demo startup path.
- No live model fallback.
- No live model proof.
- No release readiness.
- No deployment readiness.
- No source mutation by the startup script.
- No frontend fixture state.
- No authority granted by service startup, seed evidence, model-mode text, or UI banner text.

The startup script coordinates local commands. It does not own governance gates.

A startup script is a coordinator. It is not authority.

## Known Limits

- The script can start local API/UI processes, but it does not guarantee their long-term health after startup.
- Local SQL readiness still depends on the developer machine and configured local database.
- The seed remains delegated to `demo-seed.ps1`; this slice does not change seed semantics.
- Live model demo mode remains intentionally unsupported in this path.
- This slice does not perform browser click-path rehearsal.

## Validation

- `Scripts/demo/start-v0.1-demo.ps1 -CheckOnly -NoStart -Json -ApiBaseUrl http://127.0.0.1:1 -UiBaseUrl http://127.0.0.1:1`: passed and reported blocked API/UI with one next safe action.
- `Scripts/demo/start-v0.1-demo.ps1 -CheckOnly -NoStart -Json -ModelMode Live -ApiBaseUrl http://127.0.0.1:1 -UiBaseUrl http://127.0.0.1:1`: passed and blocked live mode without fallback.
- `Scripts/demo/start-v0.1-demo.ps1 -CheckOnly -NoStart -Json -ApiBaseUrl https://example.com -UiBaseUrl http://127.0.0.1:1`: passed and blocked remote API URL with no process start.
- `Scripts/demo/start-v0.1-demo.ps1 -CheckOnly -NoStart -Json -ApiBaseUrl http://127.0.0.1:1 -UiBaseUrl https://example.com`: passed and blocked remote UI URL with no process start.
- Focused DEMO-5/DEMO-6 startup contract tests: 13/13 passed.
- DEMO seed contract compatibility: 12/12 passed.
- Integration category contract tests: 7/7 passed.
- C11 secret scan: 9/9 passed.
- Tauri skeleton-run Playwright compatibility: 10/10 passed.
- Tauri build: passed with existing large chunk warning.
- `dotnet restore IronDev.slnx`: passed with existing warnings.
- `dotnet build IronDev.slnx --no-restore --nologo --verbosity minimal`: 0 errors / 7 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging exact DEMO-5/DEMO-6 files.

## Review Line

One command may coordinate the demo. It may not invent readiness.

## Killjoy

If the model is fake, say it on the screen.
