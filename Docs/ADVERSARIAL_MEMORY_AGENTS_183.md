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

`MemoryImprovementAgent` reviews completed-run evidence, Doubt findings, Killjoy rebuttals, and promotion outcomes. It proposes staged memory improvements only.

It uses small focused context:

- run trace/report refs
- top relevant evidence refs
- Doubt findings
- Killjoy review
- promotion outcome

It does not load full project memory. It enforces `MaxContextTokens` and `MaxProposalsPerRun`.

During Alpha, accepted-memory key readiness is always false. Future key grants require a reviewed policy, repeated low-noise proposal evidence, reversible versioning, Conscience review, Killjoy review, and human approval.

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
- MemoryImprovementAgent returns one to three staged proposals.
- No proposal directly mutates accepted memory.
- Accepted-memory key readiness is false.
- Real repo writes, accepted memory mutation, ticket creation, and patch apply remain blocked.
