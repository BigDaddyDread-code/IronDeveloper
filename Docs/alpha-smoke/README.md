# BookSeller Alpha Smoke

The alpha smoke path makes the D-series dogfood run repeatable from a fresh checkout.

It proves a narrow thing: IronDev can run the BookSeller `validate-book` fixture through the governed skeleton path using deterministic model output. Gate mode stops at the human approval gate. REL-2 applied mode records an explicit deterministic human approval phrase, requests continuation, and applies through the copy-only workspace spine.

It does not prove alpha readiness, release readiness, deployment readiness, policy satisfaction, live-model quality, SQL/API persistence, product UI approval recording, commit, push, release, deployment, or batch completion.

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

Deterministic single-ticket applied smoke:

```powershell
Scripts/smoke/alpha-smoke.ps1 `
  -Project BookSeller `
  -Ticket validate-book `
  -ModelMode Deterministic `
  -RunUntil Applied `
  -RecordHumanApproval `
  -ApprovalPhrase "I approve continuation for run <runId> package <hash>"
```

Useful output switches:

```powershell
Scripts/smoke/alpha-smoke.ps1 -CheckOnly -Json
Scripts/smoke/alpha-smoke.ps1 -CheckOnly -Markdown
```

By default, mutation-shaped smoke output is written outside the repository under the local app-data IronDev alpha-smoke folder.

## Prerequisites

- Fresh checkout of `BigDaddyDread-code/IronDeveloper`.
- .NET SDK available on `PATH`.
- Git available on `PATH`.
- Clean source worktree before running `-RunUntil Gate`.
- No secrets required for deterministic mode.

Node, SQL, API, UI, and Weaviate are not required by the current deterministic service-level smoke. That is an honest current gap, not a hidden pass.

## What The Gate Smoke Does

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

## What The Applied Smoke Adds

1. Records deterministic clean critic review evidence.
2. Requires `-RecordHumanApproval` and the exact hash-bound approval phrase template.
3. Records an accepted approval bound to the generated run ID and critic package hash.
4. Requests continuation through `TicketSkeletonRunService.ContinueAsync`.
5. Requests controlled apply through `TicketSkeletonRunService.ApplyAsync`.
6. Verifies the final report reconstructs the applied loop.
7. Verifies the apply-copy receipt exists on disk.

## What It Does Not Do

- It does not start API or UI.
- It does not write tickets through the API.
- It does not connect to SQL.
- Gate mode does not request or record a critic review.
- Gate mode does not record accepted approval.
- Gate mode does not request continuation.
- Gate mode does not apply to source.
- Applied mode records deterministic smoke evidence only; it does not prove product UI/API approval recording.
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
