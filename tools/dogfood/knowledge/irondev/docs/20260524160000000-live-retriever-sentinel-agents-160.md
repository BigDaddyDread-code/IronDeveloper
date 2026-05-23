---
id: LIVE_RETRIEVER_SENTINEL_AGENTS_160
project: IronDev
title: Live Retriever And Sentinel Agents 160
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
dogfood_run_id: LiveRetrieverSentinel160
created_utc: 2026-05-24T16:00:00Z
primary_retrieval_questions:
- What does IRONDEV-160 prove?
- Can RetrieverAgent and SentinelAgent use live LLMs?
- Can live RetrieverAgent override memory ranking?
- Can live SentinelAgent create tickets or mutate memory?
boundary: Opt-in live model evidence only. No ranking override, writes, memory mutation, ticket creation, patch apply, self-approval, or ungated autonomy.
---

# Live Retriever And Sentinel Agents 160

IRONDEV-160 extends opt-in live governed model execution to RetrieverAgent and SentinelAgent.

RetrieverAgent can attempt a live model call after the deterministic memory search and weighted context bundle are produced. SentinelAgent can attempt a live model call after deterministic insight classification.

The deterministic result remains in force. Live model output is advisory evidence only.

Validated command:

```text
campaign live-retriever-sentinel-160 --run-id <run> --json
```

The smoke proves:

- RetrieverAgent deterministic weighted context packaging still works.
- SentinelAgent deterministic observed/affected project insight classification still works.
- Both agents record opt-in live local provider attempts.
- Unavailable providers fall back safely.
- Ranking override, real repo writes, memory mutation, ticket creation, patch apply, and self-approval remain blocked.
