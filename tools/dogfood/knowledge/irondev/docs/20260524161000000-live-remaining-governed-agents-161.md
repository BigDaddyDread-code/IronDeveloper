---
id: LIVE_REMAINING_GOVERNED_AGENTS_161
project: IronDev
title: Live Remaining Governed Agents 161
document_type: ArchitectureCheckpoint
authority: Accepted
status: Current
created_utc: 2026-05-24T16:10:00Z
primary_retrieval_questions:
  - Which remaining IronDev agents have opt-in live evidence paths?
  - Does ResearchAgent override project memory?
  - Does QualityAgent override deterministic gates?
  - Does SupervisorAgent bypass governance?
boundary: Opt-in live evidence only. No writes, memory mutation, ticket creation, patch apply, quality override, or self-approval.
---

# Live Remaining Governed Agents 161

IRONDEV-161 completes the current opt-in live governed agent pass for ResearchAgent, QualityAgent, and SupervisorAgent.

ResearchAgent can attempt live model evidence after explicit external evidence is packaged. It remains read-only and cannot override accepted project memory.

QualityAgent can attempt live model evidence after deterministic quality evidence is produced. Deterministic gates remain authoritative.

SupervisorAgent can attempt live model evidence after deterministic orchestration state is known. ConscienceAgent, ThoughtLedger, and deterministic stop conditions remain authoritative.

TesterAgent, ConscienceAgent, and ThoughtLedger intentionally remain deterministic.

Validation is provided by:

```text
campaign live-remaining-agents-161 --run-id LiveRemainingAgents161 --json
```

and:

```text
test run-plan --plan tools/dogfood/test-agent-plans/irondev-live-remaining-governed-agents-161.json --run-id LiveRemainingAgents161 --json
```

This slice does not grant real repository writes, memory mutation, ticket creation, patch application, quality override, or self-approval.
