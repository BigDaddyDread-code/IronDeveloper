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

Start the current PR-02C-A preview alongside the earlier previews:

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

Project creation remains repository-independent. In this preview, Workshop checks project-scoped BA readiness before creating the first conversation, creates that session with an atomic lease fence and durable operation, submits a fenced durable run, reports queued/running/terminal state, can cancel the active run, recovers active or terminal state after reload, and renders only backend-persisted messages. Ambiguous create or submit delivery retains the exact operation receipt, disables conversation navigation and altered sends, and permits only the unchanged authoritative replay. The browser does not call legacy completion or direct message writes while conversation authority is active. One Workbench session remains bound to one direct conversation; this preview has no close/new-session action. Document attachments, structured BA draft metadata, durable understanding updates, and `/ticket` formalization remain outside PR-02C-A.

The lower-level backend continuity proof remains available:

```powershell
.\tools\localtest\test-workbench-ba-host.ps1 `
  -ApiBaseUrl http://127.0.0.1:5240 -PreviewId workbench-pr02c-a
```

It returns a follow-up command that reopens the same project and chat after a host restart, demonstrating that the host reconstructs each turn from durable server-owned state rather than provider-side conversation memory.
