# DEMO-REHEARSAL-001

## Executive Verdict

Verdict: `Blocked`

Reason: the full demo path now executes end-to-end from a fresh demo database on
the author's machine (author dry-run below, all evidence real), but the record
demands a NON-AUTHOR operator on a fresh checkout. That single blocker remains.

## Author Dry-Run (2026-07-07 — evidence real, non-author still required)

The rehearsal command list was executed exactly as written. The first pass hit
fifteen distinct walls; every one was fixed or durably recorded (findings ledger
below), and the pass was repeated until the whole path ran clean.

### Commit And Environment

- Starting commit SHA: `cc9e41f62ec8561fee6bf935352279804362da56` (fixes landed on `demo/demo-rehearsal-001-author-dryrun`)
- OS/shell: Windows 11 Pro 10.0.26200 / Windows PowerShell 5.1
- .NET / Node / Git: present (ToolchainCheck Passed)
- SQL: `(localdb)\MSSQLLocalDB`, database `IronDeveloper_Local` created FRESH via
  `sql-local.ps1 -Create -ApplyLocalDevSetup` + `apply-migrations.ps1`
- Weaviate: `NotRequiredForDeterministicDemoPath`
- Root safety: Passed at every stage
- Model mode: `DeterministicOnlyLocalAlphaPreview` (explicit alpha-smoke provider pin; live is never silent)

### Final Clean Pass

```text
Scripts/demo/start-v0.1-demo.ps1  ->  exit 0, ALL stages Passed:
RootSafety/Toolchain/ModelMode/ApiUrl/UiUrl/Sql/JwtKey/ApiDatabasePin/
DeterministicModelPin/SkeletonApplyPin/ApiCheck(started)/UiCheck(started)/
DemoSeedCheck(Passed)
UI: http://127.0.0.1:5173 -> HTTP 200
```

### Product Evidence (real, from the seed receipt)

- Project ID: `2` (BookSeller, disposable source copy under the demo output root)
- Applied ticket: id `1` (validate-book), run `84c36048-30f0-491f-bdc8-5c3db7c3cb62`, state `Applied`
- Critic package hash: `afd9ff354107d504440a474580621433cf102a702dd0ecce52dc1a34ff1b0144`
- Accepted approval ID: `e02027ef-3b88-42b5-9f4d-4ebd82bb6bcc` (hash-bound, consumed by continuation)
- Paused ticket: id `2` (search-by-author), run `0bf13d81-c488-450c-b97d-af6d29f7ee01`, state `PausedForApproval`, no approval — awaiting a human
- Receipt: `<user-home>\AppData\Local\IronDev\v0.1-demo\BookSeller\demo-seed-receipt.json`

## Findings Ledger (author dry-run)

Fixed in this slice (each has its own commit):

1. Startup hid managed-process output — API/UI now log to files the blocked stage names.
2. API dies without a JWT signing key and the death was invisible — JwtKeyCheck stage: operator key if set, else an ephemeral session-local key, stated openly.
3. The demo API silently pointed at the REAL `IronDeveloper` catalog — ApiDatabasePin pins the connection to `-DatabaseName`.
4. Startup's seed invocation had no credentials — seed defaults to the documented local-dev user from `local_dev_setup.sql` (env overrides win).
5. `local_dev_setup.sql` seeded ProjectRules with hardcoded ids BEFORE the tenant/project existed — fresh databases always failed; reordered and id-resolved.
6. `sql-local.ps1` swallowed all sqlcmd output — failures now surface their last lines.
7. `demo-seed` fresh-copy path leaked restore output into its return value — crash on every fresh machine; output captured, surfaced on failure only.
8. `demo-seed` shadowed PowerShell's automatic `$matches` variable — corrupted project/ticket resolution; renamed.
9. Windows PowerShell 5.1 nests top-level JSON array responses — `Invoke-DemoApi` list callers got one nested element once a second row existed; enumerated.
10. `migrations.json` omitted every real-path migration (profiles, code index, indexing fields, agent audit) — a fresh database could never start a governed run; added.
11. The seed skipped the real first-run journey — profile detect/save, build/test commands, code index now run through product routes before tickets.
12. Deterministic mode never armed the explicit alpha-smoke provider or its response set — DeterministicModelPin sets both; `{}` responses were `ProposalEmpty`.
13. The Tester fixture authored tests with `Assert.ThrowsExactly`, absent from the sample's MSTest — every deterministic build failed; fixed to `ThrowsException`.
14. Skeleton apply was unarmed for the demo's disposable copy — SkeletonApplyPin arms the sandbox case; the governed gate chain is unchanged.
15. An uncomparable stored project path crashed the seed — now a named `DemoIdempotencyConflict` block with redacted values.

Residuals — addressed in the follow-up slice (R1-R4, validated by execution):

- R1 FIXED. Startup `SqlCheck` now probes database EXISTENCE after name-safety; a
  missing database blocks at startup with the exact remedy (create + migrations
  commands), validated against both a missing and a real database.
- R2 FIXED. `apply-migrations.ps1` default builder no longer forces Encrypt
  (legacy SqlClient cannot encrypt to LocalDB); `-Server/-Database` now works
  locally — validated. `-TrustServerCertificate` opts into encryption; fully
  custom needs use `-ConnectionString`.
- R3 FIXED. The seed's `DemoIdempotencyConflict` refusal (still deliberate —
  local demo source is never overwritten silently) now NAMES its remedy: verify,
  delete the stale copy, rerun. Contract-pinned.
- R4 FIXED. The startup's seed-blocked next-safe-action now names the stale-copy
  cleanup and the rerun, so a mid-seed failure has a written path back.

Note: the runbook command list below was updated for R2 — `apply-migrations.ps1`
now works with plain `-Server/-Database` locally.

## Required Demo Commands (validated in this dry-run)

1. `git rev-parse HEAD`
2. `Scripts/local/doctor-local.ps1 -CheckOnly -Json`
3. `Scripts/local/sql-local.ps1 -Create -ApplyLocalDevSetup -DatabaseName IronDeveloper_Local` (fresh machine)
4. `Database/apply-migrations.ps1 -Server "(localdb)\MSSQLLocalDB" -Database IronDeveloper_Local` (fresh machine)
5. `Scripts/demo/start-v0.1-demo.ps1` (starts SQL check, JWT/db/model/apply pins, API, UI, seed)
6. Open `http://127.0.0.1:5173`
7. Walk the BookSeller ticket/run/report path; the seed receipt carries the backend evidence refs.

## Blockers

- `NonAuthorOperatorMissing` — the only remaining blocker. Everything else in the
  original blocker list has been executed with real evidence.

## Manual Fixes Required Of The Non-Author

- None known. If any step demands knowledge not written above, that is a new
  finding and belongs in this ledger.

## Repeatability Verdict

`Blocked` — on the single named blocker. The author path is proven repeatable
(final pass ran clean twice: seed pass + full startup verification pass).

## Boundary

This rehearsal transcript is evidence only. It is not approval, policy
satisfaction, workflow continuation, source apply authority, release readiness,
deployment readiness, live-model proof, or permission to publish.

## Review Line

One non-author rehearsal can prove repeatability. The author dry-run's job was
to make that rehearsal boring — fifteen walls are now fifteen commits.

## Killjoy

If the demo needs the author in the room, it is not yet a demo. The author has
now left everything they know in this file.
