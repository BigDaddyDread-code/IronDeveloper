---
id: 20260523090000000-disposable-workspace-apply-proof
project: IronDev
title: DISPOSABLE_WORKSPACE_APPLY_PROOF
document_type: Architecture
authority: Accepted
source: C:\Users\bob\source\repos\AIDeveloper\Docs\DISPOSABLE_WORKSPACE_APPLY_PROOF.md
dogfood_run_id: DisposableWorkspacePhase-104
created_utc: 2026-05-23T09:00:00.0000000+00:00
---

# Disposable Workspace Apply Proof

## Decision

IronDev has completed the Alpha preview, test, and report phase.

Current decision:

`CONDITIONAL GO`: IronDev is ready to design and prove disposable workspace patch application, but is not ready to apply patches outside a disposable workspace.

This phase exists to build the cage before allowing any write behaviour.

## Current Proven Control Plane

IronDev can already:

- Query accepted memory through CLI.
- Report raw vector rank and final authority rank.
- Promote exact accepted docs when appropriate.
- Keep IronDev and BookSeller project scopes separate.
- Run TesterAgent plans through CLI and PowerShell.
- Package RetrieverAgent context from memory search.
- Draft bounded plans through PlannerAgent.
- Coordinate narrow memory and TesterAgent loops through SupervisorAgent.
- Review failure package evidence through CriticAgent.
- Run deterministic quality gates through QualityAgent.
- Produce builder previews without writing files.
- Generate failure packages that Codex can inspect.
- Run regression packs through the Alpha test phase.

The safest real path today is:

```text
Codex -> memory search -> TesterAgent plan -> failure package -> Codex analysis
```

The next path should become:

```text
Codex -> weighted context -> builder patch proposal -> disposable workspace apply -> build/test -> IDA code comparison -> failure/success package -> Codex/human decision
```

## Hard Boundary

No production or developer working tree mutation by agent flows.

No patch may be applied unless all of the following are true:

- The target workspace is disposable.
- The target workspace path is explicit.
- The target workspace is outside the real repository working tree.
- The source project is copied into the workspace, not edited in place.
- Before hashes are captured.
- After hashes are captured.
- The run has a trace ID.
- The run records source project, ticket, and source document version.
- The run records the patch/proposal ID.
- Approval state is recorded.
- Build/test result is captured.
- A failure package can be generated.
- The workspace can be reset or deleted.

If any of these are missing, the apply step must fail closed.

## Phase Roles

### Codex

Codex remains the strongest code-generation and repair brain.

Codex should propose code changes, analyse build/test failures, suggest repair patches, explain trade-offs, and use IronDev memory plus failure packages as grounding.

Codex must not apply patches directly to the real repo, bypass disposable workspace safety, weaken tests to get green, or ignore project-scoped context.

### IDA / IronDev

IDA is the control, memory, evidence, orchestration, and review system.

IDA should resolve project/ticket/context, retrieve and weight memory, create or validate bounded plans, create/reset disposable workspaces, apply patches only inside disposable workspaces, run deterministic tests and gates, compare before/after code, produce structured evidence, package failures for Codex, and enforce hard safety boundaries.

IDA must not pretend it is a mature autonomous engineer, apply patches to the real repo, approve its own unsafe changes, or treat green harness tests as proof of full autonomy.

### TesterAgent

TesterAgent executes plans. It should run CLI/PowerShell test plans, return structured reports, and stop early on configured failures.

TesterAgent must not interpret failures, repair code, or weaken assertions.

### RetrieverAgent

RetrieverAgent packages memory and context. It should run memory search, package included context, report rejected context, explain weighting/relevance, and preserve project scope.

RetrieverAgent must not hide rejected context when it matters, treat raw vector rank as truth, or cross project boundaries unless explicitly asked.

### CriticAgent

CriticAgent reviews evidence. It should review failure packages, identify likely risk areas, call out fake confidence, and recommend whether Codex should revise.

CriticAgent must not apply fixes or approve production writes.

### QualityAgent / KilljoyAgent

QualityAgent keeps the system honest. It should run deterministic code standards, report allowlisted debt, block obvious rule violations, and keep warning counts visible.

QualityAgent must not refactor code itself or hide warnings.

## Weighted Context Requirement

Before disposable apply, IronDev needs stronger context weighting evidence.

The graph decides what happens next. The weighting decides what the system believes.

A weighted context bundle should include:

- Project.
- Query or goal.
- Trace ID.
- Included sources.
- Rejected sources.
- Raw vector rank.
- Final authority rank.
- Why each source was included.
- Why each source was rejected.
- Risk notes.
- Summary for the agent.

Do not hide weighting inside one unexplained score.

## Native Workflow

IronDev should continue with the native C# LangGraph-style workflow. Do not introduce external LangGraph as a core dependency yet.

The workflow should be explicit state-machine orchestration from goal resolution through context weighting, patch proposal, disposable workspace creation, patch apply inside the workspace, build/test, comparison, result packaging, critic review, and human approval gate.

Every node must have input state, output state, status, evidence, stop reason if failed, and next allowed nodes.

## IDA Code Comparison

IDA should compare code only as a reviewer.

IDA should compare before code, after code, patch proposal, source ticket, source document version, expected scope, changed files, build/test results, failure package, and code standards.

IDA must not approve real repo writes.

## Natural Language Safety Exercise Layer

Natural language should stress the safety boundary.

Expected behaviour:

- Patch apply is only allowed inside an explicit disposable workspace.
- The disposable workspace must be outside the real repo working tree.
- Hash capture is required evidence.
- TesterAgent executes and reports only. Codex may analyse and propose. Human approval remains required.

Natural language can drive the flow, but structured proof must verify it.

## Proposed Proof Sequence

- 104 Disposable Workspace Safety Contract.
- 105 Weighted Context Bundle Contract.
- 106 RetrieverAgent Emits Weighted Context Bundle.
- 107 Supervisor Uses Weighted Context Before Builder Preview.
- 108 Create/Reset Disposable BookSeller Workspace.
- 109 Copy BookSeller Fixture Into Disposable Workspace.
- 110 Apply Patch Only Inside Disposable Workspace.
- 111 Build/Test Disposable Workspace.
- 112 IDA Code Comparison Review.
- 113 Failure Package From Disposable Apply/Build/Test.
- 114 Human Approval Gate Review.
- 115 Controlled Write Path Decision.

## Baseline Gate

Before and after every major PR in this phase, run the deterministic Alpha quality baseline:

- Build.
- Focused tests.
- Format check.
- Package audit.
- Code standards check.

This is the KilljoyAgent baseline. It should remain boring and deterministic.

## Implemented Smoke Proof

The first disposable workspace apply proof is exposed through the ReplayRunner command:

```text
builder disposable-workspace-apply-smoke --project BookSeller --dogfood-run-id <run-id> --proposal <proposal.json>
```

The matching Test Agent plan is:

```text
tools/dogfood/test-agent-plans/bookseller-disposable-workspace-apply-smoke.json
```

This proof uses a controlled BookSeller fixture, copies it to a temp disposable workspace, captures before hashes, applies a proposal-file patch only inside that workspace, captures after hashes, runs build/test inside the workspace, compares changed files against the proposal scope, and writes a Codex-readable result package.

Boundary:

- It does not mutate the real repository.
- It does not apply patches to the real BookSeller project.
- It does not grant autonomous repair.
- Human approval remains a review gate only, not permission to write to the real repo.

## Audit Notes

This phase is aligned with the Alpha Test Phase Report.

The good parts:

- It preserves the no-real-repo-write boundary.
- It treats Codex as the code brain and IDA as the control/evidence system.
- It makes context weighting explicit before write capability.
- It makes disposable workspace apply a proof phase, not a broad autonomy upgrade.
- It requires before/after hashes, trace IDs, source links, approval state, and failure packages.

The risks to watch:

- 106-115 should not be implemented as one large feature rush.
- Weighted context should expose rejected sources, not just included ones.
- Natural-language safety tests must be behaviour assertions, not documentation only.
- Disposable workspace roots must have strict path validation and fail closed.
- Human approval must not be interpreted as permission to mutate the real repo until a later controlled-write decision explicitly allows it.

## Blunt Assessment

IronDev is almost at the full cycle. The missing part is controlled write execution.

Do not add more broad reliability batches now.

Do not make more agents clever just because the harness is green.

The next correct move is:

```text
Build the cage, then allow writing only inside the cage.
```

Codex remains the strongest code-generation and repair brain.

IronDev/IDA becomes the system that decides what context is trusted, where code may be written, what evidence must be produced, and whether the result is safe enough for a human to review.
