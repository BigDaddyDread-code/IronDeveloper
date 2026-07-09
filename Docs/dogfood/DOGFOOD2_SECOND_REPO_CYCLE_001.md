# DOGFOOD-2 — Second Repo Cycle 001 (author dry-run)

**Verdict:** `CompletedWithFindings` — the governed loop generalizes: a non-BookSeller
repository was provisioned, ticketed, run, revised, reviewed, approved, and APPLIED
through product surfaces, with `LoopComplete=true` and zero report gaps. It took
thirteen findings, two recorded deviations, and one product defect to get there.
The cost of ceremony was small; the cost of WALLS was not. The walls are the backlog.

**Operator:** author-adjacent dry-run (Claude). The non-author fresh-checkout walk
remains a separate, still-open human act (DEMO-REHEARSAL-001 / DOGFOOD-ALPHA-LOCAL-001).

## Environment

- IronDev: main @ `cfa85784`. Dates: 2026-07-08 → 09 (cycle crossed midnight; JWT expiry mid-cycle handled by re-auth).
- Second repo: `C:\Users\bob\source\repos\ParcelTracker` @ `69018db` — parcel lifecycle
  domain library + MSTest tests, `ParcelTracker.slnx`, 4/4 tests green, git clean,
  deliberately NO IronDev scaffolding (no `.assets` restore trick).
- SQL: `(localdb)\MSSQLLocalDB` / `IronDeveloper_Local`. UI 5173; API 5118.
- Model modes: Deterministic first (recorded), then live `openai/gpt-4o`
  (`Ai__Provider/Ai__Model/Ai__ApiKey`), `SkeletonRepair:MaxAttempts=1`,
  `SkeletonRevision:MaxAttempts=1` — all explicit, none silent.

## The cycle (condensed; full timestamps in the working log)

1. **Provision** — POST project; readiness honestly blocked
   (`BlockedMissingBuildCommand/TestCommand/UnknownArchitecture`) with per-check
   evidence + remedies; detection proposed correct slnx commands, MSTest,
   `allowBuilderApply=false`. Confirmed profile + commands → **walls F-C/F-C2/F-D**
   (below) → after one recorded SQL deviation, `isReady=true`, all checks Confirmed.
2. **Ticket** — "Parcels can be marked Lost", 3 acceptance criteria.
3. **Run (deterministic)** — first start refused `ReadinessBlocked` (code index +
   AllowBuilderApply; F-E). After explicit index + profile flip: run `87f1ed0f`
   halted `PausedForApproval` — proposing **`src/BookSeller.Domain/Book.cs`** (the
   fixture response) for a ParcelTracker ticket (F-F). Package honest: hash verified,
   3/3 criteria uncovered. **Human gate refused.** Run left halted as evidence.
4. **Run (live, unlinked)** — `e0e70f8b`: gpt-4o wrote plausible code at GUESSED
   paths outside every csproj — build trivially green, authored tests never compiled,
   coverage said covered (tests authored, not executed) — three green layers of
   theater; the live critic caught 2 real logic findings (F-H).
5. **REVISE-1 live** (first production outing) — cited both findings + written
   instruction → revision built green in 26s, `AddressedByRevision` recorded, gate
   re-halted on the new hash. The revision followed the logic instruction but ignored
   the path instruction (F-K). Fresh review then **RecommendBlock**: the ground-truth
   verifier compared the revised package against the FIRST halt announcement —
   confirmed product defect (F-I). Run left halted as evidence.
6. **Run (live, linked)** — ticket PATCHed with `LinkedFilePaths=src/ParcelTracker.Core/Parcel.cs`
   (form tickets cannot carry it: F-J; update verb is PATCH not PUT). Run `feb20b1a`:
   gpt-4o modified the REAL file correctly (Lost enum member, transitions from
   InTransit/OutForDelivery, terminal). Live critic: 2 findings — one factually wrong
   ("missing comma"; the compiler is ground truth) → **Reject** with reason; one fair
   (`IsTerminal` redundancy) → **FixInFollowUp** with reason.
7. **Approval ceremony** — first attempt refused `UNSUPPORTED_CHARACTERS`: evidence
   references allow only `[A-Za-z0-9-_.:]`, no spaces — while the shipped ApprovalGate
   UI sends `human-reason:${free text}` (F-L, real UI-vs-API contract bug, Playwright
   mocks hid it). Conformant retry: approval created, hash-bound.
8. **Continue** → `Completed` (approval consumed, verified).
9. **Apply** — attempt 1 blocked at `validate`: **NETSDK1004** — the spine rebuilds a
   fresh copy WITHOUT restore, so any normal .NET repo fails (F-M; the BookSeller
   `.assets` trick is required hidden knowledge — the exact alpha-blocker class,
   now proven on repo #2). Deviation recorded: scaffolding committed to ParcelTracker.
   Attempt 2 blocked at `prepare`: the failed attempt's preserved workspace occupies
   the deterministic `{runId}-apply` path — **a failed apply is unretryable through
   the product** (F-N; apply attempts are not attempt-scoped like repair/revise).
   Deviation recorded: failed workspace renamed aside, evidence preserved.
   Attempt 3: **`Applied`**.
10. **Final report** — `Applied`, `LoopComplete=true`, ZERO gaps, all five receipt
    files on disk. The applied repo builds; its original 4 tests pass.

## Findings ledger

| # | Area | Finding | Product remedy named? |
|---|---|---|---|
| F-A | preflight | Stale IronDev.Api from a prior day held port 5000 AND locked bin DLLs — later API starts died on MSB3021 buried in logs; nothing detects/names "another IronDev.Api instance is running (pid/port)" | No |
| F-B | preflight | Startup SqlCheck names two causes ("does not exist OR is not reachable") but its single remedy assumes nonexistence (create+migrate) — a stopped LocalDB instance sends the operator to create a database that exists | Wrong for one named cause |
| F-C | provisioning | POST profile/commands accepts EMPTY CommandText (wrong client field name bound to empty) and returns 200 OK — no validation refusal | No |
| F-C2 | provisioning | Once an empty command row exists, readiness stops proposing detected candidates — the wizard cannot re-propose; the candidate text is lost from remedies | No |
| F-D | provisioning | Confirms accumulate rows ALL isDefault=true (no upsert); default resolution reads the empty first row; NO delete/disable surface exists — no product path out of the poisoned state. Unblocked only by direct SQL (recorded deviation) | No |
| F-E | run start | Provisioning `isReady=true` ≠ run-ready: Builder readiness separately demands code index + AllowBuilderApply, unlisted by the wizard; refusal names actions but not endpoints | Partially |
| F-F | deterministic | Deterministic mode served the BookSeller fixture proposal for a ParcelTracker ticket; the stray file compiled trivially green and reached the gate. Coverage stayed honest (3/3 uncovered) so the gate catches it — but nothing refuses/warns "deterministic response set does not match this project" | No |
| F-G | remedies | Remedies name endpoints but not payload field names (CommandText); profile confirm differs before/after first confirm (proposedProfile vs GET /profile); ticket update is PATCH where PUT is expected | No |
| F-H | live unlinked | Without LinkedFilePaths, gpt-4o invents paths/architecture outside every csproj: build green, authored tests uncompiled, coverage "covered" (authored ≠ executed) — three honest-looking green layers; only critic + human reading the diff catch it. HERO-2's lesson reproduced on repo #2. No warning that proposed paths match no compiled project | No |
| F-I | REVISE-1 | **Product defect (confirmed in source):** `SkeletonCriticGroundTruthVerifier.CheckPackageHashAsync` takes the FIRST `CriticReviewPackageReady` announcement — after a green revision it blocking-mismatches forever. Same First-vs-Last class REVISE-1 fixed in report/drift, missed in the verifier. Fix queued | Verifier honestly names the mismatch — against a stale expectation |
| F-J | tickets | CreateProjectTicketRequest cannot carry LinkedFilePaths — the form path cannot express the single most reliability-critical field (chat path can); operator must PATCH the full entity | No |
| F-K | REVISE-1 | The live revision honored the logic instruction, ignored the explicit path instruction — one bounded revision could not correct a path-level miss; linked files on the ticket is the real fix (revision context may deserve linked-file hints) | n/a — model-behavior evidence |
| F-L | approval | **UI-vs-API contract bug:** evidence references allow only `[A-Za-z0-9-_.:]` (no spaces) but the shipped ApprovalGate sends `human-reason:${free text}` (WorkItemScreen.tsx:512) — every real UI approval with a written reason will be refused `UNSUPPORTED_CHARACTERS`. Proven only against Playwright mocks | Refusal names the field, not the allowed alphabet |
| F-M | apply | **Generalization wall:** the apply spine's `validate` stage rebuilds a fresh copy WITHOUT restore → NETSDK1004 on any normal .NET repo. The BookSeller `.assets` trick is required hidden repo knowledge. The spine should restore in the validation workspace (or the readiness/provisioning contract must own this requirement openly) | Evidence chain records why; remedy not named |
| F-N | apply | A failed apply is unretryable: the preserved failed workspace occupies the deterministic `{runId}-apply` path and `prepare` refuses; apply attempts are not attempt-scoped (unlike repair/revise). Operator had to move evidence aside manually | No |

## Refusal log

| Step | Surface | Reason code | Remedy named? |
|---|---|---|---|
| startup | SqlCheck | DemoStartupSqlUnavailable | yes, wrong for reachable-case (F-B) |
| run start | skeleton-runs | ReadinessBlocked (index + AllowBuilderApply) | actions yes, endpoints no (F-E) |
| det. gate | human | operator refusal — fixture proposal ≠ ticket | n/a — gate worked (F-F) |
| revised gate | fresh critic | RecommendBlock ground-truth hash-vs-halt | stale expectation (F-I) |
| approval | accepted-approvals | UNSUPPORTED_CHARACTERS EvidenceReferences[1] | field yes, alphabet no (F-L) |
| apply 1 | spine validate | SpineBlocked:validate (NETSDK1004) | evidence yes, remedy no (F-M) |
| apply 2 | spine prepare | SpineBlocked:prepare (workspace exists) | no (F-N) |

## Ceremony vs. walls (the viability measure)

Steady-state human ceremony for one governed change, once walls were known:
confirm profile/commands ~1 min · ticket shaping ~3 min · gate reading + revise
instruction ~3 min · dispositions with reasons ~3 min · approval ceremony ~2 min
→ **~12 human-minutes per governed change**, against a real diff, a real adversarial
review, hash-bound approval, and a receipt chain. That price is defensible.

Wall time (not ceremony): ~2.5 hours of operator archaeology across F-A→F-N.
Every hour of it is product backlog, not process cost.

## Exit criteria (runbook §4)

- Second repo imported/provisioned through product surfaces only — YES (one recorded SQL deviation, F-D).
- Build/test configuration detected or asked, never assumed — YES.
- Ticket created; governed run started; halted at the human gate — YES (three times, honestly).
- Critic/finding/approval requirements visible and satisfiable — YES (with F-L workaround).
- Apply only through the backend-governed path — YES: refused twice with evidence, then Applied.
- Final report reconstructs without hiding gaps — YES: LoopComplete=true, gaps=[].
- Ceremony cost recorded — YES (above).

## Recommended next slices (from findings, in order)

1. FIX F-I (verifier last-halt) — REVISE-1 is broken-after-revision until then.
2. FIX F-L (approval ceremony evidence alphabet vs UI free-text reason) — the cockpit's
   marquee ceremony fails against the real backend.
3. F-M: restore in the apply-validate workspace — THE generalization blocker.
4. F-C/F-C2/F-D: command validation + upsert + delete surface — the wizard must not be poisonable.
5. F-E: one readiness truth (provisioning readiness must include Builder requirements).
6. F-J: LinkedFilePaths on the ticket form + surface it as a first-class readiness hint (F-H).
7. F-N: attempt-scoped apply workspaces.
8. F-F: deterministic mode refuses non-matching projects by name.
9. F-A/F-B: preflight detects rogue API instances; SqlCheck wakes/waits LocalDB before prescribing creation.

## Boundary

This cycle is evidence, not approval. Applying one change to a discardable second
repo proves the loop generalizes mechanically; it is not reliability, release
readiness, or a substitute for the non-author walk. The parcel does not care that
its tracker is governed — but the humans reviewing the next thirteen walls will.

## Killjoy line

The loop generalized. The scaffolding did not. Until a fresh repo passes validate
without secret handshakes, "bring your own repository" is an aspiration with receipts.
