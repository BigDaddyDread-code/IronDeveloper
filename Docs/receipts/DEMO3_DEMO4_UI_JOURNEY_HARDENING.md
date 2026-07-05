# DEMO-3 / DEMO-4 Receipt - Governed Run UI Journey and Dead-End Hardening

## Purpose

Make the flow shell show the governed run journey from ticket readiness through final Applied report, and make demo-path dead ends explain themselves.

DEMO-3 closes the UI gap between seeded backend run history and the product surface: an existing linked run can hydrate from backend evidence-summary/report/package endpoints instead of requiring hand-copied IDs.

DEMO-4 hardens the visible route states that were most likely to stall the demo: empty board columns, blocked readiness, linked-run absence, final report absence, and model-mode visibility.

## Files Changed

- `IronDev.TauriShell/src/flow/FlowShell.tsx`
- `IronDev.TauriShell/src/flow/board/BoardScreen.tsx`
- `IronDev.TauriShell/src/flow/workitem/WorkItemScreen.tsx`
- `IronDev.TauriShell/tests/skeleton-run-stages.spec.ts`
- `Docs/receipts/DEMO3_DEMO4_UI_JOURNEY_HARDENING.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

## What DEMO-3 Adds

- Existing ticket run history hydrates through `/evidence-summary`, `/report`, and `/critic-package`.
- Seeded `Applied` tickets can open directly to the final report view.
- Final report view shows loop status, named gaps, apply receipts, and the non-authority boundary.
- Successful controlled apply moves the user to the final report instead of leaving them in review.
- A Governance Library cross-link keeps the user inside product surfaces instead of forcing ID archaeology.

## What DEMO-4 Adds

- `Applied` tickets map to the Done board column.
- approval/review tickets map to the Review column.
- Empty board columns explain the reason and the next safe action.
- Ticket readiness blocked/empty states name the backend source of truth and next safe action.
- Linked-run evidence has loading, empty, error, and populated messages.
- The shell displays model-mode honestly: deterministic local alpha in test environments, with live mode only when backend run evidence says so.

## Boundaries

- No backend authority is added.
- No frontend state is treated as approval, continuation, apply permission, release readiness, or deployment readiness.
- No fake approval, fake continuation, fake apply receipt, or fake final report is created.
- No direct SQL final-state insert.
- No API controller, SQL migration, release, deployment, source mutation, provider, or workflow executor change.
- UI hydration is read-only evidence display.

## Known Limits

- Browser proof uses Playwright with mocked API responses for focused UI behavior.
- Long-lived local API/browser click-through remains a separate manual or live-e2e proof.
- The skeleton accepted-approval endpoint does not currently expose a typed approval phrase field in the flow shell; this PR does not invent one in the client.

## Validation

- Tauri focused browser proof: `npm test -- tests/skeleton-run-stages.spec.ts` passed, 9/9.
- Tauri build/typecheck: `npm run build` passed; Vite reported the existing large bundle warning.
- C11 secret scan: `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --no-restore --nologo --verbosity minimal` passed, 9/9; existing NU1510 warnings.
- `git diff --check`: passed.

## Review Line

The UI may guide the user through the governed path. It does not become the path's authority.

## Killjoy

A final report on screen is evidence. It is not release permission.
