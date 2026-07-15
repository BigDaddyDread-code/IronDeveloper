# CLN-30 Shared Truth Renderers Receipt

**Status:** Historical receipt

**Date:** 15 July 2026

## Outcome

The Tauri design system has one authority-neutral renderer contract for all twelve required product truth states. Existing loading, error, and empty primitives delegate to it.

Focused contract tests prove the alert/status role, live-region priority, and loading busy-state calculation used by the compiled component. The migrated primitives retain their existing layout classes; the shared renderer supplies common heading, test-ID, tone, and action hooks.

## Evidence

- `IronDev.TauriShell/src/design-system/state/TruthStateRenderer.tsx`
- `IronDev.TauriShell/src/design-system/state/LoadingState.tsx`
- `IronDev.TauriShell/src/design-system/state/ErrorState.tsx`
- `IronDev.TauriShell/src/components/EmptyState.tsx`
- `IronDev.TauriShell/tests/truth-state-renderer.spec.ts`
- `Docs/ux/SHARED_TRUTH_RENDERERS.md`

## Boundary

The renderer presents caller-supplied backend truth. It does not infer access, calculate permission, convert evidence into approval, or execute actions.
