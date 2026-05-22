# IronDev Test Agent Specification

## Purpose

The Test Agent is the cheap, fast execution agent that runs test plans produced by Codex. It does not analyse deeply, design fixes, or patch code. It executes a structured plan, captures evidence, and returns a compact report so Codex can decide what to do next.

This keeps expensive model time focused on diagnosis and repair while the Test Agent handles repeatable execution.

## Contract

The Test Agent receives a JSON test plan and returns a JSON report.

It must:

- execute steps in order
- run only the requested actions
- continue bounded conversations when the plan supplies follow-up facts
- capture stdout, stderr, timings, and artifacts
- stop early when `early_stop_on_failure` is true
- never patch code or invent fixes
- keep the summary concise
- save raw evidence to disk

## Input Shape

```json
{
  "test_run_id": "test-2026-05-22-001",
  "project_id": 42,
  "description": "Test vague user request for BookSeller storage requirements",
  "steps": [
    {
      "step": 1,
      "action": "chat_send",
      "params": {
        "message": "I need to save data",
        "workspace": "Chat"
      }
    },
    {
      "step": 2,
      "action": "chat_send",
      "params": {
        "message": "BookSeller should save books, authors, stock counts, storage locations, and sales history in SQL Server with Dapper. Save that as project knowledge.",
        "workspace": "Chat",
        "previous_from_step": 1
      }
    },
    {
      "step": 3,
      "action": "replay_run",
      "params": {
        "scenario": "BookSellerMvp",
        "reps": 10,
        "dry_run": true,
        "stop_on_failure": true
      }
    }
  ],
  "max_turns": 8,
  "early_stop_on_failure": true
}
```

## Supported Actions

Initial Alpha actions:

| Action | Purpose | Backing command |
| --- | --- | --- |
| `chat_send` | Send one headless chat turn through IronDev routing | `IronDev.ReplayRunner chat send` |
| `chat_conversation` | Run a bounded multi-turn chat using only supplied scenario facts | `IronDev.ReplayRunner chat send` |
| `replay_run` | Run a replay scenario batch | `Start-BookSellerReplay.ps1` |
| `failure_package` | Generate a Codex handoff package | `IronDev.ReplayRunner failure latest --for-codex` |
| `dotnet_build` | Compile a solution/project and capture analyzer/build warnings | `dotnet build` |
| `dotnet_test` | Run unit/integration tests with TRX output | `dotnet test --logger trx` |
| `coverage_run` | Run tests with Coverlet collector | `dotnet test --collect:"XPlat Code Coverage"` |
| `coverage_report` | Convert Cobertura coverage into a readable report, failing with evidence if ReportGenerator is not installed | `dotnet tool run reportgenerator` or `reportgenerator` |
| `format_check` | Verify formatting/style drift | `dotnet format --verify-no-changes` |
| `package_audit` | Check vulnerable NuGet packages | `dotnet package list --project <target> --vulnerable --include-transitive` |
| `code_standards_check` | Run deterministic Alpha code-shape and proof-boundary checks, returning warnings for Codex without patching code | `Invoke-TestAgentPlan.ps1` built-in |
| `weaviate_health` | Verify local Weaviate REST/schema availability | `Invoke-RestMethod http://localhost:8080/v1/meta` |
| `docs_search` | Probe headless dogfood knowledge retrieval and ranking evidence | `IronDev.ReplayRunner docs search` |
| `sql_document_version_smoke` | Create SQL-backed project document versions, index them into semantic memory tables, and assert the current version outranks the stale version | `IronDev.ReplayRunner memory sql-version-smoke` |
| `weaviate_sql_document_version_smoke` | Upsert SQL-backed document-version chunks into Weaviate, run a real near-vector query, and assert final authority ranking corrects stale raw vector preference | `IronDev.ReplayRunner memory weaviate-sql-version-smoke` |
| `cross_project_memory_smoke` | Upsert similar SQL-backed chunks for IronDev and BookSeller, run a real near-vector query, and assert cross-project candidates are rejected for IronDev context | `IronDev.ReplayRunner memory cross-project-smoke` |
| `memory_reindex_freshness_smoke` | Reindex current/stale SQL-backed memory twice, upsert to Weaviate twice, and assert current authority, stale visibility, project scope, exact-title promotion, and duplicate counts | `IronDev.ReplayRunner memory reindex-freshness-smoke` |
| `bookseller_supervised_campaign` | Run the sequential 10-run BookSeller supervised campaign and return a compact campaign report for Codex review | `Invoke-BookSellerSupervisedCampaign.ps1` |

Example `chat_conversation` step:

```json
{
  "step": 1,
  "action": "chat_conversation",
  "params": {
    "workspace": "Chat",
    "initial_message": "I want to persist data",
    "facts_to_reveal": [
      "BookSeller should save books, authors, stock counts, storage locations, and sales history in SQL Server with Dapper. Save that as project knowledge."
    ],
    "expected_outcome": {
      "intent": "SaveDiscussionDocument",
      "requires_action": true,
      "allows_prose_response": false
    },
    "max_turns": 4
  }
}
```

The Test Agent may send only the supplied messages and facts. It must not invent requirements.

Example toolchain step:

```json
{
  "step": 1,
  "action": "dotnet_build",
  "params": {
    "target": "IronDev.slnx"
  }
}
```

Planned CLI actions:

| Action | Purpose | Future command |
| --- | --- | --- |
| `project_create` | Create a new project | `irondev project create` |
| `ticket_add` | Add tickets | `irondev ticket add` |
| `build_run` | Trigger Build Agent | `irondev build run` |
| `test_run` | Run unit/integration tests | `irondev test run` |
| `test_drive` | Drive app endpoints/UI | `irondev test drive` |
| `status` | Read project status/logs | `irondev status` |
| `retrieve` | Force semantic retrieval | `irondev retrieve` |

Unsupported actions must be reported as `SKIPPED_UNSUPPORTED` or `FAILED_UNSUPPORTED`. The Test Agent must not fake success for actions that do not have a real backing command.

Memory spine actions are deliberately small and evidence-first. `weaviate_health` proves the local Weaviate endpoint is reachable. `docs_search` proves the local dogfood knowledge store can return project-scoped ranking evidence. `sql_document_version_smoke` proves a SQL `ProjectDocument`/`ProjectDocumentVersion` pair can be indexed into semantic artefact/chunk tables, linked back to a source discussion, ranked with current-version authority over a stale version, and recorded with a semantic search trace id. `weaviate_sql_document_version_smoke` proves the next link: SQL-backed chunks can be written to Weaviate, returned by a real vector query, and then corrected by IronDev's authority/current/stale ranking when raw vector retrieval prefers stale content. `cross_project_memory_smoke` proves project boundaries hold when raw Weaviate retrieval prefers another project's more similar document.

## Output Shape

```json
{
  "test_run_id": "test-2026-05-22-001",
  "overall_result": "PARTIAL_SUCCESS",
  "summary": "Project context prompt routed correctly, replay passed, no files changed.",
  "key_metrics": {
    "build_success": null,
    "unit_test_pass_rate": null,
    "coverage_percent": null,
    "api_drive_success_rate": null,
    "steps_passed": 3,
    "steps_failed": 0,
    "steps_skipped": 0
  },
  "critical_issues": [],
  "full_log_location": "tools/dogfood/runs/test-2026-05-22-001/logs",
  "time_taken_seconds": 42,
  "next_suggestions": [
    "Promote this plan into a replay regression case."
  ]
}
```

## Result Values

- `SUCCESS`
- `PARTIAL_SUCCESS`
- `FAILED`
- `BLOCKED`
- `SKIPPED_UNSUPPORTED`

## Behaviour Rules

- Be deterministic and literal.
- Do exactly what the test plan says.
- In conversation mode, reveal only supplied facts.
- Never patch, refactor, or invent fixes.
- Never convert an unsupported command into a fake success.
- Prefer dry-run unless the plan explicitly allows writes.
- Capture raw command output.
- Include a trace envelope for each step.
- Validate the final report shape before returning it.
- Keep the final report under roughly 800 tokens.
- Put full evidence in the log folder.

## Codex Handoff

When a step fails, the Test Agent should produce enough evidence for Codex:

- test run id
- failed step
- action
- command
- exit code
- stdout/stderr excerpts
- expected behaviour
- actual behaviour
- artifact paths
- failure package path when available

Codex then owns diagnosis, patching, build/test validation, and replay reruns.

## Local Dogfood Knowledge Commands

The Test Agent and Codex can use the headless runner to build up a local IronDev dogfood knowledge store while BookSeller grows more complex.

```powershell
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- docs clean --project IronDev --force
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- docs import --file .\some-note.md --project IronDev --type Discussion
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- docs search "context agent routing" --project IronDev
```

`docs clean` archives local dogfood docs and seeds a fresh baseline. It does not delete SQL project data.
