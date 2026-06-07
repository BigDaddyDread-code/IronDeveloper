# Workspace copy-only apply E2E proof

This proof is covered by the focused integration test:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "WorkspaceCopyOnlyApply"
```

## What it covers

The test creates a tiny disposable .NET source repository, prepares a workspace through the product CLI, modifies the workspace with one added file and one modified file, then runs the governed copy-only apply spine through:

```text
workspace check
workspace prepare
workspace validate
workspace diff
workspace promotion-package
workspace promotion-approval
workspace apply-preflight
workspace apply-dry-run
workspace apply-copy
workspace apply-verify
workspace post-apply-validate
workspace source-report
```

It asserts the final source report succeeds, the source repository contains the expected add/modify changes, `.irondev` evidence stays out of the source repository, and all expected workspace evidence files exist.

## What it does not cover

This proof does not add delete apply, rollback, commits, branches, GitHub PR creation, agents, tickets, memory writes, or UI.

The failure-package path is already covered by focused PR-24 tests and is intentionally deferred from this E2E proof to keep the happy-path proof cheap and deterministic.

## Evidence files produced

The successful proof expects these files under `.irondev/runs/<run-id>/` in the disposable workspace:

```text
diff.json
promotion-package.json
promotion-approval.json
apply-preflight.json
apply-dry-run.json
apply-copy.json
apply-verify.json
post-apply-validation.json
source-report.json
```
