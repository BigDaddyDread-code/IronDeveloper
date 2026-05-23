---
id: SELF_IMPROVEMENT_CAMPAIGN_157
project: IronDev
title: Self Improvement Campaign 157
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-23T10:57:00Z
primary_retrieval_questions:
  - What is IronDev self-improvement campaign 157?
  - Which agents are mature in the governed autonomy control plane?
  - Does campaign 157 allow real repository writes?
boundary: Governed autonomy only. Real repository writes and self-approval remain blocked.
---

# Self Improvement Campaign 157

Campaign 157 matures the governed autonomy control plane while preserving the safety boundary.

Core outcomes:

- `Docs/AGENTS.md` is the current source of truth for agent roles and authority.
- Model profiles are runtime-configurable.
- OpenAI, LocalOpenAI, and Ollama providers are supported by model profiles.
- ArchitectAgent performs governed architecture review.
- BuilderAgent remains caged inside explicit disposable workspaces.
- ConscienceAgent and ThoughtLedger remain required before write-capable workflows.
- Real repository writes remain blocked.

This document is accepted project memory for the 157 campaign direction. It does not grant agents authority to mutate accepted memory, create tickets directly, or apply patches outside the disposable workspace cage.
