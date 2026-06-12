# PR75 Real DB Tool Gate Decision Smoke Receipt

Purpose: prove the durable tool gate decision store works against a real SQL Server database through approved stored procedures only.

This is a gate decision evidence smoke, not an approval, executor, workflow, source apply, or memory promotion smoke.

## Required order

Run database-backed smoke commands sequentially.

```powershell
.\Database\apply-migrations.ps1 -ConnectionString "<connection-string>"
.\Database\verify-migrations.ps1 -ConnectionString "<connection-string>"
.\Database\smoke-tool-request.ps1 -ConnectionString "<connection-string>"
.\Database\smoke-tool-gate-decision.ps1 -ConnectionString "<connection-string>"
```

## What the PR75 smoke proves

- `governance.ToolGateDecision` exists.
- `governance.usp_ToolGateDecision_Record` records a durable decision.
- The decision links to an existing `governance.ToolRequest`.
- Recording also appends a `tool.gate.decision.recorded` governance event.
- The decision can be queried by project, tool request, and correlation.
- SQL constraints preserve no approval grant, no execution grant, no source mutation, and no memory promotion.

## Boundary language

- Gate decision is not approval.
- Gate pass is not human approval.
- Gate decision is not execution permission.
- Gate decision is not workflow progress.
- Gate decision is not source apply.
- Gate decision is not memory promotion.
- Audit evidence is not approval.
- API status is not governance.
- Human review remains required for source apply and memory promotion.

## Expected summary shape

```json
{
  "durableGateDecisionRecorded": true,
  "gateDecisionIsApproval": false,
  "gatePassIsHumanApproval": false,
  "executionPermissionGranted": false,
  "toolExecuted": false,
  "sourceApplied": false,
  "memoryPromoted": false
}
```
