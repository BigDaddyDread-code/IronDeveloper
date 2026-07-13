# Dead Code and Compatibility Deletion

**Status:** Supporting deletion proof

**Snapshot:** 15 July 2026

**Programme slice:** CLN-36

CLN-36 deletes only code that passed all five deletion proofs. Compatibility URLs remain parser inputs and resolve to canonical product surfaces; the deleted route components no longer own runtime UI.

| Deleted item | No runtime references | No generated-client reference | No route dependency | No test dependency | Canonical replacement |
|---|---|---|---|---|---|
| `features/home/HomeRoute.tsx` | No importer from live `App`/FlowShell graph | None | `/projects/{projectId}/board` is canonical | No test imports; absence now locked | `flow/board/BoardScreen.tsx` |
| `features/chatToBuild/BuildRoute.tsx` | No importer | API methods remain available to current Work Item flow | `/build` is compatibility-only | No test imports; absence now locked | Work Item `BuildStage` and governed run actions |
| `features/runReports/RunReportsRoute.tsx` | No importer | Generated run-report contracts retained | `/runs` is compatibility-only | No test imports; absence now locked | Work Item evidence and Library Audit |
| `features/runReports/PromotionReviewRoute.tsx` | No importer | No generated reference to the component | No current product route | No test imports; absence now locked | Work Item Review and Governance evidence viewers |
| `features/runReports/useRunReportsWorkspace.ts` | Imported only by the two deleted run-report routes | API facade retained | No independent route | No test imports; absence now locked | Current route-specific backend reads |
| `WorkspaceNavigationProvider` in live `AppProviders` | FlowShell uses `useProductRoute`; no live consumer | None | Product navigation is FlowShell-owned | Tauri build and canonical route tests | Session and Project providers plus product route state |

`app/routes.ts`, governance viewer route metadata, and `state/useWorkspaceNavigation.tsx` are not deleted in this slice because compatibility/static-boundary code and legacy ticket sources still reference their types or hook. Source being old is not proof of deletability.

## Verification

- TypeScript/Vite production build proves the live import graph closes without the deleted modules.
- `dead-code-containment.spec.ts` locks file absence, removal of the legacy provider from live composition, and canonical Board, Work Item, Audit, `/build`, and `/runs` behavior.
- API facade and generated OpenAPI output are unchanged.
- Historical documents are not rewritten; their old component references remain historical evidence, not runtime dependencies.
