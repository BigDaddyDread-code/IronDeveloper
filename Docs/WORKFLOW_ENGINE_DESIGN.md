# IronDev LangGraph-Style Workflow Design

## Purpose

This document captures the proposed **LangGraph-style state machine direction** for IronDev so it can be tracked, refined, and eventually turned into implementation tickets.

The goal is not to blindly adopt LangGraph as a dependency. The goal is to apply the useful pattern behind LangGraph:

> Every AI development workflow should be explicit, stateful, resumable, traceable, and safe to interrupt or approve.

For IronDev, this means moving from loose LLM calls into a proper workflow engine that can manage project memory, planning, build proposals, tests, failures, retries, and human approval gates.

---

## Core Decision

IronDev should implement a **C#/.NET LangGraph-style workflow engine** rather than immediately adopting LangGraph itself.

### Why

IronDev is a .NET-first product. The workflow engine needs to sit close to:

* Tickets
* Project documents
* Knowledge Compiler artefacts
* Code index / semantic index
* Build/test execution
* SQL trace storage
* WPF review screens
* Future API/cloud execution

Using the LangGraph idea is valuable. Pulling in LangGraph directly may be premature because it would likely introduce a Python/JavaScript orchestration layer before IronDev has finished its own .NET-native project memory and build loop.

---

## What "LangGraph-Style" Means for IronDev

A LangGraph-style flow means modelling AI work as a graph of named nodes.

Each node does one clear job.

Each node receives workflow state, performs work, updates state, and returns the next transition.

Example:

```text
Load Ticket
  ↓
Compile Knowledge Context
  ↓
Create Implementation Plan
  ↓
Human Approval
  ↓
Generate Code Proposal
  ↓
Apply Patch in Sandbox
  ↓
Run Build
  ↓
Run Tests
  ↓
Diagnose Failure or Complete
```

This turns AI-assisted development into a controlled process instead of a giant prompt.

---

## Why This Matters

Without a state machine, AI development tends to become:

```text
Prompt → LLM guesses → tool call → more guessing → final response
```

That is not good enough for IronDev.

IronDev needs:

* Durable workflow state
* Human approval points
* Clear retry rules
* Build/test feedback loops
* Traceability from source document to ticket to code change
* Ability to resume after failure
* Ability to explain why a decision or change happened

This is directly aligned with IronDev's goal of becoming a trustworthy AI development cockpit.

---

## Relationship to the Knowledge Compiler

The Knowledge Compiler should remain the brain of the system.

The state machine should not replace the Knowledge Compiler. It should use it.

The Knowledge Compiler provides:

* Project documents
* Versioned Markdown source material
* Decisions
* Requirements
* Risks
* Standards
* Architecture notes
* Source document version traceability
* Eventually semantic/vector retrieval
* Conflict detection

The workflow engine uses that compiled knowledge to plan, build, validate, and trace work.

---

## Recommended Build Order

The correct order is:

```text
1. Knowledge Compiler semantic memory
2. Conflict detection / authority-aware retrieval
3. Ticket build workflow state machine
4. Multi-agent orchestration
5. Autonomous build/test/fix loop
```

The reason is simple: agents without reliable memory become messy. Build the brain first, then build the agents that use it.

---

## Proposed Architecture

### Main Service

```csharp
public interface ITicketBuildWorkflowOrchestrator
{
    Task<TicketBuildWorkflowResult> StartAsync(
        TicketBuildWorkflowRequest request,
        CancellationToken cancellationToken = default);

    Task<TicketBuildWorkflowResult> ResumeAsync(
        Guid workflowRunId,
        CancellationToken cancellationToken = default);
}
```

This service owns the high-level workflow.

It should not contain every step inline. Each step should be a separate node.

---

### Workflow Node Interface

```csharp
public interface IWorkflowNode<TState>
{
    string Name { get; }

    Task<WorkflowNodeResult<TState>> ExecuteAsync(
        TState state,
        CancellationToken cancellationToken = default);
}
```

Each node takes the current state and returns the updated state plus the next transition.

---

### Workflow Node Result

```csharp
public sealed class WorkflowNodeResult<TState>
{
    public required TState State { get; init; }
    public required string NextNode { get; init; }
    public bool RequiresHumanApproval { get; init; }
    public bool IsTerminal { get; init; }
    public string? Message { get; init; }
}
```

---

### Ticket Build Workflow State

```csharp
public sealed class TicketBuildWorkflowState
{
    public Guid WorkflowRunId { get; set; }
    public int ProjectId { get; set; }
    public int TicketId { get; set; }

    public string CurrentNode { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";

    public int RetryCount { get; set; }
    public int MaxRetries { get; set; } = 3;
    public bool RequiresHumanApproval { get; set; }

    public int? SourceDocumentVersionId { get; set; }

    public string? TicketTitle { get; set; }
    public string? TicketDescription { get; set; }

    public string? KnowledgeContextMarkdown { get; set; }
    public string? ImplementationPlanMarkdown { get; set; }
    public string? GeneratedPatch { get; set; }
    public string? BuildOutput { get; set; }
    public string? TestOutput { get; set; }
    public string? FailureDiagnosisMarkdown { get; set; }
    public string? CompletionSummaryMarkdown { get; set; }

    public List<int> KnowledgeArtefactIds { get; set; } = [];
    public List<string> AffectedFiles { get; set; } = [];
    public List<string> TraceMessages { get; set; } = [];
}
```

---

## Proposed Workflow Nodes

### 1. LoadTicketNode

Loads the ticket and validates that it can enter the build workflow.

Responsibilities:
* Load ticket by ID
* Validate project ID
* Validate ticket status
* Load source document version link if present
* Record trace entry

Output: `Next: CompileKnowledgeContextNode`

---

### 2. CompileKnowledgeContextNode

Asks the Knowledge Compiler for the best context for this ticket.

Responsibilities:
* Retrieve linked source document version
* Retrieve related decisions
* Retrieve standards
* Retrieve architecture notes
* Retrieve related tickets
* Retrieve semantic code index summaries
* Apply authority-aware ranking
* Detect conflicts if possible

Output: `Next: CreateImplementationPlanNode`

---

### 3. CreateImplementationPlanNode

Creates a human-readable implementation plan.

Responsibilities:
* Use ticket + compiled knowledge context
* Produce a structured implementation plan
* Include affected areas
* Include risk notes
* Include test plan
* Include assumptions

Output: `Next: RequestPlanApprovalNode`

---

### 4. RequestPlanApprovalNode

Human-in-the-loop approval gate.

Responsibilities:
* Pause workflow
* Show implementation plan in UI
* Allow approve / reject / request changes
* Save approval decision to trace

Output:
* Approved → `GenerateCodeProposalNode`
* Rejected → `ReviseImplementationPlanNode`

---

### 5. ReviseImplementationPlanNode

Updates the implementation plan based on human feedback.

Output: `Next: RequestPlanApprovalNode`

---

### 6. GenerateCodeProposalNode

Generates a code change proposal.

Output: `Next: RequestCodeApprovalNode`

---

### 7. RequestCodeApprovalNode

Human review gate for the generated code proposal.

Output:
* Approved → `ApplyPatchNode`
* Rejected → `ReviseCodeProposalNode`

---

### 8. ApplyPatchNode

Applies the approved patch in a controlled workspace.

Output: `Next: RunBuildNode`

---

### 9. RunBuildNode

Runs the build.

Output:
* Success → `RunTestsNode`
* Failure → `DiagnoseFailureNode`

---

### 10. RunTestsNode

Runs tests.

Output:
* Success → `CreateCompletionTraceNode`
* Failure → `DiagnoseFailureNode`

---

### 11. DiagnoseFailureNode

Uses build/test output to diagnose the issue.

Output:
* Retry available → `GenerateFixProposalNode`
* Retry exceeded → `MarkWorkflowFailedNode`

---

### 12. GenerateFixProposalNode

Generates a proposed fix for the failed build/test.

Output: `Next: ApplyPatchNode`

---

### 13. CreateCompletionTraceNode

Creates the final trace and summary.

Output: `Next: CompleteTicketNode`

---

### 14. CompleteTicketNode

Completes the workflow. **Terminal.**

---

## Suggested Database Tables

### TicketBuildWorkflowRuns

* Id, WorkflowRunId, ProjectId, TicketId, SourceDocumentVersionId
* CurrentNode, Status, RetryCount, MaxRetries
* RequiresHumanApproval
* CreatedUtc, UpdatedUtc, CompletedUtc

### TicketBuildWorkflowStates

* Id, WorkflowRunId, NodeName, StateJson, CreatedUtc

### TicketBuildWorkflowEvents

* Id, WorkflowRunId, EventType, NodeName, Message, PayloadJson, CreatedUtc

### TicketBuildWorkflowApprovals

* Id, WorkflowRunId, ApprovalType, RequestedAtUtc, ResolvedAtUtc
* Status, ReviewerUserId, ReviewerComment

---

## First Implementation Slice

```text
Ticket → Compile Context → Generate Implementation Plan → Human Approval → Stop
```

This proves the workflow model without risking uncontrolled code writes.

---

## Risks

1. **Building Agents Too Early** — agents without reliable memory become messy.
2. **Too Much Automation Too Soon** — start proposal-first and approval-first.
3. **Giant Prompt Problem Returns** — use structured state, artefact IDs, controlled retrieval.
4. **Weak Persistence** — persist workflow state from the beginning.

---

## Open Questions

1. Should workflow runs be tied directly to tickets, or allow document-driven workflows before a ticket exists?
2. Should the first workflow be ticket-first or discussion-document-first?
3. Should plan approval be required every time, or configurable per project?
4. Should build/test execution happen locally first, then later move to API/worker execution?
5. Should workflow state be stored as JSON only, or partially normalized for reporting?
6. Should human feedback during approval be saved as a project memory artefact?

---

## Recommended Immediate Next Step

Create branch `feature/ticket-build-workflow-state-machine` after the current Knowledge Compiler semantic memory direction is completed or deliberately paused.

The first implementation should stop at implementation-plan approval.

---

*One-Line Summary:* IronDev should use a LangGraph-style C# workflow engine to turn AI development into a durable, traceable, approval-gated process — built on top of the Knowledge Compiler's compiled memory.
