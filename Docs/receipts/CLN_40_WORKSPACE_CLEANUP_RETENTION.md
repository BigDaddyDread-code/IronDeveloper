# CLN-40 Workspace Cleanup and Retention Receipt

**Status:** Historical receipt

**Date:** 15 July 2026

## Outcome

A deterministic, fail-closed workspace retention evaluator now defines active, failed, and applied workspace handling, archived evidence, retention, total retained-usage quota pressure, and manual/legal/audit holds.

## Evidence

- `IronDev.Core/Workspaces/WorkspaceCleanupRetentionPolicy.cs`
- `IronDev.UnitTests/WorkspaceCleanupRetentionPolicyTests.cs`
- `Docs/cleanup/WORKSPACE_CLEANUP_RETENTION_POLICY.md`
- 14 focused retention-policy tests passed in Release configuration.

## Boundary

The evaluator identifies candidates for governed review only. It never deletes a workspace or receipt, bypasses a hold, or creates authority.
