---
id: CONSCIENCE_AGENT_LITE_131
project: IronDev
title: ConscienceAgent Lite 131
document_type: ArchitectureProof
authority: Accepted
status: Accepted
dogfood_run_id: ConscienceAgentLite131
created_utc: 2026-05-23T13:10:00Z
primary_retrieval_questions:
  - What is ConscienceAgent Lite?
  - How does IDA review proposed actions?
  - What blocks real repo writes?
  - What should happen when evidence is missing?
boundary: ConscienceAgent reviews only. It does not patch, create tickets, mutate memory, or approve itself.
---

# ConscienceAgent Lite 131

ConscienceAgent Lite is IDA's bounded action reviewer.

It reviews proposed IronDev/IDA actions and returns:

- `Allow`
- `Block`
- `NeedsMoreEvidence`

It is deterministic in this slice. It does not run a deep LLM review and does not execute any action.

## What It Blocks

ConscienceAgent blocks:

- real repository writes
- production or developer working tree mutation
- TesterAgent repair/fix requests
- SentinelAgent ticket creation, patching, or memory mutation
- ResearchAgent attempts to override accepted project memory

Disposable workspace actions are allowed only when the disposable workspace boundary is explicit.

## What It Allows

ConscienceAgent can allow evidence-backed review, reporting, planning, and disposable-workspace actions when the boundary evidence is present.

## What Needs More Evidence

ConscienceAgent returns `NeedsMoreEvidence` when:

- evidence is missing
- observed project is missing
- affected project is missing
- disposable workspace boundary evidence is incomplete

## Output Contract

The review output includes:

- decision
- confidence
- reasons
- allowing factors
- blocking factors
- missing evidence
- violated boundaries
- required next steps
- observed project
- affected project
- authority sources
- boundary

## Boundary

ConscienceAgent reviews only. It does not patch, create tickets, mutate memory, approve itself, or execute actions.
