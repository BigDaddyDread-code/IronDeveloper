# BuildAgent Traceable Disposable Build Spine 140

## Purpose

Slice 140 gives heavy-duty BuilderAgent work a trace spine before expanding the real disposable repair loop.

Slice 139 proved IronDev can generate, build, and test a disposable Solitaire spike without touching the real repository. Slice 140 adds the structure needed to explain future heavier runs: context, safety, visible reasoning, builder planning, workspace mutation, build attempts, test attempts, repair attempts, evidence artefacts, and final recommendation.

## Decision

BuilderAgent can grow heavier internally, but the top-level agent crew stays stable.

Top-level roles remain:

- SupervisorAgent
- RetrieverAgent
- ConscienceAgent
- ThoughtLedger
- BuilderAgent
- TesterAgent
- CriticAgent
- QualityAgent, internally nicknamed Killjoy

BuilderAgent internals may include:

- BuildBriefCompiler
- ArchitecturePlanner
- FileManifestPlanner
- PatchWriter
- WorkspaceWriter
- BuildRunner
- TestRunner
- FailureClassifier
- RepairPlanner
- RetryController
- EvidencePackager

These are BuilderAgent internals, not new top-level agents.

## Trace Output

The trace spine records:

- BuildRunTrace
- AgentStageTrace
- ContextTrace
- ConscienceDecisionTrace
- ThoughtLedgerTrace
- BuilderPlanTrace
- WorkspaceMutationTrace
- BuildAttemptTrace
- TestAttemptTrace
- RepairAttemptTrace
- EvidenceArtifact
- FinalBuildRunReport

The Alpha storage path is file-backed:

```text
tools/dogfood/runs/{runId}/build-run-trace.json
tools/dogfood/runs/{runId}/build-run-report.json
tools/dogfood/runs/{runId}/build-run-report.md
tools/dogfood/runs/{runId}/evidence/*
```

## Command

```text
agent builder trace-smoke --project Solitaire --dogfood-run-id <run> --json
```

This command creates a synthetic trace. It does not build Solitaire, create a disposable workspace, generate app files, or apply patches.

## What The Smoke Proves

The smoke proves a future cockpit/report can represent:

- Retriever context success.
- Conscience allow decision.
- ThoughtLedger visible reasoning.
- BuilderAgent plan and file manifest.
- Build failure.
- Repair attempt.
- Test failure.
- Second repair attempt.
- Final build/test success.
- Real repo mutation count zero.
- Disposable changed-file count.
- Final recommendation.

## Boundary

This slice is traceability only.

It must not:

- create a real disposable workspace
- generate Solitaire app files
- apply patches
- mutate project memory
- create tickets automatically
- approve writes
- weaken ConscienceAgent, ThoughtLedger, SupervisorAgent, TesterAgent, CriticAgent, or QualityAgent boundaries

## Next Slice

141 should run the Solitaire disposable build through the trace spine, so the real build/test/repair path writes into the same model instead of assembling evidence after the fact.
