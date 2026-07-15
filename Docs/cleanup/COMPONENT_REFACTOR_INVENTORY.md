# Component Refactor Inventory

**Status:** Supporting refactor inventory

**Snapshot:** 15 July 2026

**Programme slice:** CLN-32

This inventory identifies frontend refactor seams before code is split. Line counts are a prioritisation signal, not a quality verdict. No candidate should be split until its observable routing, authority, loading, failure, and action-blocking behavior has characterization coverage.

## Oversized components and hooks

| Candidate | Lines | Responsibilities currently combined | Authority sensitivity | Existing/added characterization | Proposed first seam |
|---|---:|---|---|---|---|
| `features/tickets/useTicketsWorkspace.ts` | 1,398 | legacy ticket load/edit/create, readiness, evidence, run review, action blocking, navigation | High | tickets shell tests; CLN-32 gate test | Retire legacy workspace consumers before extracting backend projections |
| `flow/workitem/WorkItemScreen.tsx` | 1,288 | shape, plan, tests, build, review, approval, recovery, discussion | Critical | flow/work-item smoke; CLN-32 status projection test | Extract stage-specific state adapters without moving permission decisions client-side |
| `features/governance/WorkflowRunStepViewerRoute.tsx` | 917 | search, list/detail selection, evidence rendering, paging/error state | High | governance viewer tests | Extract read-model/query state from presentation |
| `features/chatToBuild/useProjectChat.ts` | 830 | session history, completion, audit replay, persistence, error/retry | Critical | chat navigation/governance suites; CLN-32 gate test | Separate request lifecycle from backend gate projection |
| `flow/library/DocumentsScreen.tsx` | 803 | browse, detail, version, upload, navigation, error mapping | Medium | library document tests | Extract route-specific panels around one API-owned document model |
| `flow/FlowShell.tsx` | 576 | access routing, project deep links, work-item load, primary nav, shell chrome | Critical | flow shell suite; CLN-32 route test | Extract route-outcome orchestration; keep access decisions in contexts/API truth |

Generated `api/generated/ironDevApiTypes.ts` (17,586 lines), public API aliases in `api/types.ts` (2,066), and the API facade (1,737) are large but are not UI-component split candidates. Generated output must remain generated; facade decomposition belongs to an API-client contract slice.

## Duplication inventory

| Category | Evidence | Risk | Disposition |
|---|---|---|---|
| Hooks | `state/useWorkspaceNavigation.tsx` and `flow/navigation/productRoutes.ts::useProductRoute` both own history/popstate state | Two navigation truths during legacy-shell overlap | Make product routing the survivor only after old `App` routes are retired; characterize deep links first |
| API transformations | `describeError` variants exist in Tools, Audit, Members, Build, and Work Item; screens also unwrap different error body fields | Inconsistent retry/error truth and lost backend detail | Introduce one typed API-error projection after response-shape tests exist |
| Gate calculations | `useTicketsWorkspace` and `WorkItemScreen` each combine scope, busy state, readiness/evidence, and action-specific blockers | A refactor could accidentally turn presentation readiness into permission | Characterize each governed action; centralize only backend gate projection, not authority in React |
| Navigation state | old workspace route IDs coexist with product routes and FlowShell route/project synchronization | Stale route or wrong-project selection | Retire old workspace navigation as a dedicated compatibility slice |
| Local fallback truth | legacy Tickets exposes `fallback-config` project context; current code keeps it read-only | Fallback identity can be mistaken for selected backend scope | Preserve the mutation block; delete fallback with the legacy Tickets workspace |

The chat governance projection is already centralized in `getChatModeGate`. CLN-32 found no valid reason to duplicate or relocate it. It remains a projection of explicit backend booleans: mode names and action labels alone cannot enable UI authority.

## Refactor entry rule

Before splitting a candidate, add tests for its current success, empty, slow, unavailable, retry, scope-change, and governed-refusal behavior as applicable. Then move one responsibility with no contract changes. A smaller component is not a successful refactor if it creates a second route state, a second API transformation, or local permission truth.
