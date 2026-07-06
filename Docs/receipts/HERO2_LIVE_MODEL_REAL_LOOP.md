# HERO-2 Receipt - The First Fully Real Governed Loop

## Purpose

Get rid of the fake shit.

Every prior proof of the governed loop substituted at least one fake: a deterministic
builder, an empty test author, a clean canned critic. HERO-2 runs the loop with
**zero test-double registrations**: a live model writes the proposal through the real
Builder service, the real Tester authors tests from acceptance criteria, the real
disposable workspace builds and tests, the live critic reviews the real package with
ground-truth verification, every real finding is dispositioned through the product
route, and the same hash-bound approval / continuation / controlled apply spine
reaches `Applied`.

## What actually happened (executed 2026-07-06, local)

```text
Provider/model:        OpenAI/gpt-4o (builder, tester, critic — via AgentLlmResolver)
Journey:               project create -> profile detect/save -> code index (12 files)
                       -> bulk-discount ticket -> linked file set (ticket editor path)
                       -> live run -> real dotnet build/test GREEN -> PausedForApproval
Critic verdict:        RequestChanges — TWO REAL FINDINGS on the live builder's code:
                       - High:   Incorrect Exception Message for Zero Quantity
                       - Medium: Potential Misleading Logic for Zero-Priced Books
Dispositions:          both AcceptRisk with reasons, through POST .../findings/{id}/disposition
Approval/continue/apply: hash-bound accepted approval -> Completed -> Applied
Final report:          LoopComplete = true, zero gaps
```

The adversarial tension the architecture was designed for — the critic catching the
builder — happened on the first real run, unscripted.

## Four real defects the fakes had been hiding

Each was found because the fakes were removed; each is fixed or durably recorded.

### 1. The live critic route could never resolve (product bug — FIXED)

`IManualIndependentCriticAgentService` was registered nowhere, so
`StoredManualIndependentCriticAgentService` (which the real
`SkeletonCriticReviewService` depends on) could not be constructed in any real host.
The live critic endpoint had never once been callable. Fixed in `IronDev.Api/Program.cs`;
pinned by contract test.

### 2. Test-host schema never covered the real path (test infra — FIXED)

`ApiTestBase` provisioning omitted `migrate_project_profiles.sql`,
`migrate_code_indexing.sql`, `migrate_projects_indexing_fields.sql`, and
`migrate_agent_run_audit_envelope.sql` — because no test had ever exercised the real
profile/index/critic-audit path. Added. Additionally, the hero walk provisions the
critic audit table through the app's OWN `IDbConnectionFactory`, so the runtime
database is provisioned no matter how configuration resolves.

### 3. Destructive test provisioning targeted the REAL database (safety bug — FIXED)

The test-base resolved its destructive provisioning/reset connection from the composed
app configuration, which was observed resolving to the REAL `IronDeveloper` database —
meaning `DropGovernanceSql` and domain-data resets could run against real data.
Fixed: the destructive connection is pinned to the explicit test connection string and
hard-guarded to a catalog name ending in `_Test` (refusal otherwise). Pinned by
contract test.

**OWNER-ACTION-001 — assess real IronDeveloper DB governance-table loss risk.**
The real `IronDeveloper` database currently has NO governance tables, consistent
with historical test runs having dropped them (`DropGovernanceSql` drops without
re-creating when a later step fails or targets another catalog). Owner must
explicitly assess whether governance records of value were lost, and record the
verdict (nothing-of-value / restored-from-backup / accepted-loss) before this
action is closed. Not a merge blocker if this is a disposable local dev DB — but
it is a named action, not a buried note.

Guard scope (review-hardened): destructive provisioning targets only explicitly
test-shaped catalogs — `*_Test` locally, `IronDev_CI_*` ephemeral in CI. Real
(`IronDeveloper`), local developer (`IronDeveloper_Local`), and empty catalogs are
refused. Contract coverage: `ApiTestBaseCatalogGuardContractTests` (4 tests),
executed by name in the full SQL lane.

**Open question (named, not solved):** why the composed configuration resolved to the
Development connection inside the base test factory in some sessions and the Test
connection in others was not fully root-caused; the pin makes the answer irrelevant
for safety, but the config-precedence behavior deserves its own investigation.

### 4. Prose TechnicalNotes silently defeat context hints (product gap — RECORDED)

`BuilderContextService` extracts file-path hints by splitting on newline/comma/semicolon.
A prose sentence containing a path ("Extend PriceFor in src/.../PricingService.cs. The
10-copy...") becomes one giant non-path token, `File.Exists` fails, and the content is
**silently** skipped — the live model then invents the file (observed twice: gpt-4o-mini
and gpt-4o both hallucinated `PricingService` and broke the build). The designed
mechanism — `LinkedFilePaths`, set by chat drafts and the UI ticket editor — works and
is what the hero walk uses. The silent context loss is a follow-up issue: context
assembly should name what it could not load, never fly blind quietly.

## Boundaries

- Live model output is proposed work, never authority: the model cannot approve,
  disposition, continue, or apply. The explicit human-shaped acts run through product routes.
- The live walk never falls back to deterministic mode: without the explicit opt-in
  envs it is Inconclusive; failures are named failures.
- One live pass proves the loop is real, not that the model is reliable.
- Green tests here are evidence, not release readiness.

## How to run it

```powershell
$env:IRONDEV_ALPHA_SMOKE_LIVE_MODEL = "1"
$env:IRONDEV_ALPHA_SMOKE_LIVE_PROVIDER = "OpenAI"     # or LocalOpenAI / Ollama / Custom
$env:IRONDEV_ALPHA_SMOKE_LIVE_MODEL_NAME = "gpt-4o"
# key from OPENAI_API_KEY or IRONDEV_ALPHA_SMOKE_LIVE_API_KEY
dotnet test IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj `
  --filter "FullyQualifiedName~LiveModelHeroWalkTests"
```

Not in any CI execution lane by design (CI holds no model credentials); registered in
the slow/quarantine register as ManualLocal.

## Files Changed

- `IronDev.Api/Program.cs` — missing critic DI registration (defect 1)
- `IronDev.IntegrationTests.Api/Smoke/LiveModelHeroWalkTests.cs` — the live walk (new)
- `IronDev.IntegrationTests.Api/ApiTestBase.cs` — real-path migrations + destructive-connection pin/guard (defects 2, 3)
- `IronDev.IntegrationTests/Demo/DemoSeedScriptContractTests.cs` — pins the fixes
- `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md` — register row
- `Docs/receipts/HERO2_LIVE_MODEL_REAL_LOOP.md`

## Validation

- Live walk executed locally against real SQL + OpenAI/gpt-4o: **passed (52-56s)**, including a
  clean-slate rerun after dropping the critic audit table (self-provisioning proven).
- Model-quality reality recorded: gpt-4o-mini failed the walk (hallucinated file, build red)
  — the run halted with preserved evidence and granted nothing. Failure UX worked.
- Regression with the safety pin: `DemoSeedApiDrivenTests` 3/3, `AlphaSmokeApiPersistenceTests` 8/8.
- Contract suite: `DemoSeedScriptContractTests` all passing including the new pin contract.

## Known Limits

- Live walk is single-model, single-ticket, opt-in local proof.
- Chat completion and the UI click-path for the live walk remain separate.
- Bounded repair does not exist yet: a red live build is a dead end with evidence, not a recovery path.

## Review Line

Removing the fakes found four real defects in one afternoon. That is what the fakes were costing.

## Killjoy Line

A loop that has only ever run on canned answers is a diagram. This one has now run for real — once. Once is proof of life, not reliability.
