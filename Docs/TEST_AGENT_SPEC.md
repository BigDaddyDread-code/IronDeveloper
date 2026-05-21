# IronDev Test Agent Specification

## Purpose

The Test Agent is the cheap, fast execution agent that runs test plans produced by Codex. It does not analyse deeply, design fixes, or patch code. It executes a structured plan, captures evidence, and returns a compact report so Codex can decide what to do next.

This keeps expensive model time focused on diagnosis and repair while the Test Agent handles repeatable execution.

## Contract

The Test Agent receives a JSON test plan and returns a JSON report.

It must:

- execute steps in order
- run only the requested actions
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
| `replay_run` | Run a replay scenario batch | `Start-BookSellerReplay.ps1` |
| `failure_package` | Generate a Codex handoff package | `IronDev.ReplayRunner failure latest --for-codex` |

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
- Never patch, refactor, or invent fixes.
- Never convert an unsupported command into a fake success.
- Prefer dry-run unless the plan explicitly allows writes.
- Capture raw command output.
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
