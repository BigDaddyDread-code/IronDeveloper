# Shared Truth Renderers

**Status:** Canonical client presentation contract

**Last reviewed:** 15 July 2026

**Programme slice:** CLN-30

`TruthStateRenderer` is the Tauri shell's common presentation primitive for backend-derived state. It covers authentication required, API unreachable, tenant required, project required, readiness, governed refusal, not implemented, loading, empty, error, stale data, and partial data.

The renderer owns labels, visual tone, stable test identity, heading structure, busy state, and live-region behavior. Assertive failure states render as alerts; non-interrupting states render as status regions; only loading is marked busy. It does not fetch data, infer scope, calculate readiness, interpret evidence as approval, or decide whether an action is permitted. Callers supply the title, message, and any already-authorized action from their existing backend contract.

`LoadingState`, `ErrorState`, and `EmptyState` now delegate to the shared renderer so existing call sites gain the contract without a broad screen rewrite. Further migrations should preserve each screen's backend-owned state machine and replace presentation only.

## Rules

- Use `governedRefusal` only for a refusal actually returned by the governed backend path.
- Use `readiness`, `staleData`, and `partialData` only when the payload carries that truth; absence alone is not permission to infer it.
- Retry actions repeat a safe read or connection attempt. They never bypass a refusal or scope requirement.
- `notImplemented` is an honest capability boundary, not a disabled-looking promise that local code can fulfill.
- Renderer output is evidence to the user, not authorization for the client.
