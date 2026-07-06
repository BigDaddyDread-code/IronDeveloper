# DEMO-1b / DEMO-2b Receipt - Long-Lived Local API Demo Seed

## Purpose

Close the gap left by DEMO-1a / DEMO-2a.

DEMO-1a proved the backend path through an API integration test host. DEMO-1b makes the default demo seed target a long-lived local API/SQL environment that the product UI can read.

DEMO-2a proved chat -> draft -> confirmed ticket -> startable run through the API test host. DEMO-2b adds an explicit running-API live chat ticket proof behind `-CreateLiveChatTicket`.

## Command Shape

Default non-mutating check:

```powershell
Scripts/demo/demo-seed.ps1 -CheckOnly -Json
```

Default long-lived local seed:

```powershell
Scripts/demo/demo-seed.ps1 -Seed -Project BookSeller -ModelMode Deterministic
```

Explicit proof harness, retained for CI evidence:

```powershell
Scripts/demo/demo-seed.ps1 -Seed -SeedTarget ProofHarness -Project BookSeller -ModelMode Deterministic
```

Explicit DEMO-2b live chat ticket path:

```powershell
Scripts/demo/demo-seed.ps1 -Seed -Project BookSeller -ModelMode Deterministic -CreateLiveChatTicket
```

Explicit live post-seed usability proof:

```powershell
Scripts/demo/demo-seed.ps1 -Seed -Project BookSeller -ModelMode Deterministic -ProveUsable
```

## Files Changed

- `Scripts/demo/demo-seed.ps1`
- `IronDev.IntegrationTests.Api/Demo/DemoSeedApiDrivenTests.cs`
- `IronDev.IntegrationTests/Demo/DemoSeedScriptContractTests.cs`
- `Docs/receipts/DEMO1B_LONG_LIVED_DEMO_SEED.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

## What DEMO-1b Adds

- Default `-Seed` target is now `RunningApi`.
- The script calls `/health`, `/api/auth/login`, `/api/tenants/select`, `/api/environment`, `/api/projects`, `/api/projects/{projectId}/tickets`, skeleton-run endpoints, critic-review, accepted-approvals, continuation, apply, and report endpoints.
- BookSeller source is copied to an isolated local demo source root, not edited in the repo.
- Existing seed receipts are verified against the running API before being reused.
- Existing demo source without a verified receipt blocks as an idempotency conflict instead of being overwritten.
- The receipt records project, ticket, run, approval, report, and redacted local-path references.

## What DEMO-2b Adds

- Live chat ticket creation is opt-in through `-CreateLiveChatTicket`.
- The script uses chat session, chat message, chat completion, draft ticket, draft confirmation, ticket visibility, and skeleton-run start endpoints.
- The confirmed chat ticket is expected to stop at `PausedForApproval`.
- Default DEMO-1b still records `liveChatTicketSeeded = false`.

## What The Usability Proof Adds

- The DEMO-1a proof harness now proves, on every run, that after baseline seeding the environment stays usable: two fresh tickets are created through the product API and each is driven to `PausedForApproval` on real disposable build/test evidence.
- Real, unfabricated evidence is asserted: only a green `dotnet build`/`dotnet test` reaches the gate, the critic package exists on disk, and its hash re-verifies at report time (`SkeletonEvidencePackaged` + `CriticPackage.HashVerified`).
- Repeatability is asserted: the two governed runs are genuinely distinct (distinct run ids and distinct evidence-package hashes).
- The probe stops at the human gate: no accepted approval, no continuation, no apply — and the seeded baseline (`Applied` + `PausedForApproval`) is re-checked to confirm the probe did not disturb it.
- The running-API script offers the same proof live behind `-ProveUsable`; default off so the demo baseline stays clean.

## Boundaries

- No direct SQL final-state insert.
- No frontend fixtures.
- The usability probe stops at the human gate; it never approves, continues, or applies.
- No fake approval.
- No fake continuation.
- No fake apply receipt.
- No release readiness claim.
- No deployment readiness claim.
- No live chat ticket is seeded ahead of the demo unless `-CreateLiveChatTicket` is explicitly supplied.

## Known Limits

- The running API must already be started and configured for deterministic local alpha behavior.
- The script does not start SQL, start the API, start Tauri, or launch a browser.
- The script verifies UI-readable API state; it does not perform a browser click-path journey.
- The proof-harness path remains integration-host evidence, not the long-lived demo.

## Validation

- `dotnet build IronDev.IntegrationTests.Api`: build succeeded, 0 errors.
- `dotnet build IronDev.IntegrationTests`: build succeeded, 0 errors.
- Focused DEMO script contract tests (`DemoSeedScriptContractTests`): 13/13 passed, including the new `DemoSeed_ProvesEnvironmentRemainsUsableForNewTicketsAndRepeatedRuns` and the `-CheckOnly` / root-safety script-execution contracts.
- Not run in this session (requires a real SQL database; `RequiresRealDatabase` + `LongRunning`): `DemoSeedApiDrivenTests.DemoSeed_BaselineHistory_IsApiDrivenAndSqlPersisted`, which now also drives the two-run post-seed usability probe. Must be run on a SQL-backed host before this is treated as proven end to end.

## Review Line

A test-host proof is good evidence. It is not the demo until the UI can read the seeded state.
