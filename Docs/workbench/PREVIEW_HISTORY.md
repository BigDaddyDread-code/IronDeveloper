# IronDev Workbench preview history

The machine-readable current version is `workbench-version.json`. Each preview uses its own preview ID, database, workspace, logs, API port, and UI port so it can be tested without changing another preview.

| Version | Programme slice | Preview ID | User-test focus |
|---|---|---|---|
| `0.1.0-preview.1` | PR-00A | `workbench-pr00a` | Version identity, isolated preview data, reset, V1 fallback |
| `0.1.0-preview.2` | PR-01 initial review build | `workbench-pr01` | Project-first creation, atomic initial Workbench state, direct shaping entry |
| `0.1.0-preview.3` | PR-01 review correction | `workbench-pr01` | Normative vocabulary, clean startup, explicit open/takeover, immutable replay, and write-lease fencing |
| `0.1.0-preview.4` | PR-01 acceptance correction | `workbench-pr01` | Legacy-route membership enforcement, mutation-driven lease renewal, and replayable project-open retries |
| `0.1.0-preview.5` | PR-02A | `workbench-pr02a` | Durable BA run submission, immutable server context, retry claims, exactly-once materialization, cancellation, and takeover supersession |

Start the current PR-02A preview alongside the PR-01 preview:

```powershell
.\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset `
  -PreviewId workbench-pr02a -ApiBaseUrl http://127.0.0.1:5220 -UiPort 5201
```

The PR-02A preview owns:

- database `IronDeveloper_Test_workbench_pr02a`;
- workspace `C:\IronDevTestWorkspaces\workbench-pr02a`;
- logs `C:\IronDevTestLogs\workbench-pr02a`;
- API `http://127.0.0.1:5220`;
- UI `http://127.0.0.1:5201`.

Project creation remains repository-independent. PR-02A adds the backend authority for durable BA turns; the existing Workshop conversation UI stays on its compatibility path until the later conversation slice wires submission and polling.
