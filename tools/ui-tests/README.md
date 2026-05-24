# IronDev UI Test Harness

This folder is the Codex-testable UI harness for the future IronDev UI shell.

It is not a production UI shell, not a redesign, and not a demo app. It exists so future UI work is tested through stable workflow contracts instead of screenshots or visual guessing.

## Commands

```powershell
npm install
npm run ui-test:install
npm run ui-test:all
npm run ui-test:report
```

Journey-specific commands:

```powershell
npm run ui-test:login-smoke
npm run ui-test:project-open-smoke
npm run ui-test:ticket-review-smoke
npm run ui-test:document-to-ticket
npm run ui-test:build-run-review-smoke
```

## Current State

There is no committed future web UI shell yet. The journey specs are intentionally skipped contract tests until a shell exists and exposes stable `data-testid` selectors.

The harness can still be validated today:

- Playwright config loads.
- Pending journey specs are discoverable.
- JSON and HTML report locations are configured.
- Markdown report generation works from real Playwright JSON or the sample fixture.

## Base URL

Set `IRONDEV_UI_BASE_URL` when a testable UI shell exists:

```powershell
$env:IRONDEV_UI_BASE_URL = "http://127.0.0.1:5173"
npm run ui-test:all
```

## Output

- JSON: `tools/ui-tests/reports/playwright-report.json`
- HTML: `tools/ui-tests/reports/html`
- Markdown summary: `tools/ui-tests/reports/latest-ui-test-report.md`
- Traces, screenshots, and videos: `tools/ui-tests/test-results`
