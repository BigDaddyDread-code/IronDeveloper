# Test Reliability Inventory

**Status:** Canonical cleanup test inventory

**Last reviewed:** 13 July 2026

**Programme slice:** CLN-08

## Purpose

Map executable test suites to owners, CI lanes, runtime/dependency boundaries, seed requirements, observed reliability, and authority importance. Discovery, selection, execution, and release proof remain separate claims.

## Classification Vocabulary

| Classification | Meaning |
| --- | --- |
| `Unit` | In-process deterministic logic with no database, network, process host, or environment dependency. |
| `Integration` | Multiple production components exercised together without necessarily requiring SQL. |
| `SQL integration` | Requires an isolated SQL Server database and migrations/stores. |
| `Boundary` | Proves authority, scope, dependency, source-shape, or non-execution constraints. |
| `Contract` | Proves API, DTO, script, route, migration, or cross-module contract shape. |
| `Generated-client drift` | Regenerates checked-in API/client artifacts and rejects a dirty result. |
| `UX component` | Exercises a bounded React surface or read-only component contract. |
| `Playwright mock` | Browser test with intercepted HTTP contracts and no real backend authority. |
| `Live LocalTest` | Visible UI journey against the supported LocalTest API and isolated database. |
| `Dogfood` | Product-on-itself or fixture-based governed workflow proof. |
| `Release smoke` | Named end-to-end evidence used for release qualification, never release approval. |
| `Quarantined` | Explicit owner/reason/exit record for a test excluded from ordinary execution. Quarantine is not deletion. |

## Discovered Surface

| Surface | Discovered cases | Discovery truth |
| --- | ---: | --- |
| `IronDev.UnitTests` | 393 | Entire project executes in `fast-unit-ci`. |
| `IronDev.IntegrationTests` | 14,748 | Parameterized discovery count; CI executes named/category subsets, not the whole project. |
| `IronDev.IntegrationTests.Api` | 668 | CI executes API boundary and named full-SQL subsets, not the whole project. |
| Tauri Playwright | 747 across 41 files | 158 cases in 18 files execute in `frontend-behavior-ci`; 589 cases in 23 files do not. |
| BookSeller sample domain tests | 5 | Useful sample tests; no dedicated pull-request lane. |
| Disposable BookSeller fixture test project | 0 | Build/run fixture, not a discoverable repository test suite. |

Counts are discovery evidence from `dotnet test --list-tests` and `npx playwright test --list`. They do not claim all discovered cases executed.

## Suite Ownership

| Suite | Classification | Owner | CI lane | Typical runtime | External dependency | Seed requirement | Reliability history | Authority importance |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Entire `IronDev.UnitTests` project | Unit / Boundary | Core and governance | `fast-unit-ci` | About 1-2 min | None | Deterministic builders only | Stable in Phase A-C | Critical for pure authority and projection logic |
| Skeleton governed loop (`TestCategory=SkeletonRun`) | Integration / Boundary | Workflow platform | `skeleton-run-ci` | About 3-4 min lane | None | In-memory stores and fixed evidence builders | 180/180 baseline restored in CLN-00 | Critical governed-loop non-authority boundary |
| Governance/static/API/CLI filter groups | Boundary / Contract | Governance, API, CLI | `governance-boundary-ci` | About 4-5 min lane | Full git history for changeset guards | Source tree and deterministic fixtures | Stable in Phase A-C | Critical authority, scope, and dependency wall |
| Narrow governance SQL stores | SQL integration | Data and governance | `sql-integration-ci` | About 2-3 min lane | SQL Server 2022 | Unique CI database; migrations; no product demo seed | SQL connectivity alone retries during startup | Critical durable approval/policy/apply evidence |
| Clean migration, named SQL, release and demo proofs | SQL integration / Release smoke / Dogfood | Data, release, dogfood | `full-sql-integration-ci` | About 6-8 min lane | SQL Server 2022 | Unique CI database plus bounded named smoke fixtures | Stable in Phase A-C | Critical migration and governed journey evidence |
| API in-process endpoint baseline | Integration / Contract | API | `full-sql-integration-ci` through platform baseline | Included above | SQL for baseline | Fresh isolated database | Stable | High route, auth, environment, and catalog boundary |
| TypeScript/OpenAPI contract | Contract / Generated-client drift | API and client | `frontend-contract-ci` | About 2-4 min lane | Node, .NET API startup | Checked-in OpenAPI snapshot | Stable | Critical client/server enum and request-shape truth |
| Current-product browser suite | Playwright mock / UX component | Product client | `frontend-behavior-ci` | About 4 min lane, four workers | Chromium; mocked HTTP | Per-test route fixtures and localStorage setup | Three observed timing failures; all passed unchanged on rerun | High user-path and client non-authority behavior |
| Historical/component browser suite | Playwright mock / UX component | Product client and governance surfaces | No CI execution lane | 8-12 min unfiltered locally | Chromium; mocked HTTP | Per-file route fixtures | No consolidated history; 589 cases are unowned | Mixed; many authority-firewall cases are high |
| Live LocalTest browser smoke | Live LocalTest | Product and operations | Manual PR/release evidence | About 1-3 min after startup | LocalTest API, LocalDB, Vite/browser | `bob@irondev.local`, test tenant/project fixtures | Manual-only; depends on supported launcher and LocalDB health | Critical entry-path proof, not replaceable by mocks |
| Live model hero walk | Dogfood / Release smoke / Quarantined | Dogfood and release | Manual opt-in only | Long-running | SQL, configured live model, disposable workspace | HERO fixture and explicit environment opt-in | Registered as owner-required external dependency | High real-loop evidence; cannot run without credentials |
| Manual indexing task | Quarantined | Indexing | `ManualLocal` only | Manual | Local index/provider | Developer-selected project | Existing ignored legacy debt; only accepted ignore | Low until converted; must not masquerade as coverage |
| Documentation contract | Boundary / Contract | Test platform | `documentation-contract-ci` | About 22 sec | None | Repository documentation tree | Zero-defect baseline in CLN-07 | High repository truth boundary |
| BookSeller sample domain tests | Unit | Sample owner | No dedicated lane | Under 10 sec | None | Inline sample entities | No recorded flake | Low repository authority; useful fixture confidence |

## Playwright File Inventory

`frontend-behavior-ci` owns the rows marked `frontend-behavior-ci`. All other mocked files are selectable locally but are not current CI execution evidence.

| File | Cases | Classification | Owner | CI lane | Seed/dependency | Reliability | Authority importance |
| --- | ---: | --- | --- | --- | --- | --- | --- |
| `accepted-approval-panel.spec.ts` | 26 | UX component / Playwright mock | Governance UI | None | Mock approval records | No known failure; unowned | Critical authority wording |
| `action-request-ui.spec.ts` | 27 | UX component / Playwright mock | Governance UI | None | Mock requests | No known failure; unowned | High request-versus-execution boundary |
| `agent-profiles.spec.ts` | 9 | Playwright mock | Settings | `frontend-behavior-ci` | Mock profile APIs | Stable | High configuration provenance |
| `ai-connections.spec.ts` | 4 | Playwright mock | Settings | `frontend-behavior-ci` | Mock connection APIs | Stable | High credential-boundary rendering |
| `approval-package-review.spec.ts` | 40 | UX component / Playwright mock | Governance UI | None | Mock package evidence | No known failure; unowned | Critical review non-authority |
| `batch-run.spec.ts` | 2 | Playwright mock | Legacy batch UI | None | Mock batch APIs | No known failure; unowned | Low compatibility surface |
| `board-ux.spec.ts` | 4 | Playwright mock | Board | `frontend-behavior-ci` | Mock board/readiness | Stable | High backend-truth projection |
| `chat-conversation-first.spec.ts` | 13 | Playwright mock | Workshop | `frontend-behavior-ci` | Deferred mock completion | One missed transient `chat.sending` assertion on PR #825; rerun passed | High conversation workflow |
| `chat-session-navigation.spec.ts` | 18 | Playwright mock | Workshop | `frontend-behavior-ci` | Mock channels/sessions/unread | One unread badge clear timeout on PR #825; rerun passed | High collaboration state |
| `chat-ticket-draft-review.spec.ts` | 5 | Playwright mock | Workshop | `frontend-behavior-ci` | Mock draft/session | Stable | High discussion-to-work boundary |
| `chat-visual-smoke.spec.ts` | 2 | UX component / Playwright mock | Workshop | Manual visual command | Mock session | No known failure; not CI-owned | Medium visual layout |
| `dogfood-receipt-viewer.spec.ts` | 45 | Dogfood / Playwright mock | Dogfood UI | None | Mock receipts | No known failure; unowned | Medium evidence presentation |
| `flow-shell-smoke.spec.ts` | 13 | Playwright mock | Product shell | `frontend-behavior-ci` | Mock session/project | Stable | Critical entry/navigation state |
| `governance-information-architecture.spec.ts` | 3 | Playwright mock | Governance | `frontend-behavior-ci` | Mock overview | Stable | High canonical Governance IA |
| `governance-overview.spec.ts` | 7 | Playwright mock | Governance | `frontend-behavior-ci` | Mock overview/failure | Stable | Critical backend-owned control state |
| `governance-timeline.spec.ts` | 26 | UX component / Playwright mock | Governance UI | None | Mock events | No known failure; unowned | High evidence chronology |
| `library-documents.spec.ts` | 14 | Playwright mock | Documents | `frontend-behavior-ci` | Mock documents/processing | Stable | High version/provenance behavior |
| `localtest-manual-smoke.spec.ts` | 1 | Live LocalTest | Product/operations | Manual only | Real LocalTest API/DB/seed | Skipped unless explicit live opt-in | Critical real entry journey |
| `members-directory.spec.ts` | 13 | Playwright mock | Members | `frontend-behavior-ci` | Mock membership APIs | Stable | High visibility/role boundary |
| `memory-proposal-review.spec.ts` | 37 | UX component / Playwright mock | Memory UI | None | Mock proposals | No known failure; unowned | Critical memory non-authority |
| `operation-status-viewer.spec.ts` | 33 | UX component / Playwright mock | Governance UI | None | Mock operation status | No known failure; unowned | High evidence-only boundary |
| `patch-artifact-panel.spec.ts` | 30 | UX component / Playwright mock | Apply UI | None | Mock patch artifact | No known failure; unowned | Critical patch evidence |
| `patch-package-viewer.spec.ts` | 41 | UX component / Playwright mock | Apply UI | None | Mock package | No known failure; unowned | Critical package review |
| `policy-satisfaction-panel.spec.ts` | 25 | UX component / Playwright mock | Governance UI | None | Mock policy records | No known failure; unowned | Critical policy-evidence boundary |
| `product-identity.spec.ts` | 2 | Playwright mock | Product shell | `frontend-behavior-ci` | Static product state | Stable | Medium product truth language |
| `project-entry.spec.ts` | 11 | Playwright mock | Entry/project chooser | `frontend-behavior-ci` | Mock projects/readiness | One missing chooser timeout on PR #828; local 11/11 and rerun passed | Critical entry journey |
| `project-routing.spec.ts` | 9 | Playwright mock | Product routing | `frontend-behavior-ci` | Mock session/project | Stable | Critical canonical route behavior |
| `project-setup.spec.ts` | 12 | Playwright mock | Provisioning | `frontend-behavior-ci` | Mock readiness/actions | Stable | High readiness truth |
| `release-readiness-evidence-panel.spec.ts` | 34 | UX component / Playwright mock | Release UI | None | Mock release evidence | No known failure; unowned | Critical evidence-not-approval |
| `rollback-evidence-panel.spec.ts` | 32 | UX component / Playwright mock | Apply UI | None | Mock rollback evidence | No known failure; unowned | Critical recovery evidence |
| `skeleton-run-stages.spec.ts` | 12 | Playwright mock | Legacy run UI | None | Mock run stages | No known failure; unowned | Medium workflow compatibility |
| `source-apply-dry-run-receipt-panel.spec.ts` | 28 | UX component / Playwright mock | Apply UI | None | Mock dry-run receipts | No known failure; unowned | Critical apply rehearsal evidence |
| `source-apply-review-panel.spec.ts` | 30 | UX component / Playwright mock | Apply UI | None | Mock apply review | No known failure; unowned | Critical human apply boundary |
| `tool-gate-decision.spec.ts` | 32 | UX component / Playwright mock | Tools/Governance UI | None | Mock gate decisions | No known failure; unowned | Critical request/gate separation |
| `tools-catalogue.spec.ts` | 6 | Playwright mock | Tools | `frontend-behavior-ci` | Mock tool capabilities | Stable | High connection/enablement truth |
| `ui-authority-firewall.spec.ts` | 13 | Boundary / Playwright mock | Governance UI | None | Static/mocked hostility cases | No known failure; unowned | Critical frontend authority firewall |
| `ui-authority-hostile-corpus.spec.ts` | 5 | Boundary / Playwright mock | Governance UI | None | Hostile text corpus | No known failure; unowned | Critical semantic permission leaks |
| `ux-start.spec.ts` | 5 | Playwright mock | Entry/Board | `frontend-behavior-ci` | Mock entry/readiness | Stable | Critical front door and Board action |
| `workflow-continuation-evidence-panel.spec.ts` | 31 | UX component / Playwright mock | Workflow UI | None | Mock continuation evidence | No known failure; unowned | Critical continuation non-authority |
| `workflow-run-step-viewer.spec.ts` | 37 | UX component / Playwright mock | Workflow UI | None | Mock workflow records | No known failure; unowned | High durable trace rendering |
| `workitem-ux.spec.ts` | 10 | Playwright mock | Work Item | `frontend-behavior-ci` | Mock work item/run evidence | Stable | Critical governed work journey |

## Reliability Findings

| Finding | Severity | Evidence | Owner | Next slice |
| --- | --- | --- | --- | --- |
| `TEST-REL-01` 589 Playwright cases have no GitHub execution lane | P2 | 747 discovered minus 158 bounded cases | Test platform and product client | CLN-09 assigns critical authority files or records deliberate retirement scope. |
| `TEST-REL-02` integration ownership is filter-based, not exhaustive | P2 | 14,748 discovered cases; CI map owns named/category subsets | Test platform | CLN-09 adds executable inventory checks before broad lane expansion. |
| `TEST-REL-03` three current-product tests have one observed timing failure each | P2 | PR #825 sending/unread; PR #828 project chooser; unchanged reruns passed | Product client | CLN-09 removes transient-state and asynchronous-render races without adding retries. |
| `TEST-REL-04` live model and LocalTest proof are manual | P2 accepted boundary | Credentials/real DB/visible journey cannot be safely mocked as equivalent | Dogfood, release, operations | Preserve explicit manual ownership; CLN-10 stabilizes seeds and CLN-42 qualifies clean clone. |
| `TEST-REL-05` test seed truth is spread across scripts, fixtures, and tests | P2 | LocalTest credentials/projects and SQL smoke identities have multiple owners | Test platform/data | CLN-10 creates the canonical test-data and seed contract. |

No P0/P1 reliability finding is open at CLN-08. No retry, timeout increase, skip, or test deletion is authorized by this inventory.

## Review Line

Every load-bearing test class has an owner and execution truth; unexecuted discovery is named as a gap rather than counted as green.

## Killjoy Line

Fourteen thousand discovered tests are not fourteen thousand executed tests, and one green rerun is not proof that a timing race never existed.
