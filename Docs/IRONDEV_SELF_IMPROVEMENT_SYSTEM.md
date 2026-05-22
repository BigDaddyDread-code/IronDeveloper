# IronDev Self-Improvement System

## 1. Core Vision

IronDev is intended to become a self-improving AI software development cockpit.

The core flow is:

1. Start with a natural-language discussion, for example: "I want a bookseller app."
2. Extract decisions, requirements, risks, and architecture choices from that discussion.
3. Save the discussion as a versioned Markdown project document.
4. Generate linked architecture documents, implementation plans, and structured tickets.
5. Feed selected tickets into a builder workflow.
6. Use SQL Server as the source of truth and Weaviate as the retrieval/search layer.
7. Preserve traceability from generated tickets and code changes back to the exact source document version or discussion that produced them.
8. Eventually use the same process on IronDev itself, so IronDev can help improve its own codebase.

The long-term goal is not just "LLM writes code." The goal is a persistent development system that remembers project intent, retrieves the right context, avoids drift, tests itself, and continuously improves.

## 2. Current Operating Rules

These rules are current as of 2026-05-22 and should be treated as accepted architecture unless superseded by a newer accepted document.

- SQL Server owns truth and traceability.
- Weaviate is a retrieval and indexing layer, not the source of truth.
- Raw vector ranking is evidence, not authority.
- IronDev final ranking must apply project scope, authority, currentness, stale penalties, source type, and source links.
- Every project-scoped proof must show project identity in its report.
- BookSeller must never use IronDev or CODEX documents as authority.
- Builder work remains preview-first until a later proof explicitly allows writes.
- Test Agent executes and reports; it does not make architectural decisions.
- Codex/strong model analyses condensed evidence and decides the next fix or test.
- Code standards may pass with intentional warnings, but warnings must be visible, structured, and allowlisted when temporary.

## 3. Core Architecture

### SQL Server

SQL Server remains the authoritative source of truth.

It should store:

- Tenants
- Projects
- Project documents
- Project document versions
- Discussion records
- Tickets
- Ticket dependencies
- Build runs
- Test runs
- Semantic artefacts
- Semantic chunks
- Source links
- Trace records

The key rule is: Weaviate can help find things, but SQL owns truth and traceability.

### Weaviate

Weaviate is the retrieval engine layered on top of SQL-backed project memory.

It should store embedded chunks with strong metadata:

- Tenant ID
- Project ID
- Document ID
- Document version ID
- Document type
- Ticket ID, where relevant
- Source link type
- Authority/current/stale markers
- Created/modified timestamps
- Chunk role or section type

The main risk is retrieval drift: Weaviate may return the wrong project, stale version, or semantically similar but contextually wrong document. IronDev compensates with metadata filtering, authority scoring, stale penalties, project isolation, and semantic trace evidence.

### Native C# LangGraph-Style Workflow

IronDev uses a native C# LangGraph-style workflow rather than depending directly on external LangGraph.

The build workflow direction is:

1. Load ticket.
2. Compile project knowledge context.
3. Create implementation plan.
4. Propose code changes.
5. Request approval.
6. Apply changes only after approval.
7. Build.
8. Test.
9. Report.
10. Feed results back into memory.

The important design decision is that each step is explicit, inspectable, and traceable.

## 4. Why IronDev Needs A CLI

The CLI is the programmable backend interface for dogfooding and automated testing.

Manual testing will not scale to the hundreds or thousands of iterations needed to harden IronDev. The CLI gives Codex, test agents, scripts, and future automation a stable surface to drive the system without relying on the WPF UI.

The CLI should be built in C# and runnable from command line or PowerShell.

Representative command surface:

```bash
irondev health
irondev project list
irondev project current
irondev document add
irondev document list
irondev ticket add
irondev ticket list
irondev build run
irondev test run
irondev test drive
irondev memory search
irondev memory sql-version-smoke
irondev memory weaviate-version-smoke
```

No UI is required for early proof slices. The first priority is a clean, scriptable backend surface.

## 5. Testing Strategy

The intended testing loop is split by model cost and responsibility.

### Codex / Strong Model

Codex acts as the expensive brain.

Responsibilities:

- Generate smart test plans.
- Think through edge cases.
- Analyse condensed reports.
- Decide what to fix next.
- Improve test coverage and test quality.
- Propose code changes.
- Review whether the system is drifting from intent.

Codex should not be wasted on repetitive command execution.

### Cheap Model / Test Agent

A cheaper model handles repetitive evidence gathering.

Responsibilities:

- Execute CLI commands.
- Run test plans.
- Capture command output.
- Run coverage tools.
- Summarise test results.
- Produce short, structured reports for Codex.

The cheap model should not be making major architecture decisions. It is the runner, summariser, and evidence collector.

### Test Style

The testing strategy should include:

- Normal happy-path testing.
- Messy vague inputs.
- Malformed inputs.
- Similar projects/documents to test retrieval bleed.
- Repeated version updates to test stale/current ranking.
- Regression plans for known memory-spine behaviours.
- Build/test/format/package-audit checks.

The early testing focus is retrieval quality, traceability, and write safety, not full autonomous coding.

## 6. Agent Brain Architecture

The recommended starting architecture is eight focused agents.

### 1. Supervisor / Cortex

The conductor.

Responsibilities:

- Own the full process.
- Decide which agent acts next.
- Resolve disagreements.
- Approve final output.
- Stop runaway loops.
- Decide when enough evidence exists.

### 2. Planner

Turns vague goals into structured work.

Responsibilities:

- Break goals into tasks.
- Identify missing information.
- Draft build/test plans.
- Propose sensible ordering.

### 3. Architect

Owns major design decisions.

Responsibilities:

- Protect architecture direction.
- Check against existing project decisions.
- Prevent short-term hacks from damaging the system.
- Write/update architecture documents.

### 4. Builder

Writes or proposes code changes.

Responsibilities:

- Generate implementation patches.
- Use retrieved context.
- Respect code standards.
- Avoid touching unrelated files.
- Produce buildable, testable changes.

### 5. Tester

Runs behavioural and automated tests.

Responsibilities:

- Execute CLI test plans.
- Run unit/integration tests.
- Capture logs and output.
- Produce structured test reports.

### 6. Quality

Enforces standards.

Responsibilities:

- Run Roslyn analyzers.
- Run format checks.
- Run package/security checks where possible.
- Compare output against the IronDev code standards document.
- Flag code bloat, duplication, and drift.

### 7. Retriever

Owns context selection.

Responsibilities:

- Decide what project memory to search.
- Craft metadata-aware retrieval queries.
- Filter or rerank retrieved chunks.
- Package context for the Builder, Planner, or Architect.
- Record trace evidence.

### 8. Critic

Looks for deeper failure modes.

Responsibilities:

- Challenge assumptions.
- Detect objective hacking.
- Spot poor architecture tradeoffs.
- Ask whether the output solves the real problem.
- Identify hidden risks before they become expensive.

### Why Not More Agents Yet?

More than eight agents too early creates coordination overhead, cost, latency, and debugging pain.

The rule for now: add a new agent only when an existing agent has become too broad and there is a clear operational boundary.

## 7. Main Risks

### Cost

Thousands of iterations can burn money quickly.

Mitigations:

- Use cheap models for repetitive execution.
- Send only condensed reports to Codex.
- Stop early on obvious failures.
- Use deterministic tools where possible.
- Use Roslyn analyzers instead of LLMs for mechanical quality checks.

### Retrieval Drift

The wrong chunk can cause the wrong decision.

Mitigations:

- Strong metadata.
- Project and tenant filtering.
- Version-aware ranking.
- Stale penalties.
- Authority boosts.
- Semantic trace records.
- Dedicated retrieval regression tests.

### Objective Hacking

Agents may optimise for passing tests instead of improving the system.

Mitigations:

- Critic agent.
- Human approval gates.
- Test diversity.
- Behavioural tests, not just narrow assertions.
- Traceability to source intent.

### Base Model Ceiling

IronDev cannot be smarter than the models driving it.

Mitigations:

- Use stronger models only where they add value.
- Keep the system modular so better models can be swapped in later.
- Use deterministic tooling to reduce model burden.

### Code Quality Degradation

Repeated generation can create bloated, messy code.

Mitigations:

- Code standards document.
- Roslyn analyzers.
- Format checks.
- Build gates.
- Test gates.
- Review gates.
- Small scoped changes.

### Weighting Retrieved Information

Knowing which retrieved context matters most is still a hard problem.

Mitigations:

- Authority scoring.
- Source type scoring.
- Current/stale metadata.
- Explicit source links.
- Semantic traces.
- Human-reviewable context bundles.

## 8. Checkpoint Log As Of 2026-05-22

This section is a checkpoint log, not a permanent architecture rule. Newer accepted checkpoint documents may supersede these facts.

### Memory Spine 005

Proved:

- Weaviate health/reachability.
- Basic docs search through the Test Agent path.
- Authority-aware retrieval can answer a vague current-goal query.
- No obvious BookSeller primary bleed in the tested slice.

Boundary:

- Did not yet prove SQL-backed document versions.
- Did not yet prove full source document version flow.
- Did not yet prove real Weaviate vector retrieval over SQL-backed chunks.

### Memory Spine 006

Proved:

- SQL-backed ProjectDocument and ProjectDocumentVersion flow.
- Version 1/version 2 document testing.
- SQL semantic artefact/chunk indexing.
- Current version beats stale version through ranking logic.
- Semantic trace creation.

Boundary:

- Did not yet prove real Weaviate vector query over those SQL-backed chunks.

### Memory Spine 007

Proved:

- SQL ProjectDocumentVersion chunks can be written into a real Weaviate collection.
- Real nearVector query returns candidates.
- Raw Weaviate ranking may prefer stale content.
- IronDev final ranking can promote the current authoritative version above stale content.
- Source links and semantic traces are recorded.

### Memory Spine 008

Proved:

- Wrong-project memory can be rejected under real Weaviate retrieval.
- A raw BookSeller hit can be rejected when querying from IronDev context.
- Final authority ranking respects project identity over vector temptation.

### Code Standards 009 And 012

Proved:

- Code standards can run as a Test Agent quality gate.
- Build, focused tests, format, package audit, and code-shape checks are reportable.
- Large procedural files/methods can be allowed intentionally while still visible as debt.

Current rule:

- Code standards may say "pass with warnings," but those warnings must remain structured and explicit.

### Ticket Source-Link 010

Proved:

- A real SQL ProjectTicket created from a project document can preserve SourceDocumentVersionId.
- The linked ProjectDocumentVersion resolves back to the exact SQL source version.
- Orphan tickets with missing source links are detected as failures.

### Builder Context Source Memory 011

Proved:

- Builder context assembly includes the ticket's linked ProjectDocumentVersion.
- Source document metadata, source markdown/excerpt, tenant/project identity, and source link evidence are included.
- Orphan, missing-version, wrong-project, and historical-source controls fail or mark context cleanly.

Boundary:

- This proves context assembly only. It does not prove code generation or patch application.

### Memory Search CLI 013

Proved:

- Codex can query accepted IronDev project memory through CLI-accessible memory search.
- Dogfood knowledge documents can be indexed into a Weaviate-backed collection.
- Raw Weaviate vector ranking can be compared against IronDev final authority/currentness ranking.
- JSON output includes source IDs, raw rank/vector score, final rank/authority score, source links, excerpt, match reason, and semantic trace ID.

Important evidence:

- For `current Codex goals`, `CODEX_GOALS` was raw Weaviate rank 8 but final IronDev rank 1 after authority/currentness correction.

### Agent Model Profiles And Stubs 014

Proved:

- Agent model choices are configurable instead of hardcoded.
- Eight agent stubs exist: Supervisor, Planner, Architect, Builder, Tester, Quality, Retriever, Critic.
- TesterAgent is the first real execution path and can run existing Test Agent plans.

Current rule:

- All agent model profiles are OpenAI-only for now and configurable through settings.

### CLI And Dogfood Slices 015-019

Proved:

- 015: Cross-project memory proof can run through TesterAgent.
- 016: Builder proposal safety can be proven before file writes.
- 017: Failed Test Agent evidence can be packaged for Codex handoff.
- 018: Codex-facing memory search was extracted from Program.cs.
- 019: SQL/Weaviate/cross-project memory smoke commands were extracted from Program.cs.

Current CLI dogfood shape:

```text
Codex asks memory
        ↓
TesterAgent runs validation
        ↓
Builder preview proves no writes
        ↓
Failure package gives repair evidence
        ↓
Quality gate keeps debt visible
```

### BookSeller Controlled Fixture 020

Proved:

- BookSeller exists as a controlled non-IronDev dogfood fixture.
- BookSeller has accepted architecture, ticket, and test-plan memory documents.
- BookSeller has a simple BOOK-001 ticket fixture.
- Test Agent can run a BookSeller-specific plan through the CLI.
- Reports remain project-scoped to BookSeller.
- Memory search can reject IronDev/CODEX title fragments from BookSeller result sets.

Current rule:

- BookSeller is an explicit sample/test project, not a hidden CLI default.
- Commands should remain project-scoped through `--project BookSeller` or an explicitly selected current project.

### BookSeller Ticket Source-Link 021

Proved:

- The existing ticket source-link proof can run under `project = BookSeller`.
- The generated BookSeller ticket has SourceDocumentVersionId.
- The linked ProjectDocumentVersion resolves exactly.
- The orphan/missing-source control fails cleanly.
- The report carries BookSeller project identity.

Boundary:

- This does not build the BookSeller app.
- This does not prove builder preview.
- This does not apply patches or mutate the target BookSeller repository.

## 9. Practical Next Build Order

The next slices should stay narrow and evidence-first.

### 022: BookSeller Builder Preview Proof

Goal:

- Prove BookSeller builder preview for BOOK-001 is project-scoped, uses BookSeller memory, excludes IronDev/CODEX bleed, and performs no file writes.

Acceptance:

- Project identity is BookSeller.
- Source ticket is BOOK-001 or a BookSeller-scoped SQL ticket derived from it.
- Builder context includes BookSeller source memory.
- Preview/proposal reports files it would touch.
- No writes occur before approval.
- IronDev/CODEX documents are not accepted as BookSeller authority.

### 023: BookSeller Test-After-Preview Proof

Goal:

- Prove the test harness can run a BookSeller-scoped test plan after builder preview without applying code.

Acceptance:

- The run is project-scoped to BookSeller.
- The report states what was tested and what was not tested.
- No target project files are modified.

### 024: RetrieverAgent Real Path

Goal:

- Make RetrieverAgent the next real agent by wrapping the existing memory search path into structured context packages.

Acceptance:

- RetrieverAgent returns project-scoped context bundles.
- Raw/final ranking evidence remains visible.
- Source document/version IDs are included.
- Wrong-project and stale evidence are excluded or marked clearly.

### 025: Supervisor/Codex Loop Proof

Goal:

- Prove the first simple orchestration loop: Codex/Supervisor creates a test plan, TesterAgent executes it, IronDev records evidence, and Codex receives a repair-ready report.

Acceptance:

- Loop is traceable.
- Test Agent remains execution-only.
- Codex receives structured evidence rather than raw noise.
- No uncontrolled writes.

## 10. Agent Model Settings And Stub Strategy

IronDev should not hardcode model names directly inside agents.

Each agent should reference a configurable model profile instead.

Example:

```text
TesterAgent -> cheap-runner
ArchitectAgent -> strong-reasoner
BuilderAgent -> code-builder
CriticAgent -> strong-reviewer
```

The actual model behind each profile should be configurable through settings first, then later through SQL-backed project settings.

This allows IronDev to swap between cheaper models, stronger models, Codex-style models, local models, or future providers without changing agent code.

### Recommended Model Profiles

```json
{
  "ModelProfiles": {
    "cheap-runner": {
      "Provider": "OpenAI",
      "Model": "gpt-4o-mini",
      "Temperature": 0.1,
      "MaxOutputTokens": 1200
    },
    "standard-reasoner": {
      "Provider": "OpenAI",
      "Model": "configured-standard-model",
      "Temperature": 0.2,
      "MaxOutputTokens": 3000
    },
    "strong-reasoner": {
      "Provider": "OpenAI",
      "Model": "configured-strong-model",
      "Temperature": 0.2,
      "MaxOutputTokens": 5000
    },
    "code-builder": {
      "Provider": "OpenAI",
      "Model": "configured-code-model",
      "Temperature": 0.1,
      "MaxOutputTokens": 6000
    },
    "strong-reviewer": {
      "Provider": "OpenAI",
      "Model": "configured-review-model",
      "Temperature": 0.1,
      "MaxOutputTokens": 4000
    }
  }
}
```

### Recommended Agent Defaults

| Agent | Default model profile | Reason |
| --- | --- | --- |
| Supervisor / Cortex | strong-reasoner | Makes orchestration decisions |
| Planner | standard-reasoner | Turns vague goals into structured work |
| Architect | strong-reasoner | Owns technical direction |
| Builder | code-builder | Produces implementation changes |
| Tester | cheap-runner | Executes repetitive test plans |
| Quality | cheap-runner | Mostly deterministic/Roslyn-driven checks |
| Retriever | cheap-runner | Query shaping and metadata filtering |
| Critic | strong-reviewer | Expensive challenge/review gate |

The Architect and Critic should not run on every small loop. They should be used as important gates because they are more expensive.

### Recommended Agent Stub Contract

Create all eight agent stubs before each agent is intelligent.

The purpose is to give IronDev a stable orchestration skeleton.

```csharp
public interface IIronDevAgent
{
    string AgentName { get; }
    string Purpose { get; }
    string DefaultModelProfile { get; }
    IReadOnlyList<string> AllowedTools { get; }

    Task<AgentResult> RunAsync(AgentRequest request, CancellationToken ct = default);
}
```

Recommended first service contracts:

```text
IAgentRegistry
IAgentModelResolver
IAgentRunner
IIronDevAgent
```

Recommended first implementation slice:

```text
Supervisor/Codex creates a test plan
        ↓
TesterAgent runs CLI commands
        ↓
TesterAgent returns structured report
        ↓
Supervisor/Codex decides next action
```

Do not make all agents fully intelligent immediately. The right move is to create the stubs, settings, and model-profile resolution first, then make only the Tester path real.

## 11. Documentation Split Rule

This document is useful as an accepted architecture checkpoint, but it should not become a permanent catch-all.

When it grows again, split it into:

- `IRONDEV_SELF_IMPROVEMENT_ARCHITECTURE.md` for stable rules.
- `IRONDEV_SELF_IMPROVEMENT_ROADMAP.md` for future sequencing.
- `IRONDEV_SELF_IMPROVEMENT_CHECKPOINT_LOG.md` for completed proof slices.

Until that split happens, keep the distinction clear inside this document:

- Current Operating Rules are authoritative.
- Checkpoint Log is history.
- Practical Next Build Order is roadmap.

## 12. Blunt Assessment

The direction is good.

The biggest risk is not whether IronDev can call an LLM or generate code. That part is achievable.

The real risk is whether IronDev can reliably know what context matters, prove where that context came from, and avoid slowly corrupting its own understanding over many iterations.

That is why the Memory Spine work is the right foundation. It is not glamorous, but it decides whether the later self-improvement loop is credible or just another code-generation wrapper.

The next serious milestone remains:

> Codex creates a test plan -> Test Agent executes it through the CLI -> IronDev records evidence -> Codex analyses the report -> a traceable ticket or fix proposal is produced.

That is the first real self-dogfooding loop.
