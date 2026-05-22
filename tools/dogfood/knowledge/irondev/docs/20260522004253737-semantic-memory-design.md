---
id: 20260522004253737-semantic-memory-design
project: IronDev
title: SEMANTIC_MEMORY_DESIGN
document_type: Architecture
authority: Accepted
source: C:\Users\bob\source\repos\AIDeveloper\Docs\SEMANTIC_MEMORY_DESIGN.md
dogfood_run_id: DogfoodDocsSeed-20260522-012
created_utc: 2026-05-22T00:42:53.7501851+00:00
---

# IronDev Knowledge Compiler Semantic Memory Design

## Purpose

This document captures the next major IronDev leap after Alpha 0.1:

> Add semantic vector memory to the Knowledge Compiler so IronDev can retrieve the right project knowledge automatically instead of relying on giant prompts or manual context selection.

This is the bridge between the current Knowledge Compiler and the later LangGraph-style build workflow / multi-agent orchestration loop.

The goal is to turn the Knowledge Compiler from a typed project-memory store into an intelligent retrieval engine that can power chat, ticket creation, planning, conflict detection, and eventually autonomous build/test/fix workflows.

---

## Core Decision

The next major branch should be:

```text
feature/knowledge-compiler-semantic-memory
```

This should come before the multi-agent orchestration work.

## Why This Comes First

Agents without reliable memory will drift.

Before IronDev adds Planner Agent, Builder Agent, Validator Agent, or LangGraph-style orchestration, the Knowledge Compiler needs to become the trusted brain that provides:

* relevant decisions
* project standards
* architecture notes
* source discussion documents
* source document versions
* ticket context
* code index summaries
* conflict warnings
* authority-ranked knowledge

Semantic memory makes this possible.

---

## Current State

Alpha 0.1 gives IronDev a strong foundation:

* typed context documents
* authority levels
* provenance/source references
* manual editor flow
* discussion seeding
* discussion resolving
* artefact application
* project-memory spine

Relevant existing areas:

* `DiscussionSeedService`
* `DiscussionResolverService`
* `KnowledgeArtefactApplyService`
* `ContextConflictService`
* typed context documents
* authority-aware project memory

This means the hard foundation already exists. Semantic memory activates it.

---

## Target Outcome

After this feature, IronDev should be able to answer:

> "What knowledge matters for this request?"

And return the best relevant documents automatically.

For example, when generating a ticket, IronDev should automatically retrieve:

* the source discussion document version
* related architecture decisions
* relevant standards
* similar previous tickets
* relevant code summaries
* potential conflicts
* high-authority facts that constrain the implementation

This is the move from passive memory to active project intelligence.

---

## Dogfooding Readiness Impact

| Milestone                         | Dogfooding Capability                                      | Readiness |
| --------------------------------- | ---------------------------------------------------------- | --------: |
| After merging Alpha 0.1           | Manual seeding + basic project context                     |      ~40% |
| After semantic memory             | Automatic relevant context pull for every ticket/chat/plan |      ~75% |
| After first-slice workflow        | Ticket → semantic context → structured plan → approval     |      ~90% |
| After full workflow + build nodes | End-to-end self-improvement with safety gates              |      ~98% |

---

## Phase 1 — Core Models

### VectorEmbedding

```csharp
public sealed record VectorEmbedding
{
    public Guid Id { get; init; }
    public Guid ArtefactId { get; init; }
    public string ArtefactType { get; init; } = string.Empty;
    public int ProjectId { get; init; }
    public int? SourceDocumentVersionId { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public float[] Vector { get; init; } = [];
    public DateTime EmbeddedAtUtc { get; init; }
    public string ModelVersion { get; init; } = string.Empty;
}
```

### SemanticSearchResult

```csharp
public sealed record SemanticSearchResult
{
    public required ContextDocument Document { get; init; }
    public double Similarity { get; init; }
    public string MatchReason { get; init; } = string.Empty;
    public string AuthorityLevel { get; init; } = string.Empty;
    public int? SourceDocumentVersionId { get; init; }
}
```

### SemanticMemoryHealth

```csharp
public sealed record SemanticMemoryHealth
{
    public int ProjectId { get; init; }
    public int DocumentCount { get; init; }
    public int EmbeddedCount { get; init; }
    public int StaleEmbeddingCount { get; init; }
    public DateTime? LastEmbeddedAtUtc { get; init; }
    public DateTime? LastRebuildAtUtc { get; init; }
}
```

---

## Phase 2 — Service Interface

```csharp
public interface ISemanticMemoryService
{
    Task EmbedAndStoreAsync(ContextDocument document, CancellationToken ct = default);
    Task<IReadOnlyList<SemanticSearchResult>> SearchAsync(int projectId, string query, int limit = 8, double minSimilarity = 0.75, CancellationToken ct = default);
    Task RebuildIndexAsync(int projectId, IProgress<SemanticIndexRebuildProgress>? progress = null, CancellationToken ct = default);
    Task DeleteEmbeddingAsync(Guid artefactId, CancellationToken ct = default);
    Task<SemanticMemoryHealth> GetHealthAsync(int projectId, CancellationToken ct = default);
}
```

---

## Phase 3 — Storage Strategy

Local-first SQL-backed semantic memory. Provider-agnostic abstraction.

Possible implementations:

```text
InMemorySemanticMemoryService        // tests/dev fallback
SqliteVecSemanticMemoryService       // local-first desktop index
SqlServerSemanticMemoryService       // simple enterprise/on-prem option
WeaviateSemanticMemoryService        // cloud/team option
QdrantSemanticMemoryService          // cloud/team option
```

---

## Phase 4 — Embedding Provider

```csharp
public interface IEmbeddingProvider
{
    Task<EmbeddingResult> EmbedAsync(string input, CancellationToken ct = default);
}
```

```csharp
public sealed record EmbeddingResult
{
    public required float[] Vector { get; init; }
    public required string Model { get; init; }
    public int Dimensions => Vector.Length;
}
```

---

## Phase 4b — Embedding Content Extraction

```csharp
public interface IEmbeddingContentExtractor
{
    string Extract(ContextDocument document);
}
```

---

## Phase 4c — Semantic Ranking Options

```csharp
public sealed record SemanticRankingOptions
{
    public double SimilarityWeight { get; init; } = 0.60;
    public double AuthorityWeight { get; init; } = 0.25;
    public double FreshnessWeight { get; init; } = 0.10;
    public double DirectLinkWeight { get; init; } = 0.05;
    public double MinimumSimilarity { get; init; } = 0.75;
}
```

---

## Phase 4d — Semantic Retrieval Trace

```csharp
public sealed record SemanticRetrievalTrace
{
    public Guid Id { get; init; }
    public int ProjectId { get; init; }
    public string Query { get; init; } = string.Empty;
    public IReadOnlyList<SemanticSearchResult> Results { get; init; } = [];
    public string CallerContext { get; init; } = string.Empty;
    public DateTime QueriedAtUtc { get; init; }
}
```

---

## Phase 5 — Knowledge Compiler Integration

Wire semantic memory into:

* **DiscussionSeedService** — embed on artefact creation
* **DiscussionResolverService** — semantic search for context retrieval
* **KnowledgeArtefactApplyService** — conflict candidate detection
* **ContextConflictService** — semantic conflict candidate retrieval

---

## Database Tables

### SemanticEmbeddings

Id, ProjectId, ArtefactId, ArtefactType, SourceDocumentVersionId, ContentHash, ModelVersion, VectorDimensions, VectorData, EmbeddedAtUtc, CreatedUtc, UpdatedUtc

### SemanticIndexRuns

Id, ProjectId, StartedAtUtc, CompletedAtUtc, Status, TotalDocuments, ProcessedDocuments, ErrorMessage

### SemanticSearchTraces

Id, ProjectId, QueryText, ResultCount, MinSimilarity, CreatedUtc, Caller, TraceJson

---

## Hybrid Prem + Cloud Direction

Source of truth rule:

```text
SQL Server / central project store = source of truth
SQLite-Vec / local vector store = rebuildable retrieval index
Cloud vector store = shared retrieval index
Knowledge Compiler = authority-aware project brain
```

---

## First Implementation Slice

```text
Embed context documents → search them → show results → use search in DiscussionResolverService
```

---

## Risks

1. **Choosing Vector DB Too Early** — mitigate with clean abstractions
2. **Poor Embedding Hygiene** — mitigate with content hashes and stale counts
3. **Similarity Without Authority** — mitigate with explicit ranking weights
4. **Hidden Retrieval Behavior** — mitigate with retrieval trace UI
5. **Embedding Cost / API Failures** — mitigate with local provider and fail-soft

---

## Recommended Immediate Plan

1. Merge and tag Alpha 0.1 (`v0.1.0-alpha.1`)
2. Create branch `feature/knowledge-compiler-semantic-memory`
3. Add core models and interfaces
4. Add fake and in-memory implementations
5. Wire semantic search into `DiscussionResolverService`
6. Add Memory Health UI
7. Then move into conflict detection and multi-agent orchestration
