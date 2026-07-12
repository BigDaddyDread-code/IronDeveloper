# Canonical Architecture Index

**Status:** Current architecture document authority map

**Last reviewed:** 12 July 2026

**Programme slice:** CLN-03

## Purpose

This index identifies which IronDev documents define current architecture and which documents remain useful only as supporting detail, history, superseded design, or future discussion.

The index does not make prose more authoritative than the running product. When documents disagree, use this order:

1. Executable runtime behavior, persisted contracts, migrations, and tests prove what the system does.
2. Accepted architecture decisions define the boundary that implementation must preserve.
3. Canonical current architecture and product documents describe the intended present system.
4. Supporting documents explain one bounded implementation or invariant.
5. Historical, superseded, and parking-lot documents do not define current behavior.

Receipts prove that a result was recorded at a point in time. They are historical evidence, not current architecture authority, and are outside the competing-document classification in this slice.

## Classification Vocabulary

| Classification | Meaning |
| --- | --- |
| `Canonical` | Current decision or system-shape authority for its named domain. |
| `Supporting` | Current bounded detail that must not override a canonical document. |
| `Historical` | Accurate evidence or design context for an earlier implementation stage. |
| `Superseded` | Replaced as a current contract by a named canonical document. |
| `ParkingLot` | Future discussion with no current implementation authority. |
| `DeleteCandidate` | Potentially redundant current prose requiring separate proof before deletion. This label is not permission to delete. |

## Canonical Domain Map

| Domain | Canonical documents | Supporting detail | Runtime owner and current boundary |
| --- | --- | --- | --- |
| Authority model | [ADR-004](../ADR/ADR-004-proposal-review-apply-boundary.md), [ADR-005](../ADR/ADR-005-tool-request-audit-execution-boundary.md), [ADR-006](../ADR/ADR-006-critic-gate-governance-boundary.md), [ADR-007](../ADR/ADR-007-human-review-required-for-apply-and-promotion.md), [ADR-018](../ADR_018_PASSIVE_AGENT_CONTAINMENT.md), [Agents](../AGENTS.md) | [Governance substrate invariants](../BLOCK_G_GOVERNANCE_SUBSTRATE_INVARIANTS.md), [Agent capability matrix](../AGENT_CAPABILITY_MATRIX.md) | Backend policy, governed-action, approval, and agent-runner services own authority. Evidence, model output, audit, frontend state, and green CI do not approve. |
| Tenant and project boundaries | [Architecture](../ARCHITECTURE.md), [ADR-015](../decisions/ADR-015-client-shell-strategy.md) | [Cockpit backend contract](../ALPHA_COCKPIT_BACKEND_CONTRACT.md), [API/client/CLI boundary findings](API_CLIENT_CLI_BOUNDARY_FINDINGS.md) | `IronDev.Api` is the product boundary. Tenant and route project scope are backend truth; shells do not own or infer scope. |
| Workflow and run spine | [Architecture](../ARCHITECTURE.md), [Current product capabilities](../product/CURRENT_PRODUCT_CAPABILITIES.md) | [Durable workflow run substrate](../BLOCK_J_DURABLE_WORKFLOW_RUN_SUBSTRATE.md), [H12 read projection rebuild story](H12_READ_PROJECTION_BACKUP_REBUILD_STORY.md) | SQL-backed workflow run, step, transition, evidence, and read-projection services own durable run state. A projection is rebuildable and is not authority. |
| Approval and continuation | [ADR-007](../ADR/ADR-007-human-review-required-for-apply-and-promotion.md), [Current product capabilities](../product/CURRENT_PRODUCT_CAPABILITIES.md) | [Cockpit backend contract](../ALPHA_COCKPIT_BACKEND_CONTRACT.md), historical workflow receipts | Explicit backend approval evidence and live gate re-evaluation are required. Approval does not imply continuation, and continuation does not imply apply. |
| Apply and workspace safety | [Workspace apply spine](../agents/WORKSPACE_APPLY_SPINE.md), [ADR-004](../ADR/ADR-004-proposal-review-apply-boundary.md), [ADR-007](../ADR/ADR-007-human-review-required-for-apply-and-promotion.md) | [Isolated promotion apply proof](../ISOLATED_PROMOTION_APPLY_170.md), [Disposable workspace checkpoint](../DISPOSABLE_WORKSPACE_APPLY_COMPLETION_CHECKPOINT.md) | Apply is a separate, approved, hash-bound backend mutation path. Current real apply is bounded to configured disposable or isolated non-main worktrees; no active-repository or main-branch autonomy is claimed. |
| Frontend information architecture | [Product UX v2](../product/IRONDEV_PRODUCT_UX_SPEC_V2.md), [v2 foundations](../product/IRONDEV_PRODUCT_UX_SPEC_V2_FOUNDATIONS.md), [v2 surfaces](../product/IRONDEV_PRODUCT_UX_SPEC_V2_SURFACES.md), [v2 system behavior](../product/IRONDEV_PRODUCT_UX_SPEC_V2_SYSTEM.md), [v2 handoff](../product/IRONDEV_PRODUCT_UX_SPEC_V2_HANDOFF.md), [product-completion map](../product/IRONDEV_CLEANUP_AND_PRODUCT_COMPLETION_PLAN.md) | [ADR-015](../decisions/ADR-015-client-shell-strategy.md), implemented UX slice records | The canonical product IA is session/project entry, then Board, Workshop, Work Item, and Library. The React/Tauri client renders backend truth and owns no authority. |
| Audit | [Audit export and detail specification](../product/IRONDEV_AUDIT_EXPORT_SPEC.md), [ADR-005](../ADR/ADR-005-tool-request-audit-execution-boundary.md), [ADR-001](../ADR/ADR-001-SQL-source-of-truth.md) | [Chat governance audit persistence](CHAT_GOVERNANCE_AUDIT_PERSISTENCE.md) | SQL-backed audit is an authoritative read-only history of recorded activity. An audit record is evidence, not approval or execution permission. |
| Memory | [ADR-001](../ADR/ADR-001-SQL-source-of-truth.md), [ADR-002](../ADR/ADR-002-retrieval-match-not-memory-candidate.md), [ADR-003](../ADR/ADR-003-memory-candidate-proposal-promotion-boundary.md), [ADR-007](../ADR/ADR-007-human-review-required-for-apply-and-promotion.md) | [Governed memory proposal substrate](../BLOCK_K_GOVERNED_MEMORY_PROPOSAL_SUBSTRATE.md) | SQL-backed accepted records are durable authority; retrieval and vector indexes are derived. Proposals, matches, safety results, and model output cannot self-promote. The complete current implementation map is intentionally deferred to CLN-23. |
| Deployment topology | [Architecture](../ARCHITECTURE.md), [Local development](../local-development.md) for supported local topology only | [ADR-015](../decisions/ADR-015-client-shell-strategy.md) | The supported current topology is API plus SQL-backed infrastructure with thin HTTP clients. Hosted workspace and production deployment topology are deferred; no document currently has authority to claim them as shipped. |
| Database and source-of-truth rules | [ADR-001](../ADR/ADR-001-SQL-source-of-truth.md), [ADR-017](../decisions/ADR-017-migration-state-tracking.md) | [H12 read projection rebuild story](H12_READ_PROJECTION_BACKUP_REBUILD_STORY.md), [Chat governance audit persistence](CHAT_GOVERNANCE_AUDIT_PERSISTENCE.md) | SQL is the durable source of truth for authoritative backend product state. Governed file-backed workspace evidence is allowed only where an explicit contract defines it. Migration state and derived projections are evidence, not authority. |

## Classified Architecture-Bearing Documents

This corpus covers documents that make system-wide or domain architecture claims and therefore can compete with the canonical map. CLN-04 owns the exhaustive inventory of every repository document.

### Canonical

| Document | Authority |
| --- | --- |
| [Architecture](../ARCHITECTURE.md) | Current system shape and thin product boundary. |
| [Agents](../AGENTS.md) | Current governed agent roles and authority limits. |
| [ADR-001 through ADR-007](../ADR/README.md) | Accepted source-of-truth, memory, proposal, tool, critic, and human-review boundaries. |
| [ADR-015](../decisions/ADR-015-client-shell-strategy.md) | API, client, CLI, and Tauri boundary. |
| [ADR-016](../decisions/ADR-016-governed-chat-mode-gating.md) | Chat mode classification and governance boundary. |
| [ADR-017](../decisions/ADR-017-migration-state-tracking.md) | Migration-state evidence boundary. |
| [ADR-018](../ADR_018_PASSIVE_AGENT_CONTAINMENT.md) | Passive-agent containment in governed tool paths. |
| [Workspace apply spine](../agents/WORKSPACE_APPLY_SPINE.md) | Current governed apply sequence and safety invariants. |
| [Current product capabilities](../product/CURRENT_PRODUCT_CAPABILITIES.md) | Current reachable implementation truth. |
| [Cleanup and product-completion map](../product/IRONDEV_CLEANUP_AND_PRODUCT_COMPLETION_PLAN.md) | Current product-area classification and canonical IA. |
| [Product UX v2 index and modules](../product/IRONDEV_PRODUCT_UX_SPEC_V2.md) | Current product and frontend experience contract. |
| [v2.5 Governance UX specification](../product/IRONDEV_V25_GOVERNANCE_UX_SPEC.md) | Current Governance surface contract. |
| [Audit export and detail specification](../product/IRONDEV_AUDIT_EXPORT_SPEC.md) | Current Audit detail and export contract. |

### Supporting

| Document | Bounded use |
| --- | --- |
| [ADR pack index](../ADR/README.md) | Navigation and original freeze context for accepted ADRs. |
| [ADR-008 API surface exposure rules](../ADR/ADR-008-api-surface-exposure-rules.md) | Block F exposure rationale; current client boundary is ADR-015. |
| [API surface exposure rules](../API_SURFACE_EXPOSURE_RULES.md) | Earlier API contract constraints that remain compatible with current ADRs. |
| [Agent capability matrix](../AGENT_CAPABILITY_MATRIX.md) | Detailed current role/capability inventory beneath `AGENTS.md`. |
| [Cockpit backend contract](../ALPHA_COCKPIT_BACKEND_CONTRACT.md) | Project-scope and workflow rules; “cockpit” naming is legacy. |
| [Governance substrate invariants](../BLOCK_G_GOVERNANCE_SUBSTRATE_INVARIANTS.md) | Durable evidence-versus-authority rules. |
| [Durable workflow run substrate](../BLOCK_J_DURABLE_WORKFLOW_RUN_SUBSTRATE.md) | Initial ledger contract still supporting the current run spine. |
| [Governed memory proposal substrate](../BLOCK_K_GOVERNED_MEMORY_PROPOSAL_SUBSTRATE.md) | Initial staging boundary beneath current memory ADRs. |
| [API/client/CLI boundary findings](API_CLIENT_CLI_BOUNDARY_FINDINGS.md) | Verified implementation findings beneath Architecture and ADR-015. |
| [BA tease-out system](BA_TEASE_OUT_SYSTEM.md) | Current Exploration behavior within the Product UX contract. |
| [Chat governance audit persistence](CHAT_GOVERNANCE_AUDIT_PERSISTENCE.md) | Transactional persistence detail for governed chat turns. |
| [H12 read projection rebuild story](H12_READ_PROJECTION_BACKUP_REBUILD_STORY.md) | Derived-projection backup and rebuild invariant. |
| [Local development](../local-development.md) | Supported local topology and startup guidance only. |
| [v2.5 Product specification](../product/IRONDEV_PRODUCT_UX_SPEC_V25.md) | Product direction and planning baseline; explicitly does not claim current support. |
| [Isolated promotion apply proof](../ISOLATED_PROMOTION_APPLY_170.md) | Accepted bounded apply checkpoint beneath the current apply spine. |

### Historical

| Document | Why historical |
| --- | --- |
| [Backend architecture](../BACKEND_ARCHITECTURE.md) | Snapshot after PRs 42-51.5 and before the Block F contract freeze. |
| [Backend contract freeze report](../BACKEND_CONTRACT_FREEZE_REPORT.md) | Freeze assessment for PRs 42-55, not current whole-product architecture. |
| [Block H project authority policy model](../BLOCK_H_PROJECT_AUTHORITY_POLICY_MODEL.md) | Initial PR82 vocabulary-only slice. |
| [Block I A2A handoff contract spine](../BLOCK_I_A2A_HANDOFF_CONTRACT_SPINE.md) | Initial PR90 model-only slice, not current runtime topology. |
| [Block P0-Y authority roadmap](../BLOCK_P0_TO_Y_AUTHORITY_ROADMAP.md) | Explicitly marked fully completed historical roadmap. |
| [Disposable workspace apply proof](../DISPOSABLE_WORKSPACE_APPLY_PROOF.md) | Earlier conditional-go checkpoint superseded by the current apply spine. |
| [Disposable workspace apply completion checkpoint](../DISPOSABLE_WORKSPACE_APPLY_COMPLETION_CHECKPOINT.md) | Proposed checkpoint retained as implementation history. |
| [UX-0 screen/state inventory](../ux/ux-0-screen-state-inventory.md) | Explicit 7 July implementation snapshot with since-shipped gaps. |
| [UX-START entry sequence](../ux/ux-start-entry-sequence.md) | Implemented slice record, not the current complete entry contract. |
| [UX project entry screen](../ux/ux-project-entry-screen.md) | Implemented slice record, not the current complete project IA. |

### Superseded

| Document | Canonical replacement |
| --- | --- |
| [Flow-first UX specification](../ux/flow-first-ux-spec.md) | Product UX v2 and Current Product Capabilities. |
| [Full UX map](../ux/full-ux-map.md) | Product UX v2 and the product-completion map. |
| [Future UX product spec](../ux/future-ux-product-spec.md) | Product UX v2 and Current Product Capabilities. |
| [LangGraph-style workflow design](../WORKFLOW_ENGINE_DESIGN.md) | Current workflow/run spine and explicit backend contracts; the proposed “next” engine is not current direction. |
| [Knowledge Compiler semantic memory design](../SEMANTIC_MEMORY_DESIGN.md) | Memory ADRs and the future CLN-23 reality audit; its “next major branch” instruction is not active. |

### ParkingLot

| Document | Boundary |
| --- | --- |
| [Overall memory system discussion](OVERALL_MEMORY_SYSTEM_DISCUSSION.md) | Explicit future discussion only; no active implementation authority. |

### DeleteCandidate

None confirmed in this bounded architecture corpus. CLN-04 may identify redundant active prose, but deletion requires reference and runtime-usage proof in a separate slice. Historical receipts are never delete candidates.

## Known Architecture Truth Gaps

| Gap | Current treatment | Owning cleanup slice |
| --- | --- | --- |
| Complete inventory of current memory read, write, promotion, and index paths | Do not infer it from old semantic-memory designs. Preserve the accepted non-authority boundaries. | CLN-23 |
| Hosted workspace and production deployment topology | Explicitly deferred; local topology must not be presented as hosted support. | CLN-38 through CLN-42 after earlier cleanup gates |
| Durable Work Item aggregate distinct from ticket compatibility substrate | Product remains partially backed by legacy ticket semantics. | Product-completion map owner; no new feature work during cleanup |
| Exhaustive status for every repository document | This index classifies architecture-bearing competitors only. | CLN-04 |

## Review Line

A developer can find the current rule for a domain without treating an old roadmap, proposed design, or receipt as present authority.

## Killjoy Line

An architecture document is not canonical because it is detailed, recent-looking, or confidently written. It is canonical only when this index names it and the running contracts still agree.
