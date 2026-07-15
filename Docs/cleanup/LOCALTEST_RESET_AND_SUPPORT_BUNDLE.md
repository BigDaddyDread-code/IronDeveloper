# LocalTest Reset and Support Bundle

**Status:** Canonical operations runbook

Use `tools/localtest/Invoke-LocalTestResetAndSupport.ps1` with exactly one action:

```powershell
# Rebuild and reseed the isolated LocalTest tenant/projects.
.\tools\localtest\Invoke-LocalTestResetAndSupport.ps1 -Action ResetTestTenantProject -ConfirmReset

# Reset one explicitly named disposable child of the configured LocalTest workspace root.
.\tools\localtest\Invoke-LocalTestResetAndSupport.ps1 -Action ResetDisposableWorkspace -WorkspacePath C:\IronDevTestWorkspaces\disposable-run-1 -ConfirmReset

# Export a bounded, redacted bundle.
.\tools\localtest\Invoke-LocalTestResetAndSupport.ps1 -Action ExportSupportBundle -OutputPath .\artifacts\support\irondev-support.zip
```

Reset actions refuse non-test database/workspace configuration and require explicit confirmation. Workspace reset is contained below the configured LocalTest root, rejects reparse-point traversal, and requires a delimited test/disposable/run leaf marker.

Support export requires a `.zip` target and includes a manifest plus at most 20 of the newest `.log` tails, each capped at 2,000 lines. Reparse-point logs are excluded and included logs receive generic filenames. The bundle retains correlation IDs for investigation while redacting JSON/key-value credentials, bearer/JWT/API tokens, credential-bearing URLs, and connection strings. It excludes configuration files, databases, workspace contents, receipts, prompts, model responses, and arbitrary artifacts.
