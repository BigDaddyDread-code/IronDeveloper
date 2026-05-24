# Advanced Adversarial And Self-Improving Agents 183

## Purpose

IRONDEV-183 adds two governed Alpha agents:

- `DoubtAgent`, formally the Adversarial Review Agent.
- `MemoryImprovementAgent`, a proposal-only self-improving memory reviewer.

The internal nickname for `DoubtAgent` may be AssholeAgent, but the product and documentation name is `DoubtAgent`.

## Boundary

This slice grants no new mutation authority.

- No real repository writes.
- No accepted memory mutation.
- No ticket creation.
- No patch apply.
- No self-approval.
- No ConscienceAgent or ThoughtLedger bypass.
- No infinite revision loops.

## DoubtAgent

`DoubtAgent` stress-tests plans, promotion packages, and proposed changes for hidden assumptions, missing evidence, language-specific risks, governance gaps, and fake confidence.

High or Critical findings require explicit Killjoy rebuttal before promotion. Killjoy is the Code Review Agent terminology for this path. Doubt can force review and revision, but it cannot patch, approve, mutate memory, create tickets, or block forever.

## MemoryImprovementAgent

`MemoryImprovementAgent` reviews completed-run evidence, Doubt findings, Killjoy rebuttals, and promotion outcomes. It starts at Level 1: proposal-only. It may recommend that a proposal should be staged, but it cannot write to a staging area or accepted memory in this slice.

It uses small focused context:

- run trace/report refs
- top relevant evidence refs
- Doubt findings
- Killjoy review
- promotion outcome

It does not load full project memory. It enforces `MaxContextTokens` and `MaxProposalsPerRun`.

During Alpha, accepted-memory key readiness is always false. Future key grants require a reviewed policy, repeated low-noise proposal evidence, reversible versioning, Conscience review, Killjoy review, and human approval.

## Memory Permission Ladder

MemoryImprovementAgent permissions are explicit:

| Level | Name | Authority |
| --- | --- | --- |
| 0 | ReadOnlyObserver | Reads run reports, traces, reviews, human decisions, and accepted memory summaries. Writes nothing. |
| 1 | ProposalOnly | Produces `MemoryImprovementProposal` objects with evidence bundles. Writes nothing. This is the current Alpha level. |
| 2 | StagingAreaWrite | May write to a non-authoritative memory staging area only after MemoryKeyGate approval. Accepted memory remains blocked. |
| 3 | AutoStageLowRiskLessons | May auto-stage narrow observations when strict evidence and safety metrics pass. Architecture, security, authority, and policy changes remain blocked. |
| 4 | AutoApplyTinyNonAuthoritativeMemory | May apply tiny non-authoritative observations/tags only after strong regression history and rollback proof. |
| 5 | AcceptedMemoryMutation | Dangerous key. Not available in Alpha. Even later, this requires Killjoy, Conscience, human approval, trace evidence, rollback, and diff preview. |

The simple rule is: MemoryImprovementAgent earns keys from reviewed outcomes, not intelligence claims.

## Evidence Source Rule

MemoryImprovementAgent is not its own evidence source. It can interpret evidence, but evidence must come from the governed spine:

- run reports
- trace evidence
- build/test results
- TestPlanRunner reports
- Doubt/Critic findings
- Killjoy review
- Conscience review
- human accept/reject/edit outcomes
- memory search traces
- code index or symbol evidence
- promotion decisions

LLM confidence, self-assessment, uncited summaries, and unlinked notes do not count as authority evidence.

Every memory proposal must include a `MemoryProposalEvidenceBundle` with a claim, evidence references, missing evidence, and an evidence boundary. The campaign also writes `memory-proposal-evidence-audit.json`.

## Memory Key Gate

`MemoryKeyGateReview` decides whether a permission increase is even reviewable. In this slice it evaluates the first key, Level 2 staging-area write, and returns `NeedsMoreEvidence`.

The gate tracks:

- proposal count
- human accepted/rejected/edited counts
- unsafe proposal count
- duplicate proposal count
- missing evidence count
- Killjoy approval rate
- human acceptance rate
- context budget health
- retrieval improvement proof

The gate can recommend a permission change only. It does not grant accepted-memory authority.

## CLI

```text
campaign adversarial-memory-agents-183 --run-id <run> --json
agent doubt review --subject "<subject>" --json
agent memory-improvement propose --affected-project IronDev --json
```

## Evidence

The campaign writes:

```text
tools/dogfood/runs/{runId}/doubt-review.json
tools/dogfood/runs/{runId}/killjoy-review.json
tools/dogfood/runs/{runId}/memory-improvement-proposal.json
tools/dogfood/runs/{runId}/memory-key-gate-review.json
tools/dogfood/runs/{runId}/memory-proposal-evidence-audit.json
tools/dogfood/runs/{runId}/conscience-memory-review.json
tools/dogfood/runs/{runId}/trace.json
tools/dogfood/runs/{runId}/report.json
tools/dogfood/runs/{runId}/report.md
```

Run Reports can surface Doubt findings and staged memory proposals from the file-backed report.

## Acceptance

The 183 smoke proves:

- Doubt produces adversarial findings.
- High/Critical findings require rebuttal.
- Killjoy addresses every high/critical finding.
- MemoryImprovementAgent returns one to three proposal-only memory improvements.
- Every memory proposal includes governed evidence.
- MemoryKeyGate requires more evidence before Level 2 staging-area write.
- No proposal directly mutates accepted memory.
- Accepted-memory key readiness is false.
- Real repo writes, accepted memory mutation, ticket creation, and patch apply remain blocked.
