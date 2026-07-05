# BookSeller Alpha Smoke

The alpha smoke path makes the D-series dogfood run repeatable from a fresh checkout.

It proves a narrow thing: IronDev can run the BookSeller `validate-book` fixture through the governed skeleton path using deterministic model output. Gate mode stops at the human approval gate. REL-2 applied mode records an explicit deterministic human approval phrase, requests continuation, and applies through the copy-only workspace spine. REL-3 persisted applied mode drives the authenticated API test host against SQL-backed stores and proves the same trail survives project, ticket, run, approval, continuation, apply, and report persistence.

It does not prove alpha readiness, release readiness, deployment readiness, policy satisfaction, live-model quality, product UI approval recording, commit, push, release, deployment, or batch completion.

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

SQL/API persisted deterministic applied smoke:

```powershell
Scripts/smoke/alpha-smoke.ps1 `
  -Project BookSeller `
  -Ticket validate-book `
  -ModelMode Deterministic `
  -RunUntil Applied `
  -RequireExistingAcceptedApproval
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

Node, UI, and Weaviate are not required by the current deterministic smoke. SQL/API persisted mode uses the in-process API test host and the configured integration-test SQL database; it does not start the product UI or require a live model.

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

REL-2 service-level applied mode:

1. Records deterministic clean critic review evidence.
2. Requires `-RecordHumanApproval` and the exact hash-bound approval phrase template.
3. Records an accepted approval bound to the generated run ID and critic package hash.
4. Requests continuation through `TicketSkeletonRunService.ContinueAsync`.
5. Requests controlled apply through `TicketSkeletonRunService.ApplyAsync`.
6. Verifies the final report reconstructs the applied loop.
7. Verifies the apply-copy receipt exists on disk.

REL-3 SQL/API persisted applied mode:

1. Creates the BookSeller project and ticket through authenticated API routes.
2. Starts the skeleton run through the API and reconstructs the halt report through the API.
3. Records deterministic clean critic review evidence through the critic-review API route.
4. Creates and reads back an accepted approval through the accepted-approval API backed by SQL.
5. Requests continuation through the API and verifies the live SQL-backed approval unblocks the run.
6. Requests controlled apply through the API and verifies the final API report reaches `Applied`.
7. Verifies SQL contains the run, event trail, and accepted approval rows.

## What It Does Not Do

- It does not start the product UI.
- Gate mode does not request or record a critic review.
- Gate mode does not record accepted approval.
- Gate mode does not request continuation.
- Gate mode does not apply to source.
- REL-2 applied mode records deterministic smoke evidence only; it does not prove SQL/API persistence.
- REL-3 persisted mode creates accepted approval through the API, but it does not prove product UI approval recording.
- It does not release or deploy.

## Expected Artifacts

Gate mode writes:

- `run-receipt.json`
- `alpha-smoke-result.json`
- `alpha-smoke-summary.md`
- `alpha-smoke.trx`

The receipt records model mode, run-until target, run ID, gate state, critic package hash, approval target hash, named gaps, and boundary language. REL-3 receipts also record API/SQL persistence, project/ticket IDs, accepted approval ID, apply receipt path/hash, and the final reconstructed state.

Readiness mode writes only `alpha-smoke-result.json` and `alpha-smoke-summary.md`; it does not advertise a `run-receipt.json` because no skeleton run has executed yet.

## Boundary

Smoke output is evidence only.

Root safety is a precondition for smoke execution. It is not evidence, approval, or execution authority.

A successful smoke run is not alpha readiness.
