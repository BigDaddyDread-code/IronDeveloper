# ADR 018: Passive Agent Containment In Governed Tool Paths

Status: accepted
Date: 2026-05-27

## Context

IronDev has accumulated agent names for several review and governance behaviours. Some of those behaviours are active workflow participants. Others are deterministic checks, policy decisions, or evidence validators.

The Alpha path needs fewer passive agent concepts, not more. Review/check logic should default to a governed tool, policy service, workflow node, or explicit validator unless there is a strong reason to use an agent.

## Decision

The governed Planner tool loop no longer routes trace/failure evidence collection or evidence sufficiency review through `CriticAgent`.

That passive role is merged into deterministic evidence validation and deterministic evidence review:

- `EvidenceValidationService` decides whether required evidence is present.
- The Planner loop emits an `EvidenceReview` stage instead of a `CriticReview` stage.
- Trace and failure evidence requests are made by `EvidenceValidator`, not `CriticAgent`.
- The serialized `CriticReview` result property remains temporarily for report compatibility, but its payload identifies `DeterministicEvidenceReview` as the reviewer.

`CriticAgent` remains available only for its legacy, opt-in failure-package review path until that surface is separately retired or justified.

## Rules

- Do not add a passive agent for code standards, policy checks, evidence sufficiency, simple risk classification, or deterministic validation.
- Put simple governance logic in validators, policy evaluators, workflow nodes, or governed tools.
- Add a new passive agent only with an ADR that explains why a service/tool/workflow node is insufficient.
- Passive review output is advisory evidence. It must not patch, apply, approve, mutate memory, create tickets, or bypass human approval.

## Consequences

- The governed tool path is easier to follow: planner requests evidence, tools collect evidence, validators check sufficiency, human escalation records the gate.
- `CodeStandardsAnalysisTool` remains a governed read-only tool, not a `CodeStandardsAgent`.
- Existing legacy reports that read the `CriticReview` property keep working while the actual payload and stages move away from passive-agent routing.
