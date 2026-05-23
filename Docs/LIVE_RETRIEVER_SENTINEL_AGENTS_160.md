# Live Retriever And Sentinel Agents 160

## Purpose

IRONDEV-160 extends the opt-in live governed agent path to RetrieverAgent and SentinelAgent.

The goal is to let these agents use configured model profiles as advisory evidence while preserving deterministic memory retrieval, authority ranking, project scoping, and insight classification.

## What Changed

- `RetrieverAgent` can attempt an opt-in live model call after the real memory search path returns a weighted context bundle.
- `SentinelAgent` can attempt an opt-in live model call while producing an observational insight artefact.
- `campaign live-retriever-sentinel-160` proves deterministic fallback, live-provider attempt recording, and no-write governance.
- `agent retriever search` and `agent sentinel observe` accept `--live-llm` and `--model-profile`.

## Boundary

Live model output is evidence only.

RetrieverAgent and SentinelAgent still must not:

- change memory ranking
- override accepted memory
- cross project boundaries silently
- create tickets
- mutate memory
- patch files
- apply patches
- approve writes
- block builds by themselves

If the configured provider is unavailable, the agent records the attempt and deterministic behaviour remains in force.

## Validation

Primary smoke:

```text
test run-plan --plan tools/dogfood/test-agent-plans/irondev-live-retriever-sentinel-agents-160.json --run-id LiveRetrieverSentinel160 --json
```

The smoke proves:

- deterministic RetrieverAgent weighted context remains available
- deterministic SentinelAgent insight classification remains available
- opt-in live local provider attempts are recorded for both agents
- provider failure falls back safely
- ranking override, real repo writes, memory mutation, ticket creation, patch apply, and self-approval remain blocked
