# C# Dogfood Runner 143

## Purpose

This slice moves the primary Test Agent/dogfood plan execution path from PowerShell into `IronDev.ReplayRunner`.

PowerShell scripts remain as compatibility wrappers, but the main execution owner is now C#.

## What Changed

Added a C# `TestPlanRunner` path behind:

- `test run-plan --plan <path> --run-id <run> --json`
- `dogfood run-plan --plan <path> --run-id <run> --json`
- `agent tester run-plan --plan <path> --run-id <run> --json`

`Invoke-TestAgentPlan.ps1` now delegates to `test run-plan`.

The previous script body is preserved as `Invoke-TestAgentPlan.Legacy.ps1` for compatibility fallback while older actions are ported.

## Standard Run Folder

Each C# runner execution writes:

```text
tools/dogfood/runs/{runId}/report.json
tools/dogfood/runs/{runId}/test-agent-report.json
tools/dogfood/runs/{runId}/trace.json
tools/dogfood/runs/{runId}/report.md
tools/dogfood/runs/{runId}/evidence/
tools/dogfood/runs/{runId}/logs/
```

## Compatibility Rule

Native C# actions run without PowerShell.

If a plan contains an unported action, `TestPlanRunner` fails over to `Invoke-TestAgentPlan.Legacy.ps1` and marks the report with:

```json
{
  "compatibility_mode": true,
  "compatibility_warnings": []
}
```

That fallback is intentional Alpha debt, not the preferred path.

## Native Actions In This Slice

The C# runner owns the important 140-142 path:

- `memory_search`
- `agent_tester_run_plan`
- `buildagent_trace_smoke`
- `builder_repair_loop_smoke`
- `cli_command_surface_cleanup`
- `csharp_dogfood_runner_smoke`

## Boundary

This changes the dogfood execution layer only.

It does not:

- change Test Agent plan semantics
- change memory retrieval semantics
- change builder or governance semantics
- grant agents new authority
- permit real repository writes
- remove PowerShell compatibility scripts

## What This Proves

IronDev can now run dogfood plans through C# as the primary engine, while old PowerShell entrypoints continue to work as wrappers.

This is the right foundation for the future WPF cockpit: buttons can call C# services/commands instead of shelling through a PowerShell test runner.
