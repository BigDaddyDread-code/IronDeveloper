# IronDev LocalTest Manual Test Plan

This plan verifies the Tauri cockpit against isolated LocalTest data. It must not touch the normal development database or real project rows.

## Reset And Launch

1. Reset the isolated database and seed repeatable data:

   ```powershell
   .\tools\localtest\reset-localtest-data.ps1
   ```

2. Start the backend API and Tauri shell in LocalTest mode:

   ```powershell
   .\tools\localtest\start-pr-manual-test.ps1 -Reset -FreshSession
   ```

   For browser-only UI testing instead of the desktop shell:

   ```powershell
   .\tools\localtest\start-pr-manual-test.ps1 -Reset -FreshSession -BrowserOnly
   ```

3. Run the Playwright/manual-smoke hybrid and generate a report:

   ```powershell
   .\tools\localtest\Invoke-LocalTestSmoke.ps1 -Reset -StartServices
   ```

   Reports are written to `tools/localtest/reports/latest-localtest-report.md` and `tools/localtest/reports/latest-localtest-report.json`.

## Seeded Login

- Email: `bob@irondev.local`
- Password: `change-me-local-only`
- Tenant: `Local Test Tenant`
- Project: `IronDev Local Test Project`
- Realistic fixture: `BookSeller Test Fixture`

## Checklist

- Confirm `/api/environment` reports `LocalTest`, database `IronDeveloper_Test`, and `isTestEnvironment: true` through the authenticated UI or a token-bearing API request.
- Confirm the UI header shows a `LocalTest` environment badge.
- Sign in with the LocalTest user.
- Select `Local Test Tenant` if tenant selection is shown.
- Confirm the project selector loads `IronDev Local Test Project`.
- Open the Tickets workspace.
- Confirm these seeded tickets are visible:
  - `Add Governed Tool Architecture`
  - `Wire Start Disposable Run`
  - `Improve Ticket Workspace UI`
- Select `Add Governed Tool Architecture`.
- Confirm Execution Evidence shows an honest empty state with no linked run.
- Confirm Review Latest Run is disabled when no real linked run exists.
- Confirm Start Disposable Run is enabled only when the selected ticket/project/session are valid and readiness is not explicitly blocked.
- Select `Wire Start Disposable Run`.
- Confirm Execution Evidence shows linked run `localtest-run-ticket-3002`.
- Click Review Latest Run.
- Confirm the in-ticket run review panel opens without losing the selected ticket.
- Confirm the run review shows run status, run ID, ticket title, disposable run marker, output summary, and events/evidence state.
- Open Documents if the current shell exposes the workspace or API-backed list.
- Confirm these seeded documents are visible through the API/UI:
  - `Alpha UI Manual Test Notes`
  - `Code Standards Draft`
  - `Test Agent Direction`
- Confirm unfinished or unavailable actions show honest disabled reasons or backend errors.
- Confirm no fake green success state appears for actions that did not actually run.
- Confirm the normal development database was not modified.

## API Smoke

Use these endpoints while the LocalTest API is running:

```powershell
Invoke-RestMethod http://localhost:5000/health
```

`/api/environment` and cockpit data should be loaded through the UI or through a token-bearing API client.

## Safety Rules

- The LocalTest database name must contain `Test`.
- The LocalTest workspace and logs roots must contain `Test`.
- Real repository writes must remain disabled in LocalTest.
- Missing backend functionality must be disabled or return an honest error. Do not fake successful execution.
