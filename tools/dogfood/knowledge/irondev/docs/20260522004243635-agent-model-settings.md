---
id: 20260522004243635-agent-model-settings
project: IronDev
title: AGENT_MODEL_SETTINGS
document_type: Architecture
authority: Accepted
source: C:\Users\bob\source\repos\AIDeveloper\Docs\AGENT_MODEL_SETTINGS.md
dogfood_run_id: DogfoodDocsSeed-20260522-012
created_utc: 2026-05-22T00:42:43.6489925+00:00
---

# IronDev Agent Model Settings

## Purpose

IronDev should choose models by agent role, not by one global chat setting.

Cheap, fast models should handle literal execution, routing smoke tests, summarisation, and low-risk transformation work. Stronger models should handle high-level planning, ambiguous failure diagnosis, build proposal review, and code-facing reasoning.

## Initial Roles

| Role | Default Tier | Purpose |
| --- | --- | --- |
| `ContextRouter` | Cheap | Route user messages, detect action intent, and block unsafe prose fallback. |
| `TestAgent` | Cheap | Execute structured plans and report results. It does not patch code. |
| `Summarizer` | Cheap | Compress traces, test reports, and conversation history. |
| `KnowledgeImporter` | Cheap/Medium | Convert external notes into reviewable discussion documents. |
| `BuildPlanner` | Strong | Create ticket implementation plans and affected-file proposals. |
| `FailureDiagnoser` | Medium/Strong | Analyse failed replay/build/test evidence and propose likely fix areas. |
| `CodeProposalReviewer` | Strong | Review proposed code changes before approval. |

## Settings Shape

```json
{
  "agent_model_settings": [
    {
      "agent_role": "TestAgent",
      "provider": "OpenAI",
      "model": "gpt-4o-mini",
      "temperature": 0.1,
      "max_tokens": 1200,
      "fallback_model": "gpt-4o-mini",
      "enabled": true
    },
    {
      "agent_role": "BuildPlanner",
      "provider": "OpenAI",
      "model": "gpt-5.5",
      "temperature": 0.2,
      "max_tokens": 8000,
      "fallback_model": "gpt-5.4",
      "enabled": true
    }
  ]
}
```

## Trace Requirement

Every LLM or agent trace should eventually record:

- `DogfoodRunId`
- `AgentRole`
- `Provider`
- `Model`
- `PromptVariantId` when applicable
- dry-run/write mode
- cost estimate when available

This lets replay runs prove whether a cheap agent was sufficient or whether a stronger model was justified.
