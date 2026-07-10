# Flow-First UX Specification

> **Status: Superseded as a current product contract.** Retained for design rationale. Use [IronDev Product and UX Specification v2](../product/IRONDEV_PRODUCT_UX_SPEC_V2.md) and [Current Product Capabilities](../product/CURRENT_PRODUCT_CAPABILITIES.md) for current navigation and implementation truth.

Status: proposed rework of the TauriShell UX. Supersedes the workspace-tab information architecture.

## Thesis

The current shell is organised by nouns: eight sibling workspaces (Home, Chat, Build, Tickets, Knowledge, Runs, Governance, Settings). The system's actual product is a **pipeline with tension**: BA shaping → ticket contract → governed build → adversarial review → human gate → receipt. The user should live on that pipeline, not hop between tabs while carrying the thread in their head.

> The UI should *be* the flow. Gates are barriers you satisfy, not pages you visit.

## Design principles

1. **The work item is the unit of navigation.** One spine from intent to receipt; every screen is a stage of the same object.
2. **Gates are rendered as locks.** A stage the item has not earned is visibly locked, with the unmet conditions listed. Blocked states always show the next safe action (Block D language).
3. **The contract rides along.** Acceptance criteria, binding decisions, standards, affected files, and open questions attach at Shape and remain visible — and accumulating — at every later stage. Architecture is enforced in view, not archived in a Knowledge tab.
4. **Tension is visible.** Critic findings, dispositions, and the human gate are first-class UI, not a modal at the end. A rejected disposition is flagged for the human, never silently absorbed.
5. **The UI displays backend truth; it never invents it.** No screen synthesises state the API cannot explain. Read-only surfaces stay read-only.

## Information architecture: three surfaces

### 1. The Board (portfolio)

Home screen. Work items as cards in pipeline columns: Shape, Ticket, Build, Review, Done. Dependency edges between cards ("blocked by WI-42"). Batch actions appear here when the sequencing layer lands (north star: define a project, let it run, watch tickets flow). Each stage column doubles as a persona inbox — BA lives in Shape, approver lives in Review.

### 2. The Work Item (the spine)

Where ~90% of time is spent. Constant layout, three invariants:

- **Stage rail (top).** Shape → Ticket → Build → Review → Done, with gates drawn *between* stages. Gate states: locked (conditions listed), satisfied, waiting-on-human.
- **Left pane changes per stage.**
  - Shape: shaping discussion (chat embedded as a tool, not a destination) with promote-to-criteria affordances.
  - Ticket: the frozen contract, readiness detail, provenance (created-from chat, based-on files/decisions).
  - Build: live run view — Orchestrator slices, Builder diff progress, Tester runs; sandbox/worktree indicator.
  - Review: critic findings, each with a required disposition (accepted-will-fix / accepted-already-fixed / rejected-reason / deferred-ticket / needs-clarification); human gate actions at the foot.
  - Done: receipts, trace, links into governance viewers.
- **Right pane is always the contract.** Criteria (with per-stage enrichment: criterion → test at Build, criterion → finding at Review, criterion → receipt at Done), architecture chips (ADRs, standards), affected files, open questions.

### 3. The Library (reference)

Non-workflow surfaces: solution explorer (code-index truth with staleness banner), ADR/standards browser, governance timeline and the existing 17 governance viewers (re-homed as drill-down targets linked from work-item receipts), settings.

## Artifact flow model

| Stage | Contract rail shows | New at this stage |
| --- | --- | --- |
| Shape | draft criteria, arch chips, files, open questions | attach ADRs/standards, raise open questions |
| Ticket | frozen criteria, readiness state | MustObey bindings, provenance refs |
| Build | criterion → test matrix filling in | diff stats, worktree id, enforced-standard ticks |
| Review | criterion → finding pins, dispositions | critic verdict, disposition ledger |
| Done | criterion → receipt links | apply/commit receipts, trace links |

## Endpoint mapping (current API)

| UI element | Endpoint |
| --- | --- |
| Shaping discussion | `POST /api/projects/{id}/chat/sessions` + messages |
| Draft criteria / plan / tests | `POST .../tickets/draft`, `.../draft/plan`, `.../draft/tests` |
| Promote to ticket | `POST .../tickets` |
| Stage rail gate: readiness | `GET .../tickets/{id}/build-readiness` |
| Build run view | `POST/GET .../tickets/{id}/build-runs`, `.../build-runs/{runId}` |
| Review pane | `GET .../build-runs/{runId}/review`, `.../review-package` |
| Receipts / trace | `GET /api/runs/{runId}/report`, `/events`; governance viewers |
| Solution explorer | code-index `file-count`, `files/search`, `files/recent`; **needs new `GET .../code-index/files` list endpoint** |

## Roles and multiuser

Stage visibility maps to Block F roles: BA/author (Shape, Ticket), operator (Build), reviewer (Review, read), approver (Review gate actions), viewer (read-only everywhere). Hard prerequisite before inviting a second human: A12 tenant-scope proof and the F01–F15 role/visibility matrix. Platform hooks (GitHub etc.) integrate at the API contract, entering the same pipeline via `import-external` and the feedback source kinds; platforms are clients, never authority.

## Component inventory

**Tear down:** the 8-route workspace shell, `WorkspaceNav`, pathname-ternary governance routing in `IronDevShell.tsx`, duplicate primitives (`components/` vs `design-system/` copies of StatusBadge, EmptyState, WorkspaceHeader).

**Keep:** API client + generated OpenAPI types, `useProjectContext` / `useSessionContext`, all 17 governance viewer panels (re-homed under Library), MarkdownRenderer, diff viewing pieces, Playwright harness.

**New:** StageRail (with gate locks), ContractRail, WorkItemScreen (stage-switching left pane), BoardScreen, SolutionExplorer, DispositionControl, HumanGatePanel.

## Sequencing (depth-first)

1. Work Item screen, Shape → Ticket only (also serves as the Phase 0 cockpit).
2. Build + Review stages as the walking skeleton comes alive.
3. Board when more than one work item flows (arrives with batch sequencing).
4. Library last — mostly re-homing existing panels.

Just-in-time backend pulls the UI will force: A01 (readiness API reads real truth), I01–I04 (standard error/blocked/stale envelopes), D19 (next-safe-action formatting), the code-index list endpoint.

## Mockups

High-fidelity screen mockups (Board, Shape, Build, Review, Library) are maintained as a self-contained HTML page, published at:

<https://claude.ai/code/artifact/98d05e6e-10e8-47c0-bfa7-f40a7dcf7b2b>

Source of truth for visual decisions until a design-system doc exists.

## Open questions

- Does Shape produce a *draft work item* immediately, or only on first promote? (Recommend: immediately, so the Board shows shaping work.)
- Where do multi-ticket epics live — Board grouping or a parent work item?
- How much of the run view is live-streamed vs. polled? (Current API is poll-shaped.)
