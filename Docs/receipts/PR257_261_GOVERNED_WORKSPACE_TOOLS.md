# PR257-261 - Governed Workspace Tools Receipt

## Purpose

Block AC gates workspace command execution for the manual patch proposal workbench.

Patch-loop test commands now have to pass through:

1. `ToolRequest`
2. `WorkspaceToolGateDecision`
3. `ToolExecutionResult`

The goal is run-scoped command evidence, not broad tool autonomy.

## Tool request model

Block AC adds:

- `ToolRequest`
- `ToolKind`
- `ToolRequestKind`
- `ToolRiskClassification`
- `ToolCommandBoundary`
- `ToolEvidenceRef`

Tool requests are file-backed patch-run artifacts only.

They are not SQL records.
They are not API records.
They are not approval records.
They are not execution permission outside the disposable workspace.

## Workspace gate rules

`WorkspaceToolGateEvaluator` allows a command only when:

- the run id is present
- the workspace exists
- the working directory is the workspace or under it
- the working directory is not the source repository
- the command does not request source apply
- the command does not request git commit, push, merge, or tag
- the command does not request pull request creation
- the command does not request memory mutation or promotion
- the command does not request workflow continuation
- the command does not request release, deploy, or merge approval

Blocked commands write gate evidence before returning failure.

## Result evidence artifacts

Each run can now write:

- `tool-requests.jsonl`
- `tool-gate-decisions.jsonl`
- `tool-results.jsonl`
- `tool-output/<tool-result-id>.stdout.txt`
- `tool-output/<tool-result-id>.stderr.txt`
- `tool-output/<tool-result-id>.combined.txt`
- `tool-output/<tool-result-id>.summary.md`

Blocked commands record `WasExecuted = false`.

Allowed commands record stdout, stderr, combined output, exit code, and a summary.

## Patch test and finish integration

`irondev patch test` now routes the test command through the workspace tool path.

The test phase inside `irondev patch finish` uses the same path.

`test-results.txt` and `test-output-summary.md` are rendered from tool evidence.

There is no separate user-supplied test command shell path for patch test or patch finish.

## Action spine additions

Block AC adds workspace-scoped non-authority actions:

- `WorkspaceToolRequestCreated`
- `WorkspaceToolGateEvaluated`
- `WorkspaceCommandExecuted`
- `WorkspaceToolResultRecorded`

These are allowed in the current block only for workspace-confined patch-run commands.

Generic `ToolExecution` remains authority-bearing, registered only, and not executable in this block.

## Bypass tests

The Block AC test pack proves:

- workspace tool actions are non-authority
- generic `ToolExecution` remains authority-bearing
- direct git push remains forbidden or unsupported
- workspace dotnet command is allowed
- source-repository working directory is blocked
- git push is blocked
- git commit is blocked
- pull request creation is blocked
- source apply is blocked
- memory mutation/promotion is blocked
- workflow continuation is blocked
- release/deploy/merge authority is blocked
- `patch test` writes tool request, gate, and result evidence
- `patch finish` writes tool request, gate, and result evidence
- blocked patch test command writes gate evidence and no execution output
- source repository remains untouched by blocked command
- patch test/finish no longer call loose user-supplied shell paths directly

## Boundaries preserved

This block gates workspace command execution for the patch proposal workbench.

It does not apply source.
It does not execute rollback.
It does not promote memory.
It does not mutate accepted memory.
It does not execute external tools.
It does not dispatch agents.
It does not call models.
It does not continue workflow.
It does not approve release.
It does not approve deployment.
It does not approve merge.
It does not add API, SQL, UI, scheduler, worker, or autonomous runtime behavior.

Workspace command execution is allowed only through ToolRequest, WorkspaceToolGate, and ToolExecutionResult evidence.

Generic ToolExecution remains authority-bearing and not executable in this block.

## Known limitations

Block AC is not a full sandbox.

It gates:

- working directory
- command string
- known forbidden command patterns
- run/workspace binding
- evidence recording

It does not fully prevent every malicious shell trick.

Future deeper containment may require process sandboxing, path access restriction, OS-level isolation, containerized execution, allow-listed commands only, environment variable control, and network control.

Those are not part of this block.

## Validation

Validation run:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "BlockACGovernedWorkspaceTools" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "IronDevCliTests|BlockZManualPatchProposalProduct|BlockAAPatchLoopUsability|BlockABThinGovernedActionSpine|BlockACGovernedWorkspaceTools" --no-restore --logger "console;verbosity=minimal"
dotnet build IronDev.slnx --no-restore -v:minimal
git diff --check
```

Results:

- Block AC governed workspace tools: 6/6 passed.
- CLI + Block Z + Block AA + Block AB + Block AC regression band: 196/196 passed.
- Solution build: passed with 0 errors and 4 warnings.
- `git diff --check`: passed with LF/CRLF warnings only.

## Review line

PR257-261 gates workspace tools for the patch proposal workbench. It records command evidence but does not grant external tool authority.

## Killjoy line

Block AC is finished when commands have to go through the gate, not when the gate merely exists.
