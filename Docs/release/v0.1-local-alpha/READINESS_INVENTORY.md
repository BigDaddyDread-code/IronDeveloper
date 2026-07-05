# v0.1 Local Alpha Readiness Inventory

## Purpose

This inventory freezes the release truth after the D-series deterministic smoke work and before the release-hardening stream begins.

It records what is proven, what is only partially proven, what is not proven, and what is blocking a repeatable v0.1 Local Alpha path.

## Baseline Included

This inventory assumes the repository baseline includes:

- D-1: BookSeller real target and fixture tickets.
- D-2: deterministic single-ticket skeleton loop with real disposable workspace build/test.
- D-1.1: SkeletonRun CI lane.
- D-2a: repeatable deterministic BookSeller smoke command to the human approval gate.
- Block J local developer reliability scripts and safety contracts through J10.
- REL-1: release-facing root-safety gate vocabulary for required local roots.
- REL-2: deterministic service-level BookSeller path to `Applied` with explicit phrase-bound approval.

## Classification Vocabulary

| Classification | Meaning |
| --- | --- |
| Proven | The behavior is directly exercised by current merged code, scripts, tests, or CI in the stated scope. |
| Partially proven | A contract, script, service, or narrow smoke exists, but it is not enough to prove the release product path. |
| Not proven | The release path has no current end-to-end proof. |
| Blocked | The release path cannot honestly proceed until this gap is closed or explicitly descoped. |
| Out of scope | Not required for v0.1 Local Alpha. |

## Summary Verdict

Current state:

```text
Core governed loop: alive.
Deterministic single-ticket smoke: repeatable to human gate and service-level Applied.
Release path to Applied: partially proven, not SQL/API-persisted yet.
Fresh developer repeatability: partially proven, not dogfood-proven.
External alpha: blocked.
```

The next useful work is not more abstract governance. The next useful work is closing the release path from setup to one governed ticket reaching `Applied` through the product path.

## Subsystem Inventory

| Subsystem | Classification | Current Proof | Release Gap |
| --- | --- | --- | --- |
| local bootstrap | Partially proven | `Scripts/local/bootstrap-local.ps1` exists and its receipt records check/prepare behavior and boundaries. | Fresh-machine bootstrap has not been dogfooded from clone through first successful product run. |
| local SQL | Partially proven | `Scripts/local/sql-local.ps1` exists; SQL CI lanes run; SQL local command is guarded. | SQL/API persisted smoke from ticket to final report is not proven. |
| local Weaviate | Partially proven | `Scripts/local/weaviate-local.ps1` exists and doctor can delegate check-only diagnostics. | Release path does not prove Weaviate schema/content is required, optional, or correctly rebuildable for first run. |
| root safety | Partially proven | J10 root-safety validator exists, disposable workspace execution uses root validation before creating workspaces/evidence/running commands, alpha smoke has local output-root safety, and REL-1 adds a release-facing root-safety gate vocabulary for required roots. | The release gate is not yet wired as a single invoked preflight across every setup, evidence, workspace, and apply path. |
| config redaction | Partially proven | J08 redacted config summary contract exists; local doctor reports redacted diagnostics. | Release setup/docs have not proven all user-facing summaries avoid raw secrets and sensitive local paths in the full first-run path. |
| API startup | Partially proven | API exists and CI/API boundary lanes exist. | Fresh-machine start command and product-path health proof are not captured in a release runbook/dogfood transcript. |
| UI startup | Partially proven | Tauri/OpenAPI/front-end contract CI exists. | Release user journey through the UI is not proven from fresh setup. |
| project import/profile/index | Not proven | BookSeller fixture exists; scripts can use the fixture path. | Product import/profile/index path for a local repo or BookSeller release fixture is not proven end to end. |
| chat-to-ticket | Not proven | Chat/ticket components exist elsewhere in the product. | Messy human intent to confirmed ticket to governed run is not proven in the release path. |
| ticket persistence | Partially proven | SQL/store work exists and SQL lanes run. | D-2a smoke is service-level/in-memory and does not prove persistent ticket/run/report state across API restart. |
| skeleton run | Proven | SkeletonRun CI runs the `SkeletonRun` category and fails on zero selected tests; alpha smoke reaches `PausedForApproval`. | Current proof is deterministic and primarily service-level; product SQL/API path remains separate. |
| workspace execution | Proven | D-series smoke uses disposable workspace execution with real `dotnet build` and `dotnet test` against copied BookSeller source. | Controlled apply to the real target source path remains separate. |
| build/test evidence | Proven | D-series smoke and SkeletonRun lane execute build/test evidence paths. | Release path must still carry this evidence through persistent reports and receipts. |
| critic package | Proven | D-2a receipt includes critic package hash and approval target hash; gate smoke verifies package presence. | Package existence is not a critic review. |
| critic review | Partially proven | Critic-review contracts and hard-stop receipts exist; REL-2 records deterministic clean critic review evidence before continuation. | REL-2 critic review evidence is service-level deterministic smoke evidence, not an external critic service or SQL/API persisted review. |
| finding disposition | Not proven | Finding disposition models/traces exist. | Release path does not prove required findings are dispositioned before continuation/apply. |
| accepted approval | Partially proven | Approval records/evaluators exist; REL-2 requires an explicit phrase-bound approval before recording in-memory accepted approval evidence. | Live user approval recording through SQL/API/product UI is not proven. |
| continuation | Partially proven | REL-2 requests continuation only after critic review evidence and hash-matched accepted approval exist. | SQL/API persisted continuation and restart-safe report reconstruction are not proven. |
| controlled apply | Partially proven | REL-2 reaches `Applied` through the governed copy-only apply spine and records the apply receipt path/hash. | SQL/API persisted source-apply receipt and product-path apply are not proven. |
| report reconstruction | Partially proven | REL-2 reconstructs the final applied report after continuation/apply. | Product SQL/API final report after restart is not proven. |
| receipt writing | Proven | D-2a writes `run-receipt.json`, `alpha-smoke-result.json`, and `alpha-smoke-summary.md`; REL-2 adds approval/apply receipt references; CI receipts exist. | Final release receipt tying setup, SQL/API state, approval, continuation, apply, and report is not proven. |
| live model | Blocked | D-2a explicitly blocks `-ModelMode Live` with `LiveModelModeNotImplemented`. | Live model path must either reach gate safely or the release must be explicitly scoped as deterministic-only preview. |
| CI lanes | Proven | The visible CI lanes include governance-boundary, fast-unit, SQL integration, full SQL integration, frontend contract, and SkeletonRun. | CI is evidence only; it does not prove a fresh local user journey. |
| fresh dogfood docs | Not proven | Alpha-smoke docs exist for D-2a and Block J receipts exist. | No `DOGFOOD-ALPHA-LOCAL-001` transcript from fresh checkout exists yet. |

## Release Blockers

The following are release blockers unless explicitly descoped in a later release decision:

1. Root safety must become an invoked release gate, not only a present contract and local smoke preflight.
2. SQL/API persistence must prove the deterministic single-ticket path reaches `Applied` across ticket, run, approval, receipt, and report state.
3. The product path must prove the same gate order REL-2 proves at service level: critic review evidence, accepted approval, continuation, then apply.
4. Live model mode must either reach the gate safely or be explicitly scoped out as a deterministic-only preview.
5. Chat must propose work, a human must confirm the ticket, and only the confirmed ticket may become governed work.
6. Fresh-machine setup and doctor flow must produce named next safe actions without author-only knowledge.
7. Release docs/runbook must let the next developer repeat the first useful run.
8. `DOGFOOD-ALPHA-LOCAL-001` must record a repeatable run and an explicit verdict.

## Current Useful Commands

```powershell
Scripts/local/bootstrap-local.ps1 -CheckOnly
Scripts/local/sql-local.ps1 -CheckOnly
Scripts/local/weaviate-local.ps1 -CheckOnly
Scripts/local/doctor-local.ps1 -Markdown
Scripts/smoke/alpha-smoke.ps1 -CheckOnly
Scripts/smoke/alpha-smoke.ps1 -Project BookSeller -Ticket validate-book -ModelMode Deterministic -RunUntil Gate
Scripts/smoke/alpha-smoke.ps1 -Project BookSeller -Ticket validate-book -ModelMode Deterministic -RunUntil Applied -RecordHumanApproval -ApprovalPhrase "I approve continuation for run <runId> package <hash>"
```

These commands help a developer understand the current local state. They do not create approval, policy satisfaction, source apply authority, release approval, deployment readiness, or product success.

## Boundary Rules Preserved

- Evidence is not approval.
- Validation passed is not approval.
- A gate halt is not accepted approval.
- A critic package is not a critic review.
- Accepted approval is not policy satisfaction.
- Policy satisfaction is not source apply.
- A smoke run is diagnostic evidence, not release approval.
- Green CI is evidence, not merge, release, or deployment permission.
- Local configuration convenience is not authority.
- A root classified as safe is a precondition, not permission to mutate.

## What This Inventory Does Not Do

- It does not change product behavior.
- It does not add a setup command.
- It does not start API, UI, SQL, or Weaviate.
- It does not mutate source.
- It does not create evidence, approval, continuation, apply, release, or deployment authority.
- It does not claim the product is ready for external alpha use.

## Next Intended PR

REL-3: SQL/API persisted alpha smoke.

Review line: Persistence proves the trail survives the product path. It does not grant approval or authority.

Killjoy: A smoke that only works in memory is a rehearsal, not a release path.
