# J10 - Logs Evidence Workspace Root Safety

## Purpose

J10 adds a reusable local root-safety validator and applies it to the disposable workspace execution path before it creates workspaces, writes evidence, or runs workspace commands.

## Root Kinds Covered

- workspace roots
- disposable workspace roots
- evidence roots
- logs roots
- sandbox repository paths
- critic canary measurement roots
- batch-map evidence roots

## Safety Rules

The validator rejects missing required roots, relative paths, traversal paths, drive/filesystem roots, user-home roots, broad temp roots, source repository roots, roots under the source repository, file paths, final-path or existing-ancestor symlinks/reparse points, sandbox paths that equal/contain/live under the source repository, and workspace/evidence/log overlap.

Workspace roots are disposable. Evidence and logs must survive cleanup. Therefore workspace roots must not contain evidence or log roots.

## Runtime Behavior On Unsafe Roots

The disposable workspace executor resolves the actual source repository root from `SourcePath` before validating workspace and evidence roots. It handles both `.git` directories and linked-worktree `.git` files, then validates before creating directories, copying source, applying workspace writes, writing evidence, or running commands.

Unsafe roots fail closed with explicit reason codes such as `UnderRepositoryRoot`, `EvidenceUnderWorkspace`, `LogsUnderWorkspace`, `SandboxEqualsSourceRepository`, and `SandboxContainsSourceRepository`.

## Boundary Statement

A safe root is a precondition for evidence. It is not evidence, approval, execution authority, or permission to mutate source.

## Out Of Scope

- SQL schema changes or migrations
- SQL bootstrap/rebuild behavior
- Weaviate bootstrap/rebuild behavior
- developer doctor behavior
- source apply authority
- approval authority
- critic authority
- release/deploy behavior
- workflow continuation
- creating roots as part of validation

## Review Line

A root path is not safe because it is configured. It is safe only after validation.

## Killjoy

A local path can be an authority bypass wearing a config key.
