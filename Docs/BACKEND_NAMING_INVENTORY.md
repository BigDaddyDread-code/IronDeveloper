# Backend Naming Inventory

PR-48 normalises backend vocabulary before the backend contract freeze. This inventory is part of the contract surface: it records what changed, what intentionally did not change, and why no behavior change is intended.

## Boundary statements

- No behavior change intended.
- No SQL/API/CLI/UI/runtime/persistence/capability changes.
- SQL remains the source of truth for governed memory and audit records.
- Vector, index, and retrieval surfaces remain lookup-only and do not grant authority.
- Proposal is not apply.
- Candidate is not accepted memory.
- Audit is not approval.
- Gate is not executor.
- Critic is not governance.
- Memory safety classification is not approval.
- Tool request is a request form, not execution permission.
- Model output remains advisory only.
- Human review remains required for source apply and memory promotion.

## Naming changes in this PR

| Current name | New name | Reason | Files affected | Surface | Behavior unchanged |
| --- | --- | --- | --- | --- | --- |
| `CollectiveMemoryRetrievalCandidate` | `CollectiveMemoryRetrievalMatch` | Retrieval output was named `Candidate`, which collided with candidate memory. Retrieval returns ranked matches; it does not create or promote candidate memory. | `IronDev.Core/AgentMemory/Collective/CollectiveMemoryRetrievalModels.cs`, `IronDev.Infrastructure/AgentMemory/SqlCollectiveMemoryRetrievalService.cs`, `IronDev.IntegrationTests/AgentMemory/CollectiveMemoryRetrievalBoundaryTests.cs` | Core contract/internal backend use | Yes |
| `RetrievalCandidateId` | `RetrievalMatchId` | Keeps the output identifier aligned with retrieval-match vocabulary. | Same as above | Core contract/internal backend use | Yes |
| `CollectiveMemoryRetrievalResult.Candidates` | `CollectiveMemoryRetrievalResult.Matches` | Makes the candidate/memory boundary explicit: retrieval returns matches, not memory candidates. | Same as above | Core contract/internal backend use | Yes |

## Intentionally unchanged terms

| Term | Reason kept | Files / area | Surface | Boundary confirmation |
| --- | --- | --- | --- | --- |
| `CollectiveMemoryRetrievalAuthorityFilter.IncludeCandidates` | This filter means candidate-authority memory rows may be included in retrieval. The word candidate is accurate here because it refers to the memory authority level, not the retrieval row shape. | `IronDev.Core/AgentMemory/Collective/CollectiveMemoryRetrievalModels.cs` | Core contract | Candidate memory remains non-authoritative and cannot promote itself. |
| `AgentToolExecutionGate` | The gate evaluates whether a future executor may proceed; it explicitly does not execute. Existing name is acceptable because `Gate` is part of the boundary vocabulary. | `IronDev.Core/Agents/AgentToolExecutionGateModels.cs` | Core contract | Gate does not execute tools. |
| `AgentToolExecutionGateDecision.GrantsExecution` | The decision records future executor eligibility, not direct execution. Renaming would ripple into already-reviewed gate tests; inventory documents the boundary instead. | `IronDev.Core/Agents/AgentToolExecutionGateModels.cs` | Core contract | `ExecutesTool` remains false; gate is not executor. |
| `AgentRunSafetySummaryDto` / `SafetySummary` | Read API projection already exposes this DTO. It means unsafe-claim summary, not approval. Renaming is deferred to the API contract phase to avoid accidental API-shape churn in a backend naming PR. | `IronDev.Core/Agents/Audit/AgentRunAuditQueryDtos.cs` | Read API DTO | Audit summary does not approve or grant authority. |
| `ManualImplementationAgentPatchProposalService` | Name clearly says patch proposal. It does not apply patches or mutate source. | `IronDev.Core/Agents/Concrete/ManualImplementationAgentPatchProposalService.cs` | Core service | Proposal is not apply. |
| `ManualTestFailureRepairProposalLoopService` | Name clearly says repair proposal loop. It does not rerun tests, apply patches, or mutate source. | `IronDev.Core/Agents/Concrete/ManualTestFailureRepairProposalLoopService.cs` | Core service | Repair proposal is not repair execution. |
| `ManualTicketReviewFixProposalLoopService` | Name clearly says review and fix proposal. It does not apply fixes or submit GitHub reviews. | `IronDev.Core/Agents/Concrete/ManualTicketReviewFixProposalLoopService.cs` | Core service | Fix proposal is not source apply. |
| `MemoryImprovementProposal*` | Proposal wording is correct: these are reviewable proposed memory changes, not accepted memory or collective-memory promotion. | `IronDev.Core/AgentMemory/MemoryImprovementProposalModels.cs` | Core contract | Proposal cannot promote memory. |
| `MemoryGovernance*` | Existing governance wording refers to memory-use policy checks, not source/tool approval. Current tests enforce memory is not approval. | `IronDev.Core/AgentMemory/MemoryGovernanceModels.cs` | Core contract | Memory governance cannot grant source mutation or external-effect approval. |
| `ToolExecutionAudit*` | Audit records evidence of a tool execution result. It is not the executor and does not approve execution. | `IronDev.Core/Agents/ToolExecutionAuditModels.cs`, `IronDev.Infrastructure/ToolExecutionAudit/*` | Core/infrastructure | Audit is not approval and not executor. |
| `CriticReviewResult` | Critic output is a review result. Tests require review-only warnings and reject approval/governance claims. | `IronDev.Core/Agents/Concrete/IndependentCriticAgentModels.cs` | Core contract | Critic is not governance. |
| `Workspace apply-*` commands/models | `apply` is reserved for actual apply path models. Existing dry-run/copy/verify names were already split by operation. | `IronDev.Core/Workspaces/*`, `IronDev.Infrastructure/Services/Workspaces/*` | Workspace backend | Proposal/package/preflight/dry-run remain distinct from apply-copy. |

## Contract vocabulary by area

### Agents

| Preferred term | Meaning | Non-authority boundary |
| --- | --- | --- |
| `CriticReviewResult` | Advisory review output from boxed critic flows. | Does not govern, approve, block, mutate, or submit review. |
| `MemoryImprovementDetectionResult` | Proposal-only detection output from memory-improvement flows. | Does not persist proposals, create collective memory, or promote memory. |
| `AgentToolRequest` | Typed request form for a possible tool operation. | Does not grant execution permission and cannot contain execution result. |
| `AgentToolExecutionGate` | Deterministic eligibility gate for a possible future executor. | Does not execute tools. |
| `AgentRunAuditEnvelope` | Durable evidence envelope for manual agent runs. | Does not approve or create authority. |
| `ToolExecutionAuditRecord` | Durable evidence record for supported manual tool execution results. | Does not execute tools or grant approval. |

### Memory

| Preferred term | Meaning | Non-authority boundary |
| --- | --- | --- |
| `AgentLocalMemoryItem` | Scoped append-only local memory item for one agent/scope. | Cannot be read cross-agent through silo. |
| `CandidatePattern` | Local memory type for a possible pattern with limitations and evidence. | Candidate pattern cannot justify high-impact action alone. |
| `MemoryImprovementProposal` | Manual/governance-facing proposal for future memory improvement. | Accepted-for-future-implementation is not memory promotion. |
| `CollectiveMemoryEvidenceAggregate` | Deterministic support/contradiction aggregation. | Highest readiness is human review, not acceptance. |
| `CollectiveMemoryRetrievalMatch` | Ranked retrieval result item. | Retrieval is lookup-only and not authority. |
| `CollectiveMemoryPromotion` | Explicit governed promotion operation. | Separate from proposal, retrieval, safety, and audit. |

### Proposal / apply

| Preferred term | Meaning | Non-authority boundary |
| --- | --- | --- |
| `PatchProposal` / `PatchProposalRequest` | Proposed patch content. | Does not mutate source. |
| `apply-preflight` | Evidence gate before apply planning. | Does not apply. |
| `apply-dry-run` | Planned operation package. | Does not apply. |
| `apply-copy` | Controlled copy-only add/modify apply path. | Only approved add/modify copy path may mutate source. |
| `apply-verify` | Proof-after-mutation verification. | Does not mutate source. |

## Test and fixture naming guidance

- Use `proposal_does_not_apply` style names when the invariant is proposal/apply separation.
- Use `candidate_does_not_promote` for candidate/memory separation.
- Use `audit_does_not_approve` for audit/approval separation.
- Use `critic_does_not_govern` for critic/governance separation.
- Use `gate_does_not_execute` for gate/executor separation.
- Use `retrieval_match_not_authority` for vector/index/retrieval boundaries.

## Reviewer checklist

- Renamed terms preserve behavior.
- No SQL migrations or stored procedure changes were added.
- No service registration was added.
- No API, CLI, UI, runtime, or persistence behavior changed.
- Retrieval output now uses `Match` vocabulary to avoid candidate-memory confusion.
- Intentionally unchanged names have explicit boundary reasons above.
