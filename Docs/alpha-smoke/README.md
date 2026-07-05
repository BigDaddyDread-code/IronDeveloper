# BookSeller Alpha Smoke

The alpha smoke path makes the D-series dogfood run repeatable from a fresh checkout.

It proves a narrow thing: IronDev can run the BookSeller `validate-book` fixture through the governed skeleton path until the human approval gate using deterministic model output.

It does not prove alpha readiness, release readiness, deployment readiness, approval, policy satisfaction, workflow continuation, controlled source apply, live-model quality, or batch completion.

## Current Runnable Path

Default check-only mode writes no smoke artifacts:

```powershell
Scripts/smoke/alpha-smoke.ps1 -CheckOnly
```

Deterministic single-ticket gate smoke:

```powershell
Scripts/smoke/alpha-smoke.ps1 `
  -Project BookSeller `
  -Ticket validate-book `
  -ModelMode Deterministic `
  -RunUntil Gate
```

Useful output switches:

```powershell
Scripts/smoke/alpha-smoke.ps1 -CheckOnly -Json
Scripts/smoke/alpha-smoke.ps1 -CheckOnly -Markdown
```

By default, gate-mode output is written outside the repository under the local app-data IronDev alpha-smoke folder.

## Prerequisites

- Fresh checkout of `BigDaddyDread-code/IronDeveloper`.
- .NET SDK available on `PATH`.
- Git available on `PATH`.
- Clean source worktree before running `-RunUntil Gate`.
- No secrets required for deterministic mode.

Node, SQL, API, UI, and Weaviate are not required by the current D-2a deterministic service-level smoke. That is an honest current gap, not a hidden pass.

## What The Current Gate Smoke Does

1. Verifies the repo, toolchain, BookSeller sample, and BookSeller fixture ticket.
2. Builds the IronDev solution.
3. Runs the D-2a deterministic smoke test.
4. Copies `Samples/BookSeller` to a disposable temporary source.
5. Runs the real `TicketSkeletonRunService`.
6. Applies the deterministic Builder proposal in a disposable workspace.
7. Runs real `dotnet build` and `dotnet test` against that workspace.
8. Produces a hash-sealed critic package.
9. Verifies the run halted at `PausedForApproval`.
10. Writes `run-receipt.json`, `alpha-smoke-result.json`, and `alpha-smoke-summary.md`.

## What It Does Not Do

- It does not start API or UI.
- It does not write tickets through the API.
- It does not connect to SQL.
- It does not request or record a critic review.
- It does not create accepted approval.
- It does not request continuation.
- It does not apply to source.
- It does not release or deploy.

## Expected Artifacts

Gate mode writes:

- `run-receipt.json`
- `alpha-smoke-result.json`
- `alpha-smoke-summary.md`
- `alpha-smoke.trx`

The receipt records model mode, run-until target, run ID, gate state, critic package hash, approval target hash, named gaps, and boundary language.

Readiness mode writes only `alpha-smoke-result.json` and `alpha-smoke-summary.md`; it does not advertise a `run-receipt.json` because no skeleton run has executed yet.

## Boundary

Smoke output is evidence only.

Root safety is a precondition for smoke execution. It is not evidence, approval, or execution authority.

A successful smoke run is not alpha readiness.
