# Run Report Viewer Service 144

Status: historical WPF note. The `IronDeveloper` WPF project has been retired; product clients must use `/api/runs/*` through `IronDev.Client` or OpenAPI helpers.

## Purpose

This historical slice added the first Run Reports surface over shared C# services.

The retired WPF app read run report files through application services. Current product clients should use the run APIs through `IronDev.Client` or generated OpenAPI helpers.

## Architecture

Correct shape:

```text
Product client
  -> IronDev.Client or OpenAPI helper
  -> /api/runs/* or /api/run-reports/*
  -> run event store / report service
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

Historical WPF workspace:

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
- start CLI processes from product shells
- mutate memory
- mutate real repository files
- change retrieval semantics
- change CLI command semantics
- grant new agent authority

## What This Proves

IronDev can show trace-backed dogfood/build evidence through API-backed report services.

The UI and CLI now have the right long-term relationship:

```text
Product client -> IronDev.Client/OpenAPI -> IronDev.Api
Dogfood CLI    -> shared services / command adapters
```

Not:

```text
Product client -> CLI stdout parser
```
