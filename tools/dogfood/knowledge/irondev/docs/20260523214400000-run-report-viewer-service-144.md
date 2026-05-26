---
id: RUN_REPORT_VIEWER_SERVICE_144
project: IronDev
title: Run Report Viewer Service 144
document_type: ArchitectureProof
authority: Accepted
status: Accepted
dogfood_run_id: RunReportViewerService144
created_utc: 2026-05-23T21:44:00Z
primary_retrieval_questions:
  - How does the WPF Run Reports viewer read dogfood run reports?
  - Does the product client shell out to the CLI for run reports?
  - What services back the Run Reports workspace?
  - What run report files can IronDev read?
boundary: Read/report UI infrastructure only. WPF calls shared C# services, not CLI processes; no builder execution, patches, memory mutation, or real repo writes.
---

# Run Report Viewer Service 144

This slice adds the first durable Run Reports surface over shared C# services.

Current product clients read run reports through the API/client boundary; the retired WPF app used:

- `IRunReportService`
- `IRunEvidenceService`
- `FileRunReportService`

The UI layer is:

- `RunReportsViewModel`
- `RunReportsView`
- `RunReports` workspace navigation item

Alpha reads reports from `tools/dogfood/runs/{runId}/` and supports `builder-repair-loop-report.json`, `build-run-report.json`, `report.json`, and `test-agent-report.json`.

The reader is tolerant of missing evidence, missing markdown, malformed JSON, and unknown fields.

Important boundary:

WPF does not shell out to `IronDev.ReplayRunner` or parse stdout. CLI remains a separate front door; WPF calls shared services directly.
