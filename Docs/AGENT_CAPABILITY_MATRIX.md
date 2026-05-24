# Agent Capability Matrix

## Purpose

This document states what IronDev agents can actually do today.

Primary retrieval question: which IronDev agents are real vs stubbed?

## Capability Classes

- Real execution: performs an actual tool/control-plane action.
- Deterministic wrapper: produces structured deterministic output around existing commands.
- Memory wrapper: wraps memory search/context packaging.
- Stub: registered but not meaningfully intelligent yet.
- Unsafe/not allowed: intentionally blocked by Alpha safety boundary.

## Current Agents

| Agent | Current class | Current capability | Trust boundary |
| --- | --- | --- | --- |
| SupervisorAgent | Governed autonomous wrapper | Retrieves weighted memory, asks ConscienceAgent to review, asks ThoughtLedger to explain, and dispatches TesterAgent for bounded plans only when allowed. | Tier 3 read/test/report and Tier 4 disposable-workspace apply autonomy only; real repo writes, tickets, memory mutation, and self-approval remain blocked. |
| PlannerAgent | Deterministic wrapper | Drafts Test Agent plan JSON from a goal. | Draft only; does not execute or patch. |
| ArchitectAgent | Stub | Registered as part of the eight-agent skeleton. | Do not trust for architecture decisions yet. |
| BuilderAgent | Trace-backed disposable repair loop | Builder path has preview/disposable smoke coverage, a traceable internal spine, and a first real caged repair loop for Solitaire with intentional build/test failures and bounded repairs. | Writes remain limited to explicit disposable workspaces. No real repo writes, memory mutation, guardrail mutation, or self-approval. |
| TesterAgent | Real execution wrapper | Runs Test Agent plans through CLI/PowerShell and returns structured report. | Executes plans; does not interpret or fix failures. |
| QualityAgent | Deterministic wrapper | Runs code standards/tooling gate through existing plan machinery. | Reports quality; does not refactor. |
| RetrieverAgent | Memory wrapper | Runs memory search and packages a weighted context bundle with included/rejected sources, ranking evidence, risk notes, and source guidance. | Uses dogfood memory search; not a full retrieval planner yet. |
| CriticAgent | Deterministic wrapper | Reviews failure packages and returns evidence-backed recommendation. | Reviews only; no code changes. |
| DoubtAgent | Deterministic adversarial reviewer | Stress-tests plans, promotion packages, and proposed changes for hidden assumptions, missing evidence, governance gaps, language-specific risks, and fake confidence. | Review only. High/Critical findings require Killjoy rebuttal, but DoubtAgent cannot patch, mutate memory, create tickets, approve writes, or block forever. |
| MemoryImprovementAgent | Level 1 proposal-only memory reviewer | Reads focused completed-run evidence and proposes memory improvements with evidence bundles, token/proposal budgets, and a MemoryKeyGate review. | Proposal only. It cannot write staging memory or accepted memory. Accepted-memory key readiness remains false in Alpha; no ticket creation, patching, or self-approval. |
| SentinelAgent | Deterministic wrapper | Observes campaign/failure/test evidence and emits insight artefacts. | Observational only; no tickets, memory writes, patches, or approvals. |
| ResearchAgent | Deterministic wrapper | Packages explicit external evidence as a ResearchPackage. | Read-only; does not decide architecture, write memory, create tickets, patch code, or override project memory. |
| ConscienceAgent | Deterministic safety reviewer | Reviews proposed actions and returns Allow, Block, or NeedsMoreEvidence. | Reviews only; no patching, ticket creation, memory mutation, or self-approval. |
| ThoughtLedger | Deterministic visible reasoning service | Explains visible reasoning summaries, uncertainty, blocked actions, and safer alternatives. | Explanation only; no hidden chain-of-thought, writes, patches, tickets, or memory mutation. |
| GovernedActionReview | Deterministic control-plane package | Combines ConscienceAgent and ThoughtLedger into one review package. | Reviews and explains only; does not execute the proposed action. |

## Blunt Assessment

The agent layer is a useful skeleton, not a mature autonomous system.

The safest real path today is:

`Codex -> memory search -> TesterAgent plan -> failure package -> Codex analysis`

## Boundary

No agent currently has permission to mutate project source files autonomously.

## 137 PlannerAgent Product Spike Intake

PlannerAgent can now classify a vague new product build prompt into a bounded ProductSpikeCandidate intake. This is deterministic structured planning support only; it does not create memory, tickets, disposable workspaces, patches, or real repository writes.

## 140 BuilderAgent Trace Spine

BuilderAgent now has a deterministic traceable disposable-build spine for future heavy-duty work. Internally, the spine separates build brief compilation, architecture planning, file manifest planning, patch writing, workspace mutation tracking, build/test attempt recording, failure classification, repair planning, retry budget control, and evidence packaging.

This does not make BuilderAgent a mature autonomous engineer. It gives future disposable build runs somewhere structured to record every important move before the real 141 repair-loop build path grows.

## 141 Trace-Backed BuilderAgent Repair Loop

BuilderAgent can now run `agent builder repair-loop` for the Solitaire disposable workspace proof. The loop intentionally creates a build failure, repairs the WPF project reference inside the disposable workspace, intentionally creates a rule-test failure, repairs `KlondikeRules.cs` inside the disposable workspace, and records final build/test success in the 140 trace model.

This is the first real caged repair loop. It still does not allow real repository writes, memory mutation, guardrail mutation, regression pack mutation, or self-approval.

## 183 DoubtAgent And MemoryImprovementAgent

DoubtAgent is the formal Adversarial Review Agent. It can produce high/critical findings and force explicit Killjoy rebuttal before promotion, but it remains review-only.

MemoryImprovementAgent can review completed-run traces, Doubt findings, Killjoy rebuttals, and promotion outcomes, then produce a small number of proposal-only memory improvements. Each proposal must cite governed evidence. MemoryKeyGate currently requires more evidence before Level 2 staging-area write. It cannot mutate accepted memory. During Alpha, accepted-memory key readiness is explicitly false.
