# Live Remaining Governed Agents 161

IRONDEV-161 completes the current opt-in live governed agent pass for ResearchAgent, QualityAgent, and SupervisorAgent.

## Purpose

This slice gives the remaining useful execution/review agents an optional live model evidence path while preserving deterministic control.

It does not make the safety layer autonomous or soft.

## What Changed

- ResearchAgent can attempt a live model call after explicit external evidence is packaged.
- QualityAgent can attempt a live model call after deterministic gate evidence is produced.
- SupervisorAgent can attempt a live model call after deterministic orchestration state is known.
- `campaign live-remaining-agents-161` proves fallback and live-attempt evidence for all three agents.

## Boundaries

- ResearchAgent remains read-only external evidence packaging.
- QualityAgent remains advisory; deterministic build/test/format/package/code-standards gates remain authoritative.
- SupervisorAgent remains governed; ConscienceAgent, ThoughtLedger, and deterministic stop conditions remain authoritative.
- No real repository writes.
- No memory mutation.
- No ticket creation.
- No patch application.
- No quality override.
- No agent self-approval.

## Deliberately Deterministic Agents

TesterAgent, ConscienceAgent, and ThoughtLedger stay deterministic for now.

That is intentional:

- TesterAgent executes and reports.
- ConscienceAgent gates safety.
- ThoughtLedger explains visible reasoning summaries.

Those roles should not become loose live-model decision makers before the next autonomy phase.

## Validation

Primary smoke:

```text
test run-plan --plan tools/dogfood/test-agent-plans/irondev-live-remaining-governed-agents-161.json --run-id LiveRemainingAgents161 --json
```

Direct campaign:

```text
campaign live-remaining-agents-161 --run-id LiveRemainingAgents161 --json
```

Expected outcome:

- ResearchAgent live provider attempt is recorded.
- QualityAgent live provider attempt is recorded.
- SupervisorAgent live provider attempt is recorded.
- Fallback remains deterministic if the live provider is unavailable.
- Governance boundaries remain blocked.

## Blunt Assessment

This finishes the sensible first live-agent pass.

The agent layer is now capable enough to produce live evidence where useful, while the safety-critical gates remain boring and deterministic.
