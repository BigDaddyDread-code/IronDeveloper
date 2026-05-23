# Governed Planner/Critic Tool Loop 162-167

## Purpose

IRONDEV-162 through IRONDEV-167 add the first native governed reasoning loop where agents request capabilities, the Supervisor/tool runner validates the request, evidence is collected, the Critic reviews it, the Planner revises the plan, and a human escalation gate decides whether the result is ready for review.

This is the leap from structured prompt output to evidence-seeking agent behaviour.

## What Landed

- `AgentToolCapability`, `AgentToolRequest`, `AgentToolResult`, `AgentLoopTrace`, `EvidenceValidationResult`, `HumanEscalationGate`, and `PlannerCriticLoopResult` contracts.
- A C# `GovernedToolRegistry` with safe read/test/report capabilities.
- A C# `GovernedPlannerCriticLoopService`.
- Product-shaped command:

```text
agent loop plan-review --project IronDev --goal "<goal>" --json
```

- Campaign command:

```text
campaign governed-tool-loop-162-167 --run-id <run> --json
```

- File-backed loop trace/report output:

```text
tools/dogfood/runs/{runId}/agent-loop-trace.json
tools/dogfood/runs/{runId}/report.json
tools/dogfood/runs/{runId}/report.md
```

## Tool Contract

Agents do not execute raw shell commands directly. They request named capabilities:

```text
memory.search
code.search
trace.read
failure.latest
test.run-plan
quality.run-gate
project.build
```

The registry decides whether the capability exists, whether it requires mutation, which runtime profiles support it, and what evidence it should return.

## Language-Agnostic Runtime Profiles

.NET remains the first supported runtime adapter, but the loop does not hardcode `.NET` as the only possible project shape.

Runtime profiles now exist for:

```text
dotnet
node
python
```

The agent asks for `project.build`; the runtime profile resolves whether that means `dotnet build`, `npm run build`, or `pytest`/Python-oriented validation in a later adapter.

## Evidence Validation

The evidence validation layer checks required evidence before the revised plan can be trusted.

Current required evidence:

```text
memory.search
code.search
quality.run-gate
```

Missing or failed evidence causes the loop to return `NeedsMoreEvidence`.

## Human Escalation Gate

The loop does not self-approve. If evidence is present, the result is marked ready for human/Codex review. If evidence is missing, it asks for more evidence. If mutation is requested, it fails closed.

## Boundary

This slice does not grant:

- real repository writes
- memory mutation
- ticket creation
- patch application
- raw command execution by agents
- ConscienceAgent bypass
- ThoughtLedger bypass
- self-approval

BuilderAgent remains caged. Write paths remain disposable-workspace-only in their own governed flows.
