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

## Files Changed

- `Scripts/demo/demo-seed.ps1`
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

## Boundaries

- No direct SQL final-state insert.
- No frontend fixtures.
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

- `demo-seed.ps1 -CheckOnly -Json`: passed.
- Focused DEMO script contract tests: 12/12 passed.
- Integration category contract tests: 7/7 passed.
- C11 secret scan: 9/9 passed.
- `dotnet build IronDev.slnx --no-restore --nologo --verbosity minimal`: 0 errors / 4 warnings.
- `git diff --check`: passed.

## Review Line

A test-host proof is good evidence. It is not the demo until the UI can read the seeded state.
