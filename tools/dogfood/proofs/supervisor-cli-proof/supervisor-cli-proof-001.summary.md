# Supervisor CLI Proof 001

## Command Run

The local API was started first with:

```powershell
dotnet run --project IronDev.Api --urls http://localhost:5000
```

The proof command was then run from the repository root:

```powershell
dotnet run --no-build --project tools/IronDev.Cli -- `
  agent run supervisor `
  --project IronDev `
  --query "Validate the current IronDev CLI supervisor path and report any blockers." `
  --plan tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json `
  --run-id supervisor-cli-proof-001 `
  --json
```

`--no-build` was used after a successful build so captured stdout contains only
the product CLI command output, not compiler warnings.

## Run Facts

- Timestamp: `2026-06-07T06:40:45Z`
- Branch: `dogfood/supervisor-cli-proof-run`
- Commit: `3400567a4a33b28b7e29f453185db4531cd7d360`
- Exit code: `1`
- Top-level CLI status: `failed`
- Top-level command: `agent run supervisor`
- Run ID: `supervisor-cli-proof-001`
- Trace ID: `null`
- Supervisor decision: `request_failure_package`
- Supervisor decision reason: `TesterAgent did not return a passing report; Codex needs a failure package before patching.`
- Tester command status: `not_available`
- Tester run status: `not_available`
- Tester trace ID: `null`

## Envelope Check

Result: `PASS`

The command returned the standard CLI envelope with these root fields:

```text
status
command
traceId
summary
data
errors
warnings
```

The root object did not contain `loopReport`, so the output was not raw
ReplayRunner output.

## Evidence Paths

```text
C:\Users\bob\source\repos\AIDeveloper\tools\dogfood\runs\supervisor-cli-proof-001-tester\logs\step-001-dotnet_build.log
```

## Warnings

```text
SupervisorAgent tester contract status was not available; treating supervisor run as failed.
```

## Errors

```text
SupervisorAgent output did not include an available tester contract status.
SupervisorAgent loop failed before producing a clean Codex handoff.
```

## Follow-Up Actions

- Investigate why the tester run did not produce an available run-report contract for `supervisor-cli-proof-001-tester`.
- Preserve the fail-closed behavior: unavailable tester contract status must continue to return a non-zero product CLI result.
- Track the remaining internal `SupervisorAgent` sub-agent execution through ReplayRunner separately; this proof confirms the product CLI output is normalized, but `commandsRun` still exposes internal ReplayRunner-backed sub-agent calls.
- Consider a separate fix for API-unavailable `--json` behavior. A preliminary run without the local API produced no contract envelope because readiness failed before the supervisor run service was created.
