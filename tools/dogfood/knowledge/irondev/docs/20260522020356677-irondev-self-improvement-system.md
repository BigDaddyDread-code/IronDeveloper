---
id: 20260522020356677-irondev-self-improvement-system
project: IronDev
title: IRONDEV_SELF_IMPROVEMENT_SYSTEM
document_type: Architecture
authority: Accepted
source: C:\Users\bob\source\repos\AIDeveloper\Docs\IRONDEV_SELF_IMPROVEMENT_SYSTEM.md
dogfood_run_id: DogfoodDocsSeed-20260522-SelfImprovement
created_utc: 2026-05-22T02:03:56.6914667+00:00
---

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

## 2. Core Architecture

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

The main risk is retrieval drift: Weaviate may return the wrong project, stale version, or semantically similar but contextually wrong document. IronDev must compensate with metadata filtering, authority scoring, stale penalties, and semantic trace evidence.

### Native C# LangGraph-Style Workflow

IronDev currently uses a native C# LangGraph-style workflow rather than depending directly on external LangGraph.

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

## 3. Why IronDev Needs a CLI

The CLI is the programmable backend interface for dogfooding and automated testing.

Manual testing will not scale to the thousands of iterations needed to harden IronDev. The CLI gives Codex, test agents, scripts, and future automation a stable surface to drive the system without relying on the WPF UI.

The CLI should be built in C# and runnable from command line or PowerShell.

Initial examples:

```bash
irondev project create
irondev project list
irondev document add
irondev document version
irondev ticket add
irondev ticket list
irondev build run
irondev test run
irondev test drive
irondev memory search
irondev memory sql-version-smoke
irondev memory weaviate-version-smoke
```

No UI is required at first. The first priority is a clean, scriptable backend surface.

## 4. Testing Strategy

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

A cheaper model handles repetitive grunt work.

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

The early testing focus should be retrieval quality, not full autonomous coding.

## 5. First Major Testing Target: Retrieval Quality

Before IronDev can safely build from memory, it must prove it retrieves the right memory.

Important retrieval tests:

1. Create multiple similar projects.
2. Add similar documents across projects.
3. Add multiple versions of the same document.
4. Mark old versions stale and current versions authoritative.
5. Ask vague questions such as "what is the current goal?"
6. Confirm the correct project, document type, and current version wins.
7. Confirm stale or wrong-project chunks are either filtered out or demoted.
8. Record semantic trace evidence showing why the winning chunk won.

This is the right first self-dogfooding target because bad retrieval will poison every later builder, tester, and planning flow.

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

## 8. Current State

The original summary was directionally correct but is now slightly behind the current IronDev state.

Important current facts:

- IronDev has moved beyond pure discussion.
- The native C# LangGraph-style build workflow exists through the approval pause stage.
- The Roslyn-backed semantic layer exists.
- Weaviate has been accepted as the memory/retrieval direction.
- The Memory Spine proof slices have begun.

### Memory Spine 005

Proved:

- Weaviate health/reachability.
- Basic docs search through the Test Agent path.
- Authority-aware retrieval can answer a vague current-goal query.
- No obvious BookSeller primary bleed in the tested slice.

Did not yet prove:

- SQL-backed document versions.
- Full source document version flow.
- Real Weaviate vector retrieval over SQL-backed chunks.

### Memory Spine 006

Proved:

- SQL-backed ProjectDocument and ProjectDocumentVersion flow.
- Version 1/version 2 document testing.
- SQL semantic artefact/chunk indexing.
- Current version beats stale version through ranking logic.
- Semantic trace creation.

Did not yet prove:

- Real Weaviate vector query over those SQL-backed chunks.

### Memory Spine 007

Proved:

- SQL ProjectDocumentVersion chunks can be written into a real Weaviate collection.
- Real nearVector query returns candidates.
- Raw Weaviate ranking may prefer stale content.
- IronDev final ranking can promote the current authoritative version above stale content.
- Source links and semantic traces are recorded.

### Memory Search CLI 013

Implemented as a Codex-facing memory search command on draft PR #5.

Command:

```bash
memory search "<query>" --project IronDev --json
```

What it proves:

- IronDev now has a CLI-accessible memory search surface for Codex and the Test Agent.
- Dogfood knowledge documents can be indexed into a Weaviate-backed collection.
- Raw Weaviate vector ranking can be compared against IronDev's final authority/currentness ranking.
- JSON output includes source IDs, raw Weaviate rank/vector score, final IronDev rank/authority score, source links, excerpt, match reason, and semantic trace ID.
- Test Agent can call the memory search command through the `memory_search` action.

Validated smoke cases:

- `current Codex goals` -> `CODEX_GOALS`
- `current test agent rules` -> `TEST_AGENT_SPEC`
- `code standards large method allowlist` -> `CODE_STANDARDS`
- `builder approval before code changes` -> `CODEX_GOALS`

Additional validation:

- 012 code standards gate passed.
- 005 memory spine smoke still passed.
- Runner build passed.

Important evidence:

- For `current Codex goals`, `CODEX_GOALS` was raw Weaviate rank 8 but final IronDev rank 1 after authority/currentness correction.

This is exactly the behaviour IronDev needs: raw semantic search alone is not trusted; final ranking must apply project authority, currentness, and source intent.

Next clean ribs:

- 014: configurable agent model profiles and initial agent stubs.
- 008-style cross-project bleed protection under real Weaviate retrieval.

## 9. Practical Next Build Order

### Step 1: CLI Foundation

Build a clean CLI surface that can run against IronDev itself.

Minimum commands:

```bash
irondev health
irondev project list
irondev project current
irondev document add
irondev document list
irondev ticket add
irondev ticket list
irondev memory search
irondev memory sql-version-smoke
irondev memory weaviate-version-smoke
irondev test run
```

### Step 2: Test Agent Execution Contract

Define the handoff format between Codex and the Test Agent.

The Test Agent should accept:

- Goal
- Scope
- Preconditions
- Commands to run
- Expected checks
- Evidence to collect
- Stop conditions

It should return:

- Pass/fail status
- Command results
- Key logs
- Coverage summary if relevant
- Evidence paths
- Concise explanation
- Suggested next action

### Step 3: Memory Spine 008

Prove cross-project isolation under real Weaviate retrieval.

This is the most valuable next target because it attacks the highest-risk memory failure: IronDev confidently using the wrong project's context.

### Step 4: Codex/Test-Agent Loop

Let Codex generate a test plan, pass it to the Test Agent, receive a condensed report, and decide the next step.

At this point, Codex is no longer just reading the repo. It is starting to drive IronDev through a repeatable testing interface.

### Step 5: Expand Into Builder Dogfooding

After retrieval is trustworthy, begin feeding selected IronDev tickets through the builder workflow.

The first targets should be small, testable, and strongly traceable.

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
      "Model": "cheap-model-here",
      "Temperature": 0.1,
      "MaxOutputTokens": 1200
    },
    "standard-reasoner": {
      "Provider": "OpenAI",
      "Model": "standard-model-here",
      "Temperature": 0.2,
      "MaxOutputTokens": 3000
    },
    "strong-reasoner": {
      "Provider": "OpenAI",
      "Model": "strong-model-here",
      "Temperature": 0.2,
      "MaxOutputTokens": 5000
    },
    "code-builder": {
      "Provider": "OpenAI",
      "Model": "code-model-here",
      "Temperature": 0.1,
      "MaxOutputTokens": 6000
    },
    "strong-reviewer": {
      "Provider": "OpenAI",
      "Model": "strong-review-model-here",
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

Create all eight agent stubs now, even before each agent is intelligent.

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

Initial supporting models:

```csharp
public sealed class AgentDefinition
{
    public required string Name { get; init; }
    public required string Purpose { get; init; }
    public required string DefaultModelProfile { get; init; }
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<string> AllowedTools { get; init; } = [];
}

public sealed class ModelProfile
{
    public required string Name { get; init; }
    public required string Provider { get; init; }
    public required string Model { get; init; }
    public double Temperature { get; init; } = 0.2;
    public int MaxOutputTokens { get; init; } = 2000;
    public decimal? MaxCostPerRun { get; init; }
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

## 11. Blunt Assessment

The direction is good.

The biggest risk is not whether IronDev can call an LLM or generate code. That part is achievable.

The real risk is whether IronDev can reliably know what context matters, prove where that context came from, and avoid slowly corrupting its own understanding over many iterations.

That is why the current Memory Spine work is the right move. It is not glamorous, but it is the foundation that decides whether the later self-improvement loop is credible or just another code-generation wrapper.

The CLI should come next because it gives Codex and future agents a stable way to drive IronDev repeatedly without relying on the UI.

The agent/model-profile layer should be added early because it prevents hardcoded model choices and gives IronDev cost control from day one.

The next serious milestone should be:

> Codex creates a test plan -> Test Agent executes it through the CLI -> IronDev records evidence -> Codex analyses the report -> a traceable ticket or fix proposal is produced.

That is the first real self-dogfooding loop.