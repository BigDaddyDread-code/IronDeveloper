---
id: 20260522004251000-discussion-irondev-self-improvement-loop
project: IronDev
title: DISCUSSION_IRONDEV_SELF_IMPROVEMENT_LOOP
document_type: Discussion
authority: WorkingDraft
source: C:\Users\bob\source\repos\AIDeveloper\Docs\DISCUSSION_IRONDEV_SELF_IMPROVEMENT_LOOP.md
dogfood_run_id: DogfoodDocsSeed-20260522-012
created_utc: 2026-05-22T00:42:51.0132483+00:00
---

# IronDev CLI, Test Agent, Retrieval, And Self-Improvement Loop

## Status

Discussion document. Needs further review before promotion to architecture decisions.

## Summary

IronDev is moving toward a self-improving AI development system, not just a chat wrapper or code generator.

The intended loop is:

1. A user starts with a rough idea, such as building a BookSeller app.
2. IronDev chats with the user to clarify requirements and architectural decisions.
3. The conversation becomes a structured discussion document.
4. The discussion can become an architecture document.
5. The architecture document can be broken into tickets.
6. Tickets are sent to a builder workflow.
7. The builder generates reviewable code proposals.
8. Project memory keeps the builder aligned with original intent.
9. Eventually IronDev applies this loop to itself.

The long-term dogfood goal is for IronDev to inspect its own codebase, create improvement tickets, run builds/tests, analyse failures, and improve itself through controlled, traceable workflows.

## CLI First

Manual testing is not enough for IronDev because the system must validate:

- vague user requests
- chat-to-document behaviour
- document-to-ticket generation
- ticket build quality
- retrieval correctness
- Weaviate chunking
- workflow/agent flow
- code quality
- unit test generation
- coverage reporting
- backend/API behaviour

The CLI becomes the programmable control surface into IronDev. It should be scriptable from PowerShell and built in C# to match the product stack.

Candidate command areas:

- `irondev project create`
- `irondev project list`
- `irondev chat send`
- `irondev document create`
- `irondev document version`
- `irondev ticket add`
- `irondev ticket generate`
- `irondev build run`
- `irondev test run`
- `irondev test coverage`
- `irondev test drive`
- `irondev logs get`
- `irondev memory search`

The immediate priority is stable, machine-readable CLI behaviour, not final command names.

## UI Is Not The First Test Harness

The WPF UI remains important, but the first serious dogfood layer should bypass UI and focus on engines:

- API/service calls
- project creation
- chat requests
- document generation
- ticket generation
- builder execution
- test execution
- database state
- logs
- Weaviate retrieval
- coverage reports

The UI can be polished once the backend workflow is stable.

## Codex As Temporary External Brain

Codex temporarily acts as the external brain for what IronDev should eventually become.

The pattern is:

1. Codex receives a goal.
2. Codex creates a test plan.
3. Codex uses the CLI/Test Agent to drive IronDev.
4. IronDev returns structured outputs.
5. Codex analyses the outcome.
6. Codex decides what to test next or what to fix.

This lets IronDev test the workflow before its internal agent system is mature.

## Cheap Model And Strong Model Split

Cost control is mandatory.

Strong model or Codex should be used for:

- creating test plans
- analysing test reports
- deciding what failed
- proposing fixes
- reviewing architecture
- deciding next iteration steps

Cheap model/Test Agent should be used for:

- running CLI commands
- collecting logs
- generating compact reports
- summarising test runs
- executing predefined test plans
- basic validation
- coverage report summarisation

The expensive model should not sit inside every loop step.

## Messy Testing

IronDev should not only run fixed happy-path tests. The system must be tested with messy, vague, edge-case behaviour:

- vague app ideas
- contradictory requirements
- incomplete requirements
- malformed ticket descriptions
- strange project names
- overlapping documents
- similar projects with different decisions
- old versus new architecture versions
- ambiguous retrieval questions

The goal is to test whether IronDev clarifies, rejects, blocks, or correctly processes imperfect input.

## Coverage And Behavioural Testing

IronDev needs two testing tracks:

1. Unit test and coverage loop.
2. Behavioural CLI/API drive loop.

Coverage is useful, but it does not prove behaviour by itself. The CLI should eventually run tests, produce coverage, return reports to Codex, and let Codex add focused regression tests for real weak spots.

## Generated Project Foundations

IronDev-generated projects should eventually include standard automation foundations:

- CLI project
- test project
- logging setup
- appsettings structure
- health check
- README
- architecture document
- code standards document
- build script
- test script
- coverage script
- local dev instructions

Every generated app should be inspectable, testable, and agent-drivable by default.

## SQL Server And Weaviate Roles

SQL Server remains the source of truth for canonical project records:

- projects
- chats
- discussions
- documents
- document versions
- tickets
- ticket links
- timestamps
- relationships
- traceability

Weaviate is the retrieval/index layer only. It indexes chunks from canonical SQL-backed documents and supports semantic retrieval.

Chunk metadata must be strong:

- ProjectId
- TenantId
- DocumentId
- DocumentVersionId
- TicketId
- SourceChatId
- SourceDiscussionId
- DocumentType
- VersionNumber
- IsCurrent
- Superseded/stale state
- Tags
- Link type
- Chunk index
- Source table/entity

Without strong metadata, retrieval may return semantically similar but wrong chunks.

## Retrieval Drift

Retrieval drift is the first big problem to test.

Risks:

- wrong version is retrieved
- wrong project is retrieved
- old document outranks current architecture
- too much context causes the model to ignore the important source

Example failures:

- Old architecture says Dapper, current architecture says EF Core.
- BookSeller authentication context leaks into IronDev authentication.
- Correct chunks are retrieved but buried in a large prompt.

The first serious retrieval test should create similar projects, similar documents, and multiple versions, then verify the retrieved chunks are current, project-scoped, and authority-ranked.

## Agent Architecture

Start with eight agents maximum:

1. Supervisor/Cortex
2. Planner
3. Architect
4. Builder
5. Tester
6. Quality
7. Retriever
8. Critic

Avoid 12-15 agents initially. Too many agents increase cost, handoff failures, latency, and orchestration complexity.

Split agents only when pain proves the split is needed.

## Agent Flow

Recommended flow:

1. Supervisor receives task.
2. Planner breaks task into steps.
3. Architect checks design impact.
4. Retriever gathers relevant context.
5. Builder proposes changes.
6. Quality checks standards/static analysis.
7. Tester runs tests/coverage/behaviour checks.
8. Critic reviews for deeper problems.
9. Supervisor approves, rejects, loops, or stops.

The Supervisor coordinates but should not do all the thinking.

## Quality And Roslyn

Use deterministic tools before LLM judgement:

- Roslyn analyzers
- StyleCop
- test runners
- coverage tools
- static analysis
- complexity thresholds

LLMs should focus on architectural judgement, intent mismatch, business logic concerns, and whether a patch fits the project direction.

## Main Risks

### Cost

Mitigate with cheap execution models, strong models only for high-value decisions, short reports, early stopping, and deterministic tooling.

### Retrieval Drift

Mitigate with SQL source of truth, strong metadata, project/version filters, retrieval tests, source links, and current-version flags.

### Objective Hacking

Mitigate with critic review, hidden tests, behavioural tests, human approval gates, and quality checks.

### Model Ceiling

The system cannot exceed its underlying model quality by magic. Use strong models where reasoning matters.

### Code Quality Degradation

Mitigate with analyzers, standards, quality gates, critic review, and review-before-merge.

### Information Weighting

The system must learn which retrieved context actually mattered. This is a deep unsolved risk and should be tracked explicitly.

### Weak Learning Agent Claims

A learning/subconscious layer should start simple: run history, repeated failure patterns, successful fixes, bad retrieval examples, good context packs, agent decision logs, and quality regression notes.

## Kitchen Drawer Risk

Every system gets a fallback path. If too much work goes to a fallback/generalist agent, the architecture is failing.

Monitor fallback usage. If fallback use is high, revisit agent boundaries.

## Key Decisions To Promote Later

- Build the CLI first.
- Bypass UI initially.
- Use C# for the CLI.
- Use Codex as the temporary external brain.
- Use cheap models for execution/reporting.
- Use strong models for analysis/fixes.
- SQL Server remains the source of truth.
- Weaviate is retrieval/index only.
- Test retrieval quality early.
- Start with eight agents maximum.
- Retriever is important enough to be its own agent.
- Use Roslyn/static tools before LLM review.
- IronDev-generated projects should eventually include their own CLI/testing foundation.
- IronDev should eventually dogfood itself.
- Information weighting and credit assignment are major unsolved risks.