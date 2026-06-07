# Supervisor CLI Proof

This folder captures a dogfood proof run for the product command:

```powershell
dotnet run --no-build --project tools/IronDev.Cli -- `
  agent run supervisor `
  --project IronDev `
  --query "Validate the current IronDev CLI supervisor path and report any blockers." `
  --plan tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json `
  --run-id supervisor-cli-proof-001 `
  --json
```

The proof validates that `irondev agent run supervisor` emits the stable CLI
contract envelope through the typed supervisor run service path. The run result
may succeed, fail, or block; the proof is about honest contract output rather
than forcing a green result.

Files:

- `supervisor-cli-proof-001.json`: captured stdout from the command.
- `supervisor-cli-proof-001.summary.md`: extracted fields, result, and follow-up actions.
