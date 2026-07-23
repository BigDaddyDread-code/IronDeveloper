# IronDev Workbench preview history

The machine-readable current version is `workbench-version.json`. Each preview uses its own preview ID, database, workspace, logs, API port, and UI port so it can be tested without changing another preview.

| Version | Programme slice | Preview ID | User-test focus |
|---|---|---|---|
| `0.1.0-preview.1` | PR-00A | `workbench-pr00a` | Version identity, isolated preview data, reset, V1 fallback |
| `0.1.0-preview.2` | PR-01 initial review build | `workbench-pr01` | Project-first creation, atomic initial Workbench state, direct shaping entry |
| `0.1.0-preview.3` | PR-01 review correction | `workbench-pr01` | Normative vocabulary, clean startup, explicit open/takeover, immutable replay, and write-lease fencing |
| `0.1.0-preview.4` | PR-01 acceptance correction | `workbench-pr01` | Legacy-route membership enforcement, mutation-driven lease renewal, and replayable project-open retries |
| `0.1.0-preview.5` | PR-02A | `workbench-pr02a` | Durable BA run submission, immutable server context, retry claims, exactly-once materialization, cancellation, and takeover supersession |
| `0.1.0-preview.6` | PR-02B | `workbench-pr02b` | Stateless Analyst host, executable version contracts, bounded read-only project tools, pre-invocation fencing, provider timeout, and fair recovery |
| `0.1.0-preview.7` | PR-02C-A | `workbench-pr02c-a` | Workshop AgentRun authority, role-preserving provider requests, aggregate input/output budgets, pre-write readiness, active/terminal recovery, cancellation, and idempotent project-scoped chat entry |
| `0.1.0-preview.8` | PR-02C-B | `workbench-pr02c-b` | Typed Project Understanding facts, provenance, locks, conflicts, explicit rename acceptance, and authority-labelled operational projections |
| `0.1.0-preview.9` | PR-03 | `workbench-pr03` | Deterministic `/help` and `/ticket`, exact allowlist parsing, hash-only rejection audit, and zero-AgentRun help/typo handling |
| `0.1.0-preview.10` | PR-04A | `workbench-pr04a` | Governed `/ticket` generation, durable proposal review, provenance, immutable revisions, fenced edits, and regeneration without permanent tickets |
| `0.1.0-preview.11` | PR-04B | `workbench-pr04b` | Explicit atomic creation of permanent tickets, Work Item contracts, dependency remapping, provenance receipts, and the Delivery transition |
| `0.1.0-preview.12` | PR-05A | `workbench-pr05a` | Repository/profile authorities, deterministic setup-plan review, approved-root safety, explicit confirmation, and unsupported-profile reporting with zero filesystem writes |
| `0.1.0-preview.13` | PR-05B | `workbench-pr05b` | Confirmed-plan provisioning through isolated staging, pinned product-neutral rendering, controlled Git initialization, atomic install, crash-safe replay, and no technical-readiness claims |
| `0.1.0-preview.14` | PR-06A | `workbench-pr06a` | Fail-closed production sandbox capability, Hyper-V isolation policy, digest/feed verification, bounded qualification evidence, and cleanup/recovery without readiness or Builder authority |
| `0.1.0-preview.15` | PR-06B | `workbench-pr06b` | Immutable repository observations, restore/build/test records, code-index snapshots, exact evidence currentness, nine-gate readiness, and provider-availability separation |

Start the current PR-06B preview alongside the earlier previews:

```powershell
.\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset `
  -EnableConversationAuthority -PreviewId workbench-pr06b `
  -ApiBaseUrl http://127.0.0.1:5410 -UiPort 5391
```

The PR-06B preview owns database `IronDeveloper_Test_workbench_pr06b`, workspace `C:\IronDevTestWorkspaces\workbench-pr06b`, logs `C:\IronDevTestLogs\workbench-pr06b`, API `http://127.0.0.1:5410`, and UI `http://127.0.0.1:5391`.

PR-06B materializes immutable repository observations, restore/build/test validation records, and a file-level code-index snapshot only from the server-owned qualified repository and exact PR-06A evidence. Readiness is calculated from nine explicit gates by comparing current repository, profile, command, toolchain, image, feed, template, sandbox-policy, observation, index, and Builder-model configuration authority. A newer timestamp cannot rescue stale evidence. No browser path or command becomes authority, and the legacy project indexing fields receive only a one-way compatibility projection. Live provider availability is reported separately and cannot change durable `Ready` state. Readiness does not create or grant Builder authorization; PR-07 owns one exact, hash-bound, single-use Builder start.

The earlier PR-06A preview remains available for sandbox-boundary comparison:

```powershell
.\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset `
  -EnableConversationAuthority -PreviewId workbench-pr06a `
  -ApiBaseUrl http://127.0.0.1:5390 -UiPort 5371
```

The PR-06A preview owns database `IronDeveloper_Test_workbench_pr06a`, workspace `C:\IronDevTestWorkspaces\workbench-pr06a`, logs `C:\IronDevTestLogs\workbench-pr06a`, API `http://127.0.0.1:5390`, and UI `http://127.0.0.1:5371`.

Project creation, shaping, `/ticket`, and proposal review remain repository-independent. PR-06A extends the Repository surface only after a qualified repository and exact execution-profile authority exist. The production sandbox is unavailable unless the host can prove Windows/x64 HCS access, a digest-pinned Windows SDK image, the content-addressed read-only offline feed, and the exact versioned resource policy. It never falls back to host execution. The v0.1 process guarantee is 64 untrusted project-workload processes, enforced by a versioned and hash-bound Windows Job supervisor that creates each project process suspended and assigns it before resume. Trusted HCS, bootstrap, and supervisor processes are outside that workload count; the preview does not claim a Docker whole-silo PID limit. Failure to prove the exact Job flags, suspended assignment, restricted workload identity, or fixed broker-denial checks makes sandbox capability unavailable before project bytes are read. Qualification evidence is bound to the repository baseline, binding/profile revisions, image/feed/template/toolchain/policy/supervisor hashes, actual isolation and workload-limit inspection, stage results, bounded copied artifacts, and confirmed teardown. An ambiguous retry or service restart may materialize the exact completed evidence or repeat exact cleanup, but it never reruns a pending qualification; cleanup that cannot be proven remains visibly `Running` and fenced for later recovery. Passing evidence does not change execution readiness or grant Builder authorization; PR-06B owns mechanically current validation/readiness.

For the deterministic LocalTest rename path, send `Rename project to CalmPlan` as its own Workshop message, then accept the pending proposal in Project Context.

The earlier PR-02C-A preview remains available for comparison:

```powershell
.\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset `
  -EnableConversationAuthority -PreviewId workbench-pr02c-a `
  -ApiBaseUrl http://127.0.0.1:5240 -UiPort 5221
```

The PR-02C-A preview owns:

- database `IronDeveloper_Test_workbench_pr02c_a`;
- workspace `C:\IronDevTestWorkspaces\workbench-pr02c-a`;
- logs `C:\IronDevTestLogs\workbench-pr02c-a`;
- API `http://127.0.0.1:5240`;
- UI `http://127.0.0.1:5221`.

Project creation remains repository-independent. In this preview, Workshop checks project-scoped BA readiness before creating the first conversation, creates that session with an atomic lease fence and durable operation, submits a fenced durable run, reports queued/running/terminal state, can cancel the active run, recovers active or terminal state after reload, and renders only backend-persisted messages. Ambiguous create, submit, or cancellation delivery—including generic/malformed HTTP `5xx` and invalid success envelopes—retains the exact operation receipt and normalized payload, disables altered work and conversation navigation, and permits only exact authoritative replay. The browser does not call legacy completion or direct message writes while conversation authority is active. One Workbench session remains bound to one direct conversation; this preview has no close/new-session action. Document attachments, structured BA draft metadata, durable understanding updates, and `/ticket` formalization remain outside PR-02C-A. Model-specific capability evidence plus a context compaction/session-rollover corridor are hard gates before real-provider or default-on use.

The lower-level backend continuity proof remains available:

```powershell
.\tools\localtest\test-workbench-ba-host.ps1 `
  -ApiBaseUrl http://127.0.0.1:5240 -PreviewId workbench-pr02c-a
```

It returns a follow-up command that reopens the same project and chat after a host restart, demonstrating that the host reconstructs each turn from durable server-owned state rather than provider-side conversation memory.
