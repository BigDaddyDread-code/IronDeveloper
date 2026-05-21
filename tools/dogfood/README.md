# IronDev Dogfood Replay Harness

This folder contains the resettable BookSeller dogfood harness.

The harness is built around one rule:

```text
Every run gets a DogfoodRunId, and every trace/result/artifact should be stamped with it.
```

Example:

```text
20260521-BookSellerMvp-001
```

## Layout

```text
tools/dogfood/
  Reset-BookSellerDogfood.ps1
  Start-BookSellerReplay.ps1
  dogfood-scenarios/
    BookSellerMvp.json
  runs/
    {DogfoodRunId}/
      dogfood-run.json
      reset-log.json
      replay/
        replay-plan.json
        replay-summary.json
        replay-results.json
        action-results.json
        response-results.json
        runner-summary.json
      traces/
      screenshots/
      reports/
```

## Reset BookSeller

Dry-run first:

```powershell
.\tools\dogfood\Reset-BookSellerDogfood.ps1 `
  -BaselinePath C:\repo\BookSeller_Baseline `
  -TargetPath C:\repo\BookSeller `
  -DatabaseName BookSellerDogfood `
  -RunId 20260521-BookSellerMvp-001 `
  -DryRun
```

If local execution policy blocks scripts, run the same script through PowerShell explicitly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\dogfood\Reset-BookSellerDogfood.ps1 `
  -BaselinePath C:\repo\BookSeller_Baseline `
  -TargetPath C:\repo\BookSeller `
  -DatabaseName BookSellerDogfood `
  -RunId 20260521-BookSellerMvp-001 `
  -DryRun
```

Real reset:

```powershell
.\tools\dogfood\Reset-BookSellerDogfood.ps1 `
  -BaselinePath C:\repo\BookSeller_Baseline `
  -TargetPath C:\repo\BookSeller `
  -DatabaseName BookSellerDogfood `
  -RunId 20260521-BookSellerMvp-001 `
  -SqlServer . `
  -StopIronDev `
  -Force
```

Safety rules:

- `BaselinePath` must end with `\BookSeller_Baseline`.
- `TargetPath` must end with `\BookSeller`.
- Baseline and target cannot be the same path.
- The target cannot be `C:\`, `C:\repo`, the user profile, or the IronDev repo root.
- Destructive reset requires `-Force`.
- `-DryRun` shows intent without deleting/copying/resetting.

## Generate a randomized replay plan

Every run should be different. The replay script adds a seed and randomizes prompt prefixes, suffixes, and eligible workspace context.

```powershell
.\tools\dogfood\Start-BookSellerReplay.ps1 `
  -RunId 20260521-BookSellerMvp-001 `
  -Scenario .\tools\dogfood\dogfood-scenarios\BookSellerMvp.json `
  -Reps 100 `
  -DryRun `
  -StopOnFailure
```

Run reset and replay planning together:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\dogfood\Invoke-BookSellerDogfood.ps1 `
  -Reset `
  -DryRun `
  -Reps 100 `
  -StopOnFailure
```

The output is written to:

```text
tools/dogfood/runs/{DogfoodRunId}/replay/replay-plan.json
tools/dogfood/runs/{DogfoodRunId}/replay/replay-summary.json
tools/dogfood/runs/{DogfoodRunId}/replay/replay-report.md
```

For deterministic debugging, reuse the saved seed:

```powershell
.\tools\dogfood\Start-BookSellerReplay.ps1 `
  -RunId 20260521-BookSellerMvp-002 `
  -Reps 100 `
  -DryRun `
  -Seed 123456
```

## Runner integration

The script currently generates a replay plan. It can call the IronDev replay runner by passing `-RunnerCommand`.

The runner should read `replay-plan.json`, execute each case through IronDev internals, and save results linked by:

```text
DogfoodRunId -> CaseId -> TraceGroupId -> LLMTrace / RouteDecision / SemanticSearchTrace / WorkflowRun
```

Replay must default to dry-run and assert behaviours rather than exact wording.

The current runner executes routing plus dry-run action simulation. It writes:

```text
replay-results.json   # route, assertion, and simulated count summary
action-results.json   # dry-run discussion docs, draft tickets, plans, build approvals, blocked actions
response-results.json # per-turn user prompt, assistant response, and final-turn marker
runner-summary.json   # aggregate pass/fail result
```

Dry-run action simulation must never write project files or persist tickets. It exists to prove that a routed command would create the expected reviewable artefacts, request approval where required, and block unsafe or contradictory instructions.

Replay cases may include `followUpTurns`. The runner feeds the previous assistant response and previous user message into the next turn so it can test real conversational flow:

```text
User: I need to save data
Assistant: asks for clarification / blocks action safely
User: BookSeller should save books, authors, stock counts, storage locations, and sales history in SQL Server with Dapper. Save that as project knowledge.
Runner assertion: creates a reviewable discussion document, changes no files
```

This lets the harness test whether IronDev can ask, receive an answer, route the follow-up, and produce a reviewable action without going through the WPF interface.

## Headless chat feedback

The runner also exposes a small CLI-style chat command for Codex/headless testing. It routes one chat turn through the same deterministic command router and returns JSON feedback:

```powershell
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- `
  chat send "I need to save data" `
  --workspace Chat `
  --dogfood-run-id cli-smoke-001
```

Follow-up turns can pass the prior assistant and user text:

```powershell
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- `
  chat send "BookSeller should save books, authors, stock counts, storage locations, and sales history in SQL Server with Dapper. Save that as project knowledge." `
  --workspace Chat `
  --previous-assistant "I need a little more detail before I can safely turn that into project memory, tickets, or a build action." `
  --previous-user "I need to save data" `
  --dogfood-run-id cli-smoke-001
```

The command returns:

```text
assistantResponse
intent
confidence
isAction / requiresAction / allowsProseResponse
contextReference
matchedSignals
simulated discussion docs / draft tickets / plans / build runs
simulated files changed
dryRun
```

That JSON is the Codex feedback contract: send a prompt, read IronDev's response, decide the next prompt or patch, and run again.

## Failure package for Codex

When a replay assertion fails, generate a Codex handoff package from the latest failed replay result:

```powershell
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- `
  failure latest `
  --for-codex `
  --runs-root .\tools\dogfood\runs
```

For a specific run:

```powershell
dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj -- `
  failure latest `
  --for-codex `
  --runs-root .\tools\dogfood\runs `
  --run-id BookSellerLoop-001-iter-0007
```

The command writes:

```text
failure-package.json
failure-package.md
```

The package includes expected intent, actual intent, failed prompt, likely files, repro command, validation command, and safety rules.

## Headless CLI smoke test

Run the first smoke suite for the headless control port:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\dogfood\Test-HeadlessCliSmoke.ps1
```

It verifies:

- `chat send` returns JSON feedback.
- a vague prompt returns a clarification-style response.
- a follow-up answer routes to a dry-run action.
- a 10-case replay batch passes.
- an intentional replay failure produces `failure-package.json` and `failure-package.md`.

## Vague prompt pressure

The BookSeller scenario intentionally includes vague and contradictory prompts such as:

```text
make it better
turn that into the thing
same as before but better
set it all up then build the first one
build it now but don't change anything
```

These cases should not assert exact prose. They assert routing safety:

- ask for clarification when context is missing
- resolve `this/that/above` only when a source context exists
- block unsafe or contradictory actions
- stop at approval before code changes
- never create tickets or files from a vague prompt without source evidence

## Compare two replay plans

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\dogfood\Compare-DogfoodReplayRuns.ps1 `
  -LeftRunId BookSellerMvp-20260521-001 `
  -RightRunId BookSellerMvp-20260521-002
```

The comparison reports seed equality, prompt overlap, case counts, and workspace mix. A healthy chaos batch should usually have different seeds and low prompt overlap.

## Run one case at a time

For long hardening loops, run one randomized case per iteration. This creates a separate run folder for each iteration and a JSONL loop log.

Plan-only smoke loop:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\dogfood\Invoke-DogfoodIterationLoop.ps1 `
  -LoopId BookSellerLoop-001 `
  -Iterations 25 `
  -DryRun `
  -StopOnFailure
```

Future internal runner loop:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\dogfood\Invoke-DogfoodIterationLoop.ps1 `
  -LoopId BookSellerLoop-001 `
  -Iterations 1000 `
  -DryRun `
  -StopOnFailure `
  -RunnerCommand "dotnet run --project .\tools\IronDev.ReplayRunner\IronDev.ReplayRunner.csproj --"
```

Each iteration writes:

```text
tools/dogfood/runs/{LoopId}-iter-0001/replay/replay-plan.json
tools/dogfood/runs/{LoopId}-iter-0001/replay/replay-summary.json
tools/dogfood/runs/{LoopId}/iteration-loop.jsonl
tools/dogfood/runs/{LoopId}/iteration-loop-summary.json
```
