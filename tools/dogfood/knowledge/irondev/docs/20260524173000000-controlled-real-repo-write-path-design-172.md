---
id: CONTROLLED_REAL_REPO_WRITE_PATH_DESIGN_172
project: IronDev
title: Controlled Real Repo Write Path Design 172
document_type: ArchitectureDesign
authority: Accepted
status: Current
created_utc: 2026-05-24T17:30:00Z
---

# Controlled Real Repo Write Path Design 172

Slice 172 designs the locked door out of the disposable workspace cage.

It is design only. It does not implement real repository writes, branch apply, PR creation, promotion approval execution, accepted memory mutation, ticket acceptance, auto-merge, or agent self-approval.

The current safe chain is:

```text
disposable build
  -> promotion package
  -> isolated candidate workspace
  -> build/test evidence
  -> promotion review cockpit
  -> human/Codex review
```

The future controlled write path may become:

```text
human approves promotion package for branch apply only
  -> create isolated git worktree or temporary branch
  -> apply reviewed promotable files
  -> run build/test/quality gates
  -> produce PR package
  -> human decides whether to open PR
```

Hard invariants include no direct writes to `main`, no writes to the active developer working tree, no self-approval, no accepted memory mutation, no ticket acceptance, no ConscienceAgent/ThoughtLedger bypass, no apply without trace/evidence, no project-ambiguous apply, and no promotion of blocked files.

Configurable policy includes write-path enabled flags, permitted promotion modes, runtime profile, language adapter, build/test/quality commands, allowed extensions, blocked path segments, max files/lines changed, reviewer roles, evidence requirements, approval wording, branch naming, worktree root, retention policy, expiry window, and retry count.

The design is settings-first but not safety-loose. IDA installations can configure runtime adapters, worktree roots, branch names, command templates, reviewer roles, and retention rules. They cannot configure away the hard invariants.

The write path must stay language-aware without becoming language-specific. `csharp-dotnet` remains the first executable runtime profile, while Java, TypeScript, Python, and other future adapters plug into the same policy/evidence shape. If no reviewed executable adapter exists, the future apply path fails closed with `NeedsMoreEvidence`.

Approval must be scoped. Approving an isolated candidate means only:

```text
Apply this specific promotion package to this specific isolated branch/worktree for validation.
```

It does not mean apply to `main`, open a PR, merge a PR, mutate accepted memory, accept tickets, or allow similar writes automatically.

Future commands may include:

```text
promotion apply branch --package-run-id <run> --run-id <run> --target-worktree <path> --json
promotion pr-package create --branch-run-id <run> --run-id <run> --json
```

These commands are intentionally not implemented by 172.
