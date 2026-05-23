# Live Governed Agent Execution 158

## Purpose

IRONDEV-158 turns the ArchitectAgent from an LLM-ready reviewer into the first opt-in live governed agent execution path.

The goal is narrow: prove a selected agent can call a configured model profile while preserving deterministic fallback, traceable evidence, and hard safety boundaries.

## What Changed

- `AgentLlmClient` maps runtime model profiles to OpenAI, LocalOpenAI, and Ollama services.
- `ArchitectAgent` can attempt a live model call only when explicitly requested.
- `agent architect review` supports `--live-llm` and `--model-profile`.
- `campaign live-governed-agent-158` proves fallback, live-provider attempt handling, and missing-evidence behaviour.
- The main alpha regression pack includes the 158 smoke.

## Boundary

Live model execution does not grant new authority.

Blocked:

- real repository writes
- memory mutation
- ticket creation
- patch application
- self-approval
- ungated autonomy

If the live model is unavailable or returns unusable output, ArchitectAgent keeps the deterministic architecture review in force and records the fallback.

## Validation

- ReplayRunner build
- `irondev-live-governed-agent-execution-158.json`
- Code Standards Alpha
- main alpha regression pack
