# Self-Improvement Campaign 157

## Purpose

Campaign 157 strengthens the governed autonomy control plane without loosening the safety boundary.

The goal is to move from a mostly deterministic skeleton toward a more mature agent layer:

- runtime-configurable model profiles,
- local provider support,
- a real ArchitectAgent review path,
- traceable campaign evidence,
- preserved ConscienceAgent and ThoughtLedger governance,
- preserved caged BuilderAgent repair loop.

## What Changed

- `AgentModelResolver` now supports `OpenAI`, `LocalOpenAI`, and `Ollama`.
- `ModelProfile` includes optional provider endpoint and timeout metadata.
- `ArchitectAgent` now reviews proposals against weighted context and safety boundaries.
- `campaign self-improvement-157` produces a traceable campaign report for tickets 144-156.
- `Docs/AGENTS.md` is the current source of truth for agent roles, authority, and boundaries.

## Boundary

This campaign does not grant free autonomy.

It does not:

- allow real repository writes,
- let agents mutate accepted project memory,
- let ResearchAgent override project memory,
- let SentinelAgent create tickets,
- let TesterAgent fix failures,
- let BuilderAgent write outside the disposable workspace,
- bypass ConscienceAgent or ThoughtLedger.

## Validation

Primary smoke:

```text
test run-plan --plan tools/dogfood/test-agent-plans/irondev-self-improvement-campaign-157.json --run-id SelfImprovementCampaign157 --json
```

The smoke validates:

- provider support is visible,
- ArchitectAgent review is available,
- governance remains required,
- real repository writes remain blocked,
- the campaign produces a report under `tools/dogfood/runs/{runId}`.
