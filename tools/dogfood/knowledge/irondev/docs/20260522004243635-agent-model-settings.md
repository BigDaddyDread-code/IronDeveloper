---
id: agent-model-settings
title: AGENT_MODEL_SETTINGS
version: 2
status: accepted
project: IronDev
type: architecture
document_type: Architecture
authority: Accepted
created: 2026-05-22
created_utc: 2026-05-22T00:42:43.6489925+00:00
source: dogfood-discussion
dogfood_run_id: DogfoodDocsSeed-20260522-014
---

# IronDev Agent Model Settings

## Purpose

IronDev chooses models by agent role, not by one global chat setting.

014 keeps the provider boundary deliberately narrow: model profiles are OpenAI-only for now, but agents reference profile names instead of hardcoded model names. Later provider expansion should happen behind the profile resolver, not inside individual agents.

## Model Profiles

| Profile | Provider | Default model | Purpose |
| --- | --- | --- | --- |
| `cheap-runner` | OpenAI | `gpt-4o-mini` | Literal execution, Test Agent, quality checks, retrieval shaping |
| `standard-reasoner` | OpenAI | `gpt-4o` | Planning and ordinary reasoning |
| `strong-reasoner` | OpenAI | `gpt-5.5` | Architecture and supervision decisions |
| `code-builder` | OpenAI | `gpt-5.5` | Code proposal generation |
| `strong-reviewer` | OpenAI | `gpt-5.5` | Critic/review gates |

## Agent Defaults

| Agent | Default model profile | Purpose |
| --- | --- | --- |
| `SupervisorAgent` | `strong-reasoner` | Coordinate agent workflow and decide when enough evidence exists |
| `PlannerAgent` | `standard-reasoner` | Turn vague goals into ordered plans |
| `ArchitectAgent` | `strong-reasoner` | Protect technical direction and architecture decisions |
| `BuilderAgent` | `code-builder` | Create implementation proposals from grounded context |
| `TesterAgent` | `cheap-runner` | Execute structured plans and return compact evidence reports |
| `QualityAgent` | `cheap-runner` | Run deterministic build/test/format/package checks |
| `RetrieverAgent` | `cheap-runner` | Select project memory with metadata-aware filtering |
| `CriticAgent` | `strong-reviewer` | Challenge assumptions and review deeper risks |
| `SentinelAgent` | `cheap-runner` | Observe campaign/failure/test evidence and emit insight artefacts |
| `ResearchAgent` | `cheap-runner` | Package explicit external evidence as read-only research |
| `ConscienceAgent` | `cheap-runner` | Review proposed actions against evidence and safety boundaries |

## Settings Shape

```json
{
  "ModelProfiles": {
    "cheap-runner": {
      "Provider": "OpenAI",
      "Model": "gpt-4o-mini",
      "Temperature": 0.1,
      "MaxOutputTokens": 1200
    },
    "standard-reasoner": {
      "Provider": "OpenAI",
      "Model": "gpt-4o",
      "Temperature": 0.2,
      "MaxOutputTokens": 3000
    },
    "strong-reasoner": {
      "Provider": "OpenAI",
      "Model": "gpt-5.5",
      "Temperature": 0.2,
      "MaxOutputTokens": 5000
    },
    "code-builder": {
      "Provider": "OpenAI",
      "Model": "gpt-5.5",
      "Temperature": 0.1,
      "MaxOutputTokens": 6000
    },
    "strong-reviewer": {
      "Provider": "OpenAI",
      "Model": "gpt-5.5",
      "Temperature": 0.1,
      "MaxOutputTokens": 4000
    }
  }
}
```

## 014 Implementation Boundary

014 adds the shared skeleton only:

- `ModelProfile`
- `AgentDefinition`
- `IIronDevAgent`
- `IAgentRegistry`
- `IAgentModelResolver`
- `IAgentRunner`
- core agent stubs plus lite observer agents
- one working `TesterAgent` path that executes an existing Test Agent plan

The other seven agents are registered but intentionally not intelligent yet. They return skipped/static results until their proof slices exist.

## Trace Requirement

Every LLM or agent trace should eventually record:

- `DogfoodRunId`
- `AgentRole`
- `Provider`
- `Model`
- `ModelProfile`
- dry-run/write mode
- cost estimate when available

This lets replay runs prove whether a cheap agent was sufficient or whether a stronger model was justified.
