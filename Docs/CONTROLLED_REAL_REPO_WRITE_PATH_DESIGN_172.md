---
id: CONTROLLED_REAL_REPO_WRITE_PATH_DESIGN_172
project: IronDev
title: Controlled Real Repo Write Path Design 172
document_type: ArchitectureDesign
authority: Accepted
status: Current
created_utc: 2026-05-24T17:30:00Z
primary_retrieval_questions:
  - How should IronDev design real repository writes?
  - What evidence is required before a controlled branch or PR apply?
  - What settings can be changed and what safety invariants cannot be changed?
  - What does human approval mean after isolated promotion apply?
boundary: Design only. No real repo write implementation, no promotion approval execution, no branch apply command, and no agent authority expansion.
---

# Controlled Real Repo Write Path Design 172

## Decision

IronDev is not ready to write to the real repository by default.

It is ready to design the locked door out of the disposable cage.

The controlled write path must be a separate reviewed capability that starts from an approved promotion package and writes only to an explicit isolated branch or worktree. It must never write directly to `main`.

## Current Position

The current promotion chain is:

```text
disposable build
  -> promotion package
  -> isolated candidate workspace
  -> build/test evidence
  -> promotion review cockpit
  -> human/Codex review
```

The next future chain may become:

```text
human approves promotion package for branch apply only
  -> create isolated git worktree or temporary branch
  -> apply reviewed promotable files
  -> run build/test/quality gates
  -> produce PR package
  -> human decides whether to open PR
```

This document designs that future path. It does not implement it.

## Hard Invariants

These are not project settings and cannot be disabled by ordinary configuration:

- no direct writes to `main`
- no direct writes to the developer's current working tree
- no agent self-approval
- no accepted memory mutation as part of write apply
- no ticket acceptance as part of write apply
- no bypass of ConscienceAgent review
- no bypass of ThoughtLedger visible reasoning summary
- no apply without trace id
- no apply without source run id
- no apply without promotion package id
- no apply without proposed change id
- no apply without before/after git status evidence
- no apply without build/test/quality evidence
- no promotion of blocked generated files
- no project-ambiguous apply
- no cross-project apply unless explicitly reviewed as cross-project

## Configurable Policy

These should become settings over time:

- write path enabled flag
- permitted promotion modes
- runtime profile
- language adapter
- build command
- test command
- quality gate command
- allowed source file extensions
- blocked path segments
- maximum files changed
- maximum lines changed
- required reviewer roles
- required evidence types
- required approval wording
- branch naming template
- worktree root
- cleanup retention policy
- promotion package expiry window
- retry count for build/test after branch apply

The settings goal is flexibility, not looseness. Different IDA installations should be able to choose their runtime adapters, worktree roots, branch naming, command templates, review roles, and evidence retention without code changes.

Settings must not be used as hidden authority. The effective policy for any write-path run must be captured as evidence so the reviewer can see exactly which settings shaped the decision.

Settings must be layered:

```text
global defaults
  -> project settings
  -> run settings
  -> explicit human override
  -> hard invariants still win
```

## Language And Runtime Adapters

The controlled write path must remain language-aware without becoming language-specific.

The first executable path is still `csharp-dotnet`, but the design must carry language and runtime metadata forward from the promotion package:

- target language
- target stack
- runtime profile
- build command
- test command
- quality command
- source file extensions
- generated output rules
- package/cache output rules

Future Java, TypeScript, Python, or other adapters should plug into the same policy and evidence shape. Adding a language adapter must not require changing the governance model, approval meaning, or hard invariants.

If no reviewed executable adapter exists for a target runtime, the future write-path command must fail closed with `NeedsMoreEvidence`.

## Approval Meaning

Human approval must be scoped.

Approving an isolated candidate does not mean:

- apply to `main`
- open a pull request
- merge a pull request
- update accepted memory
- accept tickets
- allow future similar writes automatically

For the first controlled write path, approval can only mean:

```text
Apply this specific promotion package to this specific isolated branch/worktree for validation.
```

After validation, a second review is required before PR creation.

## Future Command Shape

Future commands should look like:

```text
promotion apply branch --package-run-id <run> --run-id <run> --target-worktree <path> --json
promotion pr-package create --branch-run-id <run> --run-id <run> --json
```

These commands are not implemented in 172.

## Required Evidence For Future Apply

A controlled branch/worktree apply report must include:

- run id
- trace id
- project
- promotion package id
- proposed change id
- source run id
- source trace id
- target branch or worktree path
- proof target is not `main`
- proof target is not the active developer working tree
- before git status
- after git status
- changed file list
- blocked file rejection list
- build output
- test output
- quality output
- ConscienceAgent decision
- ThoughtLedger summary
- policy snapshot
- hard invariant checklist
- approval record
- final recommendation

## Future UI Shape

The Run Reports / Promotion Review cockpit should show:

- what was approved
- who/what approved it
- what scope was approved
- what files will be applied
- what files remain blocked
- what command will run
- what hard invariants are active
- what settings shaped the run
- whether branch/worktree validation passed
- whether PR package creation is allowed

The UI must not silently convert a review into an apply.

## Failure Rules

The future branch/worktree apply must fail closed if:

- promotion package is missing
- promotion package approval state is not valid for branch apply
- package or proposed change id does not match
- target path is inside the active developer working tree
- target branch is `main`
- target branch is ambiguous
- blocked file appears in the apply list
- build/test/quality command is missing
- build/test/quality command fails
- ConscienceAgent blocks or needs more evidence
- ThoughtLedger summary is missing
- trace writing fails

## Definition Of Done For A Future Implementation

The implementation is not complete until:

- real `main` remains unchanged
- active developer working tree remains unchanged
- branch/worktree apply report exists
- PR package report exists
- promotion review cockpit can read both
- main alpha regression passes
- code standards pass
- human approval is recorded but does not self-execute merge

## Boundary

This slice is design only.

It does not add:

- a branch apply command
- a PR package command
- promotion approval execution
- real repository writes
- accepted memory mutation
- ticket acceptance
- auto-merge
- agent self-approval

## Blunt Assessment

The cage now has a review window.

The next risky capability is a locked door from the cage to a branch.

Do not build the door until the lock design is explicit, settings are separated from invariants, and the review cockpit can show exactly what is being approved.
