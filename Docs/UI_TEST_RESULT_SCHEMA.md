# UI Test Result Schema

Codex reads UI test results from a compact JSON shape embedded in `tools/ui-tests/reports/latest-ui-test-report.md` and derivable from Playwright JSON output.

## Shape

```json
{
  "runId": "ui-1716500000000",
  "suite": "IronDev UI contract journeys",
  "status": "passed",
  "startedUtc": "2026-05-25T00:00:00.000Z",
  "finishedUtc": "2026-05-25T00:00:01.234Z",
  "durationMs": 1234,
  "baseUrl": "http://127.0.0.1:5173",
  "steps": [
    {
      "name": "user can log in and reach tenant-aware shell",
      "status": "passed",
      "error": null,
      "screenshotPath": null,
      "tracePath": null
    }
  ],
  "artifacts": {
    "jsonReport": "reports/playwright-report.json",
    "htmlReport": "reports/html/index.html",
    "traceFiles": [],
    "screenshots": [],
    "videos": []
  }
}
```

## Field Rules

- `runId`: unique per report generation.
- `suite`: stable human-readable suite name.
- `status`: `passed` or `failed`.
- `startedUtc` and `finishedUtc`: ISO 8601 UTC timestamps.
- `durationMs`: total duration across reported test results.
- `baseUrl`: UI shell URL used for the run.
- `steps`: flattened test-level results.
- `steps[].status`: `passed`, `failed`, or `skipped`.
- `steps[].error`: failure text or `null`.
- `steps[].screenshotPath`: first relevant failure screenshot path or `null`.
- `steps[].tracePath`: first relevant trace path or `null`.
- `artifacts.jsonReport`: Playwright JSON report path.
- `artifacts.htmlReport`: Playwright HTML report entry path.
- `artifacts.traceFiles`: trace artifact paths.
- `artifacts.screenshots`: screenshot artifact paths.
- `artifacts.videos`: video artifact paths.

The schema is intentionally small. Codex should not need to infer workflow state from screenshots.
