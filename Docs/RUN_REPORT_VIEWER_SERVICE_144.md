# Run Report Viewer Service 144

Status: historical WPF note. The `IronDeveloper` WPF project has been retired; product clients must use `/api/runs/*` through `IronDev.Client` or OpenAPI helpers.

## Purpose

This slice adds the first durable Run Reports surface over shared C# services.

The WPF app reads run report files through application services. It does not shell out to `IronDev.ReplayRunner`, parse stdout, or couple the UI to CLI command names.

## Architecture

Correct shape:

```text
WPF UI
  -> RunReportsViewModel
  -> IRunReportService / IRunEvidenceService
  -> file-backed run report reader
```

The CLI remains a separate front door for Codex, dogfood plans, CI-style validation, and human debugging.

## What Changed

Added shared contracts:

- `IRunReportService`
- `IRunEvidenceService`
- `RunReportSummary`
- `RunReportDetail`
- `RunStageStatus`
- `RunAttemptSummary`
- `RunRepairSummary`
- `RunEvidenceItem`

Added file-backed implementation:

- `FileRunReportService`

Added WPF workspace:

- `RunReportsViewModel`
- `RunReportsView`
- `RunReports` workspace navigation item

Added dogfood smoke:

- `run-report viewer-smoke`
- `irondev-run-report-viewer-service-144.json`

## Data Source

Alpha reads file-backed reports from:

```text
tools/dogfood/runs/{runId}/
```

Supported report names include:

- `builder-repair-loop-report.json`
- `build-run-report.json`
- `report.json`
- `test-agent-report.json`

The reader is tolerant of missing markdown, missing evidence folders, malformed JSON, and unknown fields.

## Boundary

This is read/report UI infrastructure.

It does not:

- execute BuilderAgent
- apply patches
- start CLI processes from WPF
- mutate memory
- mutate real repository files
- change retrieval semantics
- change CLI command semantics
- grant new agent authority

## What This Proves

IronDev can show trace-backed dogfood/build evidence in WPF through shared C# services.

The UI and CLI now have the right long-term relationship:

```text
WPF UI  -> shared services
CLI     -> shared services / command adapters
```

Not:

```text
WPF UI -> CLI stdout parser
```
