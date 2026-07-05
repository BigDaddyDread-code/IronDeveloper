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
Deterministic single-ticket smoke: repeatable to human gate.
Release path to Applied: blocked.
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
| critic review | Partially proven | Critic-review contracts and hard-stop receipts exist; continuation/apply require critic review in later P3 work. | D-2a does not automate or persist an independent critic review request/record in the release journey. |
| finding disposition | Not proven | Finding disposition models/traces exist. | Release path does not prove required findings are dispositioned before continuation/apply. |
| accepted approval | Partially proven | Approval records/evaluators exist; D-2a correctly does not create accepted approval. | Release path has not proven live user approval recording and hash-matched consumption through the product path. |
| continuation | Partially proven | Workflow continuation contracts exist and hard-stop receipts require critic review/approval. | D-2a stops at the human gate; continuation is not requested in the repeatable smoke. |
| controlled apply | Partially proven | Source apply and controlled apply contracts/executors exist. | Release path does not prove a ticket with hash-matched accepted human approval evidence reaches `Applied` without hidden authority. |
| report reconstruction | Partially proven | D-2a verifies report reconstruction before smoke success. | Product SQL/API final report after continuation/apply is not proven. |
| receipt writing | Proven | D-2a writes `run-receipt.json`, `alpha-smoke-result.json`, and `alpha-smoke-summary.md`; CI receipts exist. | Final release receipt tying setup, approval, continuation, apply, and report is not proven. |
| live model | Blocked | D-2a explicitly blocks `-ModelMode Live` with `LiveModelModeNotImplemented`. | Live model path must either reach gate safely or the release must be explicitly scoped as deterministic-only preview. |
| CI lanes | Proven | The visible CI lanes include governance-boundary, fast-unit, SQL integration, full SQL integration, frontend contract, and SkeletonRun. | CI is evidence only; it does not prove a fresh local user journey. |
| fresh dogfood docs | Not proven | Alpha-smoke docs exist for D-2a and Block J receipts exist. | No `DOGFOOD-ALPHA-LOCAL-001` transcript from fresh checkout exists yet. |

## Release Blockers

The following are release blockers unless explicitly descoped in a later release decision:

1. Root safety must become an invoked release gate, not only a present contract and local smoke preflight.
2. A deterministic single-ticket path must reach `Applied` through continuation and controlled apply without hidden approval.
3. SQL/API persistence must prove ticket, run, receipt, and report state across the product path.
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

REL-1: J10 root safety release gate.

Review line: A safe root is a precondition for evidence. It is not evidence, approval, or execution authority.

Killjoy: If IronDev cannot prove where it is writing, it does not get to write.
