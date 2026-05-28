# IronDev UI Foundation

The Tauri shell uses small shared primitives for workspace screens:

- `WorkspaceFrame` owns the standard page header, metadata, command bar, and optional review panel slot.
- `WorkspaceSplitPane` owns the list/detail/context workbench layout.
- `WorkspaceCommandBar` renders typed workspace commands and disabled reasons.
- `ContextPanel` and `ReviewPanel` give provenance and review surfaces a consistent frame.
- `MetadataGrid`, `EmptyState`, `LoadingState`, `ErrorState`, and `StatusBadge` keep traceable facts and states consistent.

Keep these primitives boring. New variants should only be added when a real workspace needs them.
