---
id: LIVE_CRITIC_PLANNER_AGENTS_159
project: IronDev
title: Live Critic And Planner Agents 159
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
dogfood_run_id: LiveCriticPlanner159
created_utc: 2026-05-24T15:59:00Z
primary_retrieval_questions:
- What does IRONDEV-159 prove?
- Can CriticAgent and PlannerAgent use live LLMs?
- Are live CriticAgent and PlannerAgent allowed to mutate files or memory?
boundary: Opt-in live model evidence only. No writes, memory mutation, ticket creation, patch apply, self-approval, or ungated autonomy.
---

# Live Critic And Planner Agents 159

IRONDEV-159 extends opt-in live governed model execution to CriticAgent and PlannerAgent.

CriticAgent can attempt a live model call while reviewing a failure package. PlannerAgent can attempt a live model call while drafting a test plan or classifying a product-spike intake prompt.

The deterministic result remains in force. Live model output is advisory evidence only.

Validated command:

```text
campaign live-critic-planner-159 --run-id <run> --json
```

The smoke proves:

- CriticAgent deterministic fallback review still works.
- PlannerAgent deterministic product-spike intake still works.
- Both agents record opt-in live local provider attempts.
- Unavailable providers fall back safely.
- Real repo writes, memory mutation, ticket creation, patch apply, and self-approval remain blocked.
