# CLI Command Inventory

Last reviewed: 2026-05-26

This inventory classifies the current public CLI and ReplayRunner/dogfood command surface. The command surface is intentionally bluntly described: `tools/IronDev.ReplayRunner` is large and useful internally, but it is not a clean product CLI.

## Summary

| Classification | Count | Notes |
|---|---:|---|
| Product | 8 | All are in `tools/IronDev.Cli`; all call API routes through `IIronDevApiClient`. |
| Internal Dogfood | 50 | ReplayRunner commands for governed agents, campaigns, docs, memory diagnostics, promotion, and internal review. |
| Smoke Test | 22 | ReplayRunner smoke commands. |
| Replay/Test Harness | 4 | Replay plan and test plan execution commands. |
| Deprecated | 0 | No command is explicitly deprecated in the current inventory. |
| To Be Moved | 0 | Several commands are product-shaped but remain classified internal until a split/refactor ticket moves them. |
| **Total** | **84** | 8 product CLI commands + 76 ReplayRunner/dogfood commands. |

## Product CLI: `tools/IronDev.Cli`

These commands are product-intended and now route through `IronDev.Client` via `IIronDevApiClient`.

| Command name | Purpose | Uses `IronDev.Client` | Bypasses API | Reads/writes local files directly | Calls Infrastructure directly | Classification | Recommended future home |
|---|---|---|---|---|---|---|---|
| `irondev ticket create --project-id <id> --file <ticket.json>` | Create an IronDev ticket from local JSON. | Yes | No | Reads JSON input file | No | Product | `IronDev.Cli` |
| `irondev ticket list --project-id <id>` | List project tickets. | Yes | No | No | No | Product | `IronDev.Cli` |
| `irondev ticket show --project-id <id> --ticket-id <id>` | Show one ticket. | Yes | No | No | No | Product | `IronDev.Cli` |
| `irondev ticket import-github-issue --project-id <id> --file <github-issue.json>` | Import external issue JSON as an IronDev ticket. | Yes | No | Reads JSON input file | No | Product | `IronDev.Cli` |
| `irondev tickets build --project-id <id> --ticket-id <id>` | Start a product ticket build workflow run. | Yes | No | No | No | Product | `IronDev.Cli` |
| `irondev runs status --run-id <id>` | Show product-shaped run status. | Yes | No | No | No | Product | `IronDev.Cli` |
| `irondev runs report --run-id <id>` | Show product-shaped final run report. | Yes | No | No | No | Product | `IronDev.Cli` |
| `irondev runs stream --run-id <id>` | Stream product-shaped run events. | Yes | No | No | No | Product | `IronDev.Cli` |

Missing product CLI commands from the intended surface:

| Intended command | Current state | Recommended future home |
|---|---|---|
| `irondev projects list` | Missing | `IronDev.Cli` via `IronDev.Client` |
| `irondev projects create` | Missing | `IronDev.Cli` via `IronDev.Client` |
| `irondev tickets generate` | Missing | `IronDev.Cli` via `IronDev.Client` |
| `irondev tickets build` | Implemented over `/api/projects/{projectId}/tickets/{ticketId}/build-runs`; workflow persistence is still planned | `IronDev.Cli` |
| `irondev documents list` | Missing | `IronDev.Cli` via `IronDev.Client` |
| `irondev documents create` | Missing | `IronDev.Cli` via `IronDev.Client` |
| `irondev documents version` | Missing | `IronDev.Cli` via `IronDev.Client` |
| `irondev documents generate-tickets` | Missing API/client route | `IronDev.Cli` after product route exists |
| `irondev memory search` | Missing product CLI command | `IronDev.Cli`; current `memory search` exists only in ReplayRunner |
| `irondev runs status` | Implemented over `/api/runs/{runId}` | `IronDev.Cli` |
| `irondev runs report` | Implemented over `/api/runs/{runId}/report` | `IronDev.Cli` |
| `irondev runs stream` | Implemented over `/api/runs/{runId}/events`; streams live in-memory events when available and report snapshots for legacy report-only runs | `IronDev.Cli` |

## ReplayRunner/Dogfood Commands

Default values for this table:

- Uses `IronDev.Client`: No.
- Bypasses API: Yes or mixed; these are internal harness commands and may call internal services, files, SQL, Weaviate, or subprocesses.
- Reads/writes local files directly: Yes or mixed for most command families.
- Calls Infrastructure directly: Allowed only because the recommended home is internal.

| Command name | Purpose | Classification | Recommended future home |
|---|---|---|---|
| `campaign adversarial-memory-agents-183` | Adversarial/proposal-only memory agent campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent doubt review` | Adversarial plan/package review. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent memory-improvement propose` | Proposal-only memory improvement agent. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent loop plan-review` | Governed Planner/Critic loop. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `campaign governed-tool-loop-162-167` | Governed tool loop campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `campaign loop-gated-disposable-build-168` | Loop-gated disposable build campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `campaign promotion-package-169` | Promotion package campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `campaign isolated-promotion-apply-170` | Isolated promotion apply campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `campaign controlled-write-policy-173` | Controlled write policy campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `campaign controlled-write-approval-174` | Controlled write approval campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `campaign controlled-worktree-dry-run-175` | Controlled worktree dry-run campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `campaign live-remaining-agents-161` | Live remaining governed agents campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `campaign live-retriever-sentinel-160` | Live retriever/sentinel campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `campaign live-critic-planner-159` | Live critic/planner campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `campaign live-governed-agent-158` | Live governed agent execution campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent builder repair-loop` | Builder repair loop. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent builder trace-smoke` | Builder trace smoke. | Smoke Test | `IronDev.DogfoodRunner` |
| `agent architect review` | Architect review agent. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent conscience review` | Conscience review agent. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent critic review-failure` | Critic review of failure package. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent list` | List internal agents. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent planner draft-test-plan` | Planner draft test plan agent. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent planner intake-product-spike` | Planner intake/product spike agent. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent profiles` | List internal agent profiles. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent quality run-gate` | Quality gate agent. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent research package` | Research package agent. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent retriever search` | Retriever search over memory. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent sentinel observe` | Sentinel observation agent. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent supervisor run-goal` | Supervisor goal runner. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `agent tester run-plan` | Execute test plan runner through agent command. | Replay/Test Harness | `IronDev.ReplayRunner` |
| `agent thought-ledger explain` | Explain thought ledger. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `build disposable repair` | Disposable builder repair alias. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `build disposable run` | Disposable build alias. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `builder disposable-workspace-apply-smoke` | Disposable workspace apply smoke. | Smoke Test | `IronDev.DogfoodRunner` |
| `builder proposal-safety-smoke` | Builder proposal safety smoke. | Smoke Test | `IronDev.DogfoodRunner` |
| `builder solitaire-disposable-build-smoke` | Solitaire disposable build smoke. | Smoke Test | `IronDev.DogfoodRunner` |
| `chat send` | Internal chat send harness. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `docs clean` | Clean internal docs data. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `docs discussion-smoke` | Discussion/document smoke. | Smoke Test | `IronDev.DogfoodRunner` |
| `docs import` | Import internal docs data. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `docs list` | List internal docs data. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `docs search` | Search internal docs data. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `docs show` | Show internal docs data. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `dogfood build disposable-apply-smoke` | Dogfood disposable apply smoke alias. | Smoke Test | `IronDev.DogfoodRunner` |
| `dogfood build solitaire-disposable-build-smoke` | Dogfood disposable build smoke alias. | Smoke Test | `IronDev.DogfoodRunner` |
| `dogfood foundation break-test` | Foundation break-test alias. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `dogfood memory builder-context-source-smoke` | Memory builder context source smoke alias. | Smoke Test | `IronDev.DogfoodRunner` |
| `dogfood memory cross-project-smoke` | Memory cross-project smoke alias. | Smoke Test | `IronDev.DogfoodRunner` |
| `dogfood memory reindex-freshness-smoke` | Memory reindex freshness smoke alias. | Smoke Test | `IronDev.DogfoodRunner` |
| `dogfood memory sql-version-smoke` | Memory SQL version smoke alias. | Smoke Test | `IronDev.DogfoodRunner` |
| `dogfood memory ticket-source-link-smoke` | Memory ticket source link smoke alias. | Smoke Test | `IronDev.DogfoodRunner` |
| `dogfood memory weaviate-sql-version-smoke` | Memory Weaviate/SQL version smoke alias. | Smoke Test | `IronDev.DogfoodRunner` |
| `dogfood run-plan` | Execute dogfood run plan. | Replay/Test Harness | `IronDev.ReplayRunner` |
| `failure latest` | Show latest failure package. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `foundation break-test` | Foundation break test. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `govern review` | Governed action review. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `inventory validate` | Validate command inventory. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `promotion apply isolated` | Apply promotion in isolated workspace. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `promotion apply worktree-dry-run` | Promotion worktree dry run. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `promotion approval create` | Create promotion approval. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `promotion package create` | Create promotion package. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `promotion policy effective` | Show effective promotion policy. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `memory builder-context-source-smoke` | Memory builder context source smoke. | Smoke Test | `IronDev.DogfoodRunner` |
| `memory cross-project-smoke` | Memory cross-project smoke. | Smoke Test | `IronDev.DogfoodRunner` |
| `memory reindex-freshness-smoke` | Memory reindex freshness smoke. | Smoke Test | `IronDev.DogfoodRunner` |
| `memory search` | Internal memory search command. | Internal Dogfood | `IronDev.DogfoodRunner`; add separate product `irondev memory search` through API/client |
| `memory sql-version-smoke` | Memory SQL version smoke. | Smoke Test | `IronDev.DogfoodRunner` |
| `memory ticket-source-link-smoke` | Memory ticket source link smoke. | Smoke Test | `IronDev.DogfoodRunner` |
| `memory triage` | Internal memory triage command. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `memory weaviate-sql-version-smoke` | Memory Weaviate/SQL version smoke. | Smoke Test | `IronDev.DogfoodRunner` |
| `<replay-plan.json>` | Execute replay plan JSON. | Replay/Test Harness | `IronDev.ReplayRunner` |
| `campaign self-improvement-157` | Self-improvement campaign. | Internal Dogfood | `IronDev.DogfoodRunner` |
| `run-report viewer-smoke` | Run report viewer smoke using file-backed report service. | Smoke Test | `IronDev.DogfoodRunner` |
| `test run-plan` | Execute test plan runner. | Replay/Test Harness | `IronDev.ReplayRunner` |
| `tickets document-to-tickets-smoke` | Document-to-tickets smoke. | Smoke Test | `IronDev.DogfoodRunner` |
| `trace build-smoke` | Build trace smoke alias. | Smoke Test | `IronDev.DogfoodRunner` |

## Boundary Assessment

- Product CLI now uses `IronDev.Client` for the four current product ticket commands.
- Product CLI does not reference `IronDev.Infrastructure` and does not directly call repositories, SQL, or `HttpClient`.
- ReplayRunner is correctly internal in spirit, but its breadth makes it easy to confuse smoke/dogfood commands with product commands unless docs and naming stay explicit.
- Several ReplayRunner commands are product-shaped (`memory search`, `docs list`, `build disposable run`) but should not be advertised as public product CLI commands.
