---
id: 20260523090000001-research-and-sentinel-agents-future-discussion
project: IronDev
title: RESEARCH_AND_SENTINEL_AGENTS_FUTURE_DISCUSSION
document_type: FutureArchitectureDiscussion
authority: Draft
status: Proposed
dogfood_phase: PostDisposableWorkspace
source: C:\Users\bob\source\repos\AIDeveloper\Docs\RESEARCH_AND_SENTINEL_AGENTS_FUTURE_DISCUSSION.md
dogfood_run_id: FutureDiscussion-ResearchSentinelAgents
created_utc: 2026-05-23T09:00:00.0000000+00:00
primary_retrieval_questions:
  - What is the ResearchAgent?
  - What is the SentinelAgent?
  - When should IronDev build ResearchAgent and SentinelAgent?
  - How do external research and internal trace awareness fit into IronDev?
boundary: Not current implementation. Current active work remains Disposable Workspace Safety Contract 104.
---

# Research Agent And Sentinel Agent Future Discussion

## 1. Purpose

This document captures a future IronDev direction for two important agents:

- **ResearchAgent** - read-only external evidence gathering.
- **SentinelAgent** - proactive internal trace, health, debug, and pattern observation.

These agents are part of the longer-term IronDev "AI software organism" vision, but they are **not current implementation work**.

The current active phase remains:

```text
104: Disposable Workspace Safety Contract
```

The current rule remains:

```text
Build the cage first.
Then allow writing only inside the cage.
```

ResearchAgent and SentinelAgent should not jump ahead of the disposable workspace safety phase.

## 2. Current Build Order Reminder

The active roadmap is still:

```text
104: Disposable Workspace Safety Contract
105: Weighted Context Bundle Contract
106: RetrieverAgent emits WeightedContextBundle
107: Supervisor uses WeightedContext before builder preview
108: Create/reset disposable BookSeller workspace
109: Copy BookSeller fixture into disposable workspace
110: Apply patch only inside disposable workspace
111: Build/test disposable workspace
112: IDA code comparison review
113: Failure package from disposable apply/build/test
114: Human approval gate review
115: Controlled write path decision
```

ResearchAgent and SentinelAgent belong after the safety cage and context weighting are stable.

Recommended later placement:

```text
116: ResearchAgent read-only external evidence contract
117: ResearchAgent package weighted into context
118: SentinelAgent insight artefact contract
119: SentinelAgent reads traces/failures/test history
120+: SentinelAgent debug symbol / PDB / deeper runtime analysis
```

## 3. ResearchAgent

### 3.1 Core Idea

ResearchAgent is IronDev's **external awareness**.

It is a strictly read-only agent that gathers current external evidence from the web, documentation, or other external sources when explicitly requested.

It should answer questions like:

```text
What are current .NET patterns for inventory management?
What are current security recommendations for JWT in Blazor?
What are common BookSeller/e-commerce stock reservation patterns?
What does the latest official documentation say about a framework feature?
```

### 3.2 Role

ResearchAgent should:

- Search external sources.
- Collect evidence.
- Summarise findings.
- Capture source URLs, titles, dates, and credibility notes.
- Identify contradictions or uncertainty.
- Return a structured ResearchPackage.
- Feed the weighted context system as external evidence.

ResearchAgent should **not**:

- Make architecture decisions.
- Override accepted project memory.
- Apply patches.
- Edit code.
- Silently inject web content into Builder context.
- Treat web content as more authoritative than accepted IronDev project memory.

### 3.3 Rule

The weighting rule should be:

```text
Project memory = authority
ResearchAgent = evidence
Codex/human = judgement
IronDev/IDA = enforcement
```

External research can inform decisions, but accepted project memory remains authoritative unless explicitly changed through the proper decision/document process.

### 3.4 Proposed Data Contract

```csharp
public sealed record ResearchPackage(
    string Topic,
    IReadOnlyList<ResearchSource> Sources,
    IReadOnlyList<string> KeyFindings,
    IReadOnlyList<string> Conflicts,
    decimal ConfidenceScore,
    string RecommendationNote);

public sealed record ResearchSource(
    string Url,
    string Title,
    string? PublishedDate,
    string CredibilityTag,
    string Snippet);
```

The `RecommendationNote` should always include a reminder similar to:

```text
Project memory remains authoritative unless explicitly overridden by an accepted IronDev decision.
```

### 3.5 Example Research Package

```json
{
  "topic": "BookSeller inventory model patterns",
  "sources": [
    {
      "url": "https://example.com/dotnet-inventory-patterns",
      "title": "Inventory design patterns for .NET applications",
      "publishedDate": "2026-01-10",
      "credibilityTag": "Technical article",
      "snippet": "Separating catalogue data from inventory movement history improves auditability."
    }
  ],
  "keyFindings": [
    "Track catalogue identity separately from stock quantity.",
    "Keep inventory movement history if stock changes need auditing.",
    "Consider reservations separately from stock on hand if checkout exists."
  ],
  "conflicts": [],
  "confidenceScore": 0.72,
  "recommendationNote": "Use as external evidence only. Project memory remains authoritative unless explicitly overridden."
}
```

### 3.6 First Safe ResearchAgent Slice

A future first slice should be:

```text
116: ResearchAgent Read-Only Evidence Contract
```

Boundary:

- No code editing.
- No architecture changes.
- No patch proposals.
- No automatic context injection.
- Read-only evidence collection only.

## 4. SentinelAgent

### 4.1 Core Idea

SentinelAgent is IronDev's **internal awareness**.

It watches traces, logs, failures, test results, code standards output, debug symbols, and project health signals, then raises insight artefacts back to IDA/Supervisor.

This is the "watchful guardian" or "subconscious" part of the IronDev organism idea.

Recommended name:

```text
SentinelAgent
```

Alternative names:

- ReflectionAgent
- SubconsciousAgent
- InsightAgent
- HealthWatcher
- TraceObserver
- InnerVoiceAgent

SentinelAgent is preferred because it clearly means "watcher/guardian" without implying permission to make changes.

### 4.2 Role

SentinelAgent should:

- Analyse recent traces.
- Analyse build/test outcomes.
- Analyse failure packages.
- Detect repeated failure patterns.
- Detect project health trends.
- Watch for architectural drift.
- Review code standards history.
- Later inspect debug symbols, PDBs, stack traces, dumps, coverage reports, and performance traces.
- Create InsightArtefacts or ProactiveSignals.
- Feed insights into future weighted context bundles.

SentinelAgent should **not**:

- Apply fixes.
- Patch code.
- Block builds by itself.
- Rewrite tickets.
- Override Codex/human judgement.
- Mutate project source files.

### 4.3 Example Signals

SentinelAgent might surface insights like:

```text
I am seeing repeated null-handling failures around BookSeller inventory. This may indicate a missing domain rule.
```

```text
The last three BookSeller tickets touched inventory state but did not add negative-stock tests.
```

```text
Debug traces repeatedly point to InventoryReservationService.ReserveAsync. This area should be reviewed before more checkout work.
```

```text
Architectural drift concern: new components appear to access persistence directly instead of going through the repository/service boundary.
```

### 4.4 Debug Symbols / PDB Direction

The long-term SentinelAgent should eventually inspect debug symbols and runtime artefacts.

Possible future inputs:

- `.pdb` debug symbol files.
- Stack traces.
- Crash dumps.
- Test logs.
- Coverage reports.
- Performance traces.
- Allocation/hot-path information.
- Build warnings.
- Static analysis results.

Early debug-symbol scope should be modest:

- Read method names.
- Read namespaces/classes involved in stack traces.
- Link hot paths back to known project areas.
- Correlate repeated failures to files/symbols.

Do not start with complex dump analysis or full profiler interpretation.

### 4.5 Proposed Data Contract

```csharp
public sealed record InsightArtefact(
    string TraceId,
    string Project,
    string InsightType,
    string Title,
    string Description,
    string Severity,
    decimal Confidence,
    IReadOnlyList<string> RelatedTicketIds,
    IReadOnlyList<string> EvidenceRefs,
    string InnerMonologueText);
```

Example:

```json
{
  "traceId": "trace-20260523-001",
  "project": "BookSeller",
  "insightType": "RecurringFailurePattern",
  "title": "Repeated inventory validation failures",
  "description": "Recent BookSeller test failures repeatedly involve missing or weak inventory validation around stock quantity updates.",
  "severity": "Concern",
  "confidence": 0.68,
  "relatedTicketIds": ["BOOK-001"],
  "evidenceRefs": ["failure-package-BOOK-001-003", "test-report-BOOK-001-002"],
  "innerMonologueText": "I'm concerned we are treating inventory as a simple field when it may need stronger domain rules."
}
```

### 4.6 First Safe SentinelAgent Slice

A future first slice should be:

```text
118: SentinelAgent Insight Artefact Contract
```

Boundary:

- Observational only.
- No code changes.
- No patch proposals.
- No build blocking.
- No autonomous action.

## 5. How ResearchAgent And SentinelAgent Work Together

ResearchAgent gives IronDev external awareness.

SentinelAgent gives IronDev internal awareness.

Together they create a richer context loop:

```text
ResolveGoal
  ->
ResolveProject
  ->
ResolveTicket
  ->
RetrieveProjectMemory
  ->
ResearchAgent external evidence, if explicitly requested
  ->
SentinelAgent recent insight artefacts, if relevant
  ->
WeightContextBundle
  ->
Supervisor / IDA decides next action
```

The weighted context bundle must explain how each input was treated:

- Accepted project memory.
- External research evidence.
- Sentinel insight artefacts.
- Rejected or demoted context.
- Risk notes.

## 6. Interaction With Weighted Context

ResearchAgent and SentinelAgent outputs should not bypass weighting.

They should be treated as context candidates.

WeightedContextBundle should include their artefacts with source type labels:

```text
AcceptedProjectMemory
ExternalResearchEvidence
InternalSentinelInsight
FailurePackageEvidence
TestReportEvidence
```

Default weighting guidance:

```text
Accepted project memory > source-linked ticket memory > failure/test evidence > Sentinel insights > external research > broad historical notes
```

Exception:

- If the user explicitly asks for current external information, ResearchAgent evidence may be promoted, but it should still be labelled as external.
- If SentinelAgent sees repeated critical failures, its insight should be highly visible, but still advisory.

## 7. Why Not Build These Now?

These ideas are good, but they should not interrupt the current phase.

Current phase:

```text
Disposable Workspace Apply Proof
```

Current active ticket:

```text
104: Disposable Workspace Safety Contract
```

ResearchAgent and SentinelAgent depend on:

- Stable safety boundaries.
- Weighted context contract.
- Trace/evidence artefact storage.
- Disposable workspace proof results.
- Clear distinction between evidence and authority.

Building them before the safety cage risks adding more "intelligence-looking" behaviour before IronDev can safely control writes.

## 8. Proposed Future Roadmap Placement

Recommended later sequence:

```text
104: Disposable Workspace Safety Contract
105: Weighted Context Bundle Contract
106: RetrieverAgent emits WeightedContextBundle
107: Supervisor uses WeightedContext before builder preview
108: Create/reset disposable BookSeller workspace
109: Copy BookSeller fixture into disposable workspace
110: Apply patch only inside disposable workspace
111: Build/test disposable workspace
112: IDA code comparison review
113: Failure package from disposable apply/build/test
114: Human approval gate review
115: Controlled write path decision
116: ResearchAgent read-only external evidence contract
117: ResearchAgent package weighted into context
118: SentinelAgent insight artefact contract
119: SentinelAgent reads traces/failures/test history
120+: SentinelAgent debug symbol / PDB / deeper runtime analysis
```

## 9. Blunt Assessment

ResearchAgent and SentinelAgent are strong ideas.

They are not fluff.

ResearchAgent gives IronDev external awareness.

SentinelAgent gives IronDev internal awareness.

Together they support the longer-term "AI software organism" vision.

But they are future capability, not current implementation.

The current job is still:

```text
Build the disposable workspace cage first.
```

Do not lose the build order.

Do not let exciting future agents jump ahead of safety.
