---
id: LIVE_GOVERNED_AGENT_EXECUTION_158
project: IronDev
title: Live Governed Agent Execution 158
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-23T11:58:00Z
primary_retrieval_questions:
  - How does IronDev run live governed agents?
  - Does live model execution grant write authority?
  - What did 158 prove?
---

# Live Governed Agent Execution 158

IRONDEV-158 proves ArchitectAgent can attempt opt-in live model execution through configured OpenAI, LocalOpenAI, or Ollama profiles while preserving deterministic fallback.

The live call is evidence only. It does not grant real repository writes, memory mutation, ticket creation, patch application, self-approval, or ungated autonomy.

The smoke validates:

- deterministic fallback review
- opt-in local live-provider attempt handling
- missing weighted context still returns NeedsMoreEvidence
- governance boundaries remain explicit

If the live provider is unavailable, ArchitectAgent records the failed attempt and keeps deterministic architecture review in force.
