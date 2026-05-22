---
id: DISPOSABLE_WORKSPACE_PHASE_REPORT
project: IronDev
title: Disposable Workspace Phase Report
document_type: Decision
authority: Accepted
status: Current
source: C:\Users\bob\source\repos\AIDeveloper\Docs\DISPOSABLE_WORKSPACE_PHASE_REPORT.md
dogfood_phase: DisposableWorkspaceApplyProof
created_utc: 2026-05-23T10:30:00Z
primary_retrieval_questions:
  - Is IronDev ready for disposable workspace patch application?
  - What did the disposable workspace phase prove?
  - Can IronDev apply patches to the real repository?
  - What is the next safe step after disposable workspace apply proof?
---

# Disposable Workspace Phase Report

## Decision

IronDev has completed the first disposable workspace apply proof.

Decision:

```text
CONDITIONAL GO: IronDev may continue designing controlled write workflows, but real repository patch application remains blocked.
```

The current permission is narrow:

```text
Patch proposal -> explicit disposable workspace -> build/test -> comparison review -> failure/success package -> human review evidence
```

This does not grant permission to apply patches to the developer working tree or production repository.

## What Is Proven

The BookSeller disposable workspace proof now shows:

- BookSeller can be copied into an explicit disposable workspace.
- The disposable workspace is outside the real repository tree.
- Before hashes and after hashes are captured.
- A patch proposal can be loaded from an external JSON proposal file.
- Patch application is allowed only inside the disposable workspace.
- Build and test commands run against the disposable workspace path.
- Changed files are compared against the proposal scope.
- Real repository fixture hashes remain unchanged.
- IDA comparison evidence reports scope, unsafe changes, architecture alignment, and recommendation.
- Failure/success package output includes repro command, validation command, changed files, build/test result, hash summary, safety rules, and next action.
- Human approval evidence explicitly says approval does not mean real repository write permission.

The phase also includes a fail-closed proof:

- An unsafe proposal that attempts to write outside the disposable workspace is rejected.
- The patch is not applied.
- The real repository remains unchanged.
- A result/failure package is still written.

## What Is Not Proven

This phase does not prove:

- Real repository patch application.
- Autonomous repair.
- Production project mutation.
- Multi-ticket patch application.
- Long-running agent repair loops inside the disposable workspace.
- That Codex will always obey retrieved memory during code generation.
- That every future patch proposal is safe without the disposable cage.

## Safety Boundary

The current hard boundary remains:

```text
No agent flow may mutate the real repository.
```

Patch apply may run only when:

- the workspace path is explicit,
- the workspace is disposable,
- the workspace is outside the real repo,
- the source project is copied into that workspace,
- hashes are captured before and after,
- the patch proposal ID is recorded,
- the source project/ticket/document context is recorded,
- build/test evidence is captured,
- a result or failure package is written,
- and human review evidence is produced.

If any required evidence is missing, the apply path must fail closed.

## Current Role Of IDA

IDA is the control and evidence system.

IDA may:

- resolve project and ticket context,
- retrieve and weight project memory,
- create or reset disposable workspaces,
- apply proposal files only inside disposable workspaces,
- run deterministic build/test checks,
- compare before/after code evidence,
- create result/failure packages.

IDA may not:

- apply patches to the real repo,
- approve its own changes for production,
- let TesterAgent repair code,
- treat green disposable smoke tests as full autonomy.

## Next Safe Step

The next safe build phase is controlled write path design, not real write implementation.

Recommended next work:

1. Keep disposable workspace apply in the regression pack.
2. Add more negative safety proposals when new apply features are introduced.
3. Design the human-controlled promotion path from disposable evidence to a reviewed patch/PR.
4. Do not build ResearchAgent or SentinelAgent yet; those remain future architecture discussion until the write cage is stable.

## Blunt Assessment

The disposable workspace cage now exists and has a positive proof and a fail-closed proof.

That is enough to continue toward controlled write path design.

It is not enough to let IronDev write to the real repository.
