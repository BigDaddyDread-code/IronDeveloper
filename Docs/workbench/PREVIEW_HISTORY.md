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

Start the current PR-02B preview alongside the earlier previews:

```powershell
.\tools\localtest\start-pr-manual-test.ps1 -FreshSession -BrowserOnly -Reset `
  -PreviewId workbench-pr02b -ApiBaseUrl http://127.0.0.1:5230 -UiPort 5211
```

The PR-02B preview owns:

- database `IronDeveloper_Test_workbench_pr02b`;
- workspace `C:\IronDevTestWorkspaces\workbench-pr02b`;
- logs `C:\IronDevTestLogs\workbench-pr02b`;
- API `http://127.0.0.1:5230`;
- UI `http://127.0.0.1:5211`.

Project creation remains repository-independent. PR-02B executes durable BA turns through the existing Analyst role with no agent-owned memory and no repository, filesystem, Builder, or write tools. The existing Workshop conversation UI stays on its compatibility path until the later conversation slice wires submission and polling.

Run `tools/localtest/test-workbench-ba-host.ps1` against the preview for the backend BA proof. It returns a follow-up command that reopens the same project and chat after a host restart, demonstrating that the new host reconstructs each turn from durable server-owned state.
