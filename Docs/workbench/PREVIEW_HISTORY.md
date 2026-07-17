# IronDev Workbench preview history

The machine-readable current version is `workbench-version.json`. Each preview uses its own preview ID, database, workspace, logs, API port, and UI port so it can be tested without changing another preview.

| Version | Programme slice | Preview ID | User-test focus |
|---|---|---|---|
| `0.1.0-preview.1` | PR-00A | `workbench-pr00a` | Version identity, isolated preview data, reset, V1 fallback |
| `0.1.0-preview.2` | PR-01 | `workbench-pr01` | Project-first creation, atomic initial Workbench state, direct shaping entry |

Start preview 2 alongside preview 1:

```powershell
.\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset `
  -PreviewId workbench-pr01 -ApiBaseUrl http://127.0.0.1:5210 -UiPort 5191
```

Preview 2 owns:

- database `IronDeveloper_Test_workbench_pr01`;
- workspace `C:\IronDevTestWorkspaces\workbench-pr01`;
- logs `C:\IronDevTestLogs\workbench-pr01`;
- API `http://127.0.0.1:5210`;
- UI `http://127.0.0.1:5191`.

Project creation in preview 2 does not create anything in its workspace. The workspace remains reserved for the later, separately confirmed Repository Setup workflow.
