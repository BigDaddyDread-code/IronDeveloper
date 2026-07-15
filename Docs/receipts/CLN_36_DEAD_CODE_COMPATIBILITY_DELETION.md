# CLN-36 Dead Code and Compatibility Deletion Receipt

**Status:** Historical receipt

**Date:** 15 July 2026

## Outcome

Four unreferenced legacy route components and their route-only hook were deleted after runtime, generated-client, route, test, and replacement proof. The live app composition no longer mounts the legacy workspace navigation provider. Safe compatibility URL parsing remains intact.

## Evidence

- `Docs/cleanup/DEAD_CODE_COMPATIBILITY_DELETION.md`
- `IronDev.TauriShell/src/app/AppProviders.tsx`
- `IronDev.TauriShell/tests/dead-code-containment.spec.ts`

## Boundary

Compatibility types and referenced legacy ticket code were retained. This slice does not delete merely because code is old.
