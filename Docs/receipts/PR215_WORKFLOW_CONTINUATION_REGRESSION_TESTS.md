# PR215 - Workflow Continuation Regression Tests

Review line:

> PR215 locks the continue button cage. It does not press continue.

## Purpose

PR215 adds regression tests and this receipt for governed workflow continuation.

It verifies that the continuation surface remains narrow after PR214:

- Self-attested gate evaluations remain rejected.
- Fresh gate recomputation remains mandatory.
- Unsatisfied or dirty gate evidence cannot mutate workflow state.
- Stale workflow and step state hashes block before mutation.
- Workflow transition records are required before success can be claimed.
- API and CLI surfaces remain governed, narrow, and evidence-only.
- Unsafe raw/private material and authority claims remain rejected.

## Boundary

This PR does not add workflow continuation behavior.

It does not add:

- workflow runner behavior
- workflow state mutation behavior
- source apply behavior
- rollback behavior
- release readiness
- release approval
- policy satisfaction
- memory promotion
- retrieval activation
- agent dispatch
- model invocation
- tool execution
- Git or GitHub actions
- SQL schema changes
- API endpoint expansion
- CLI command expansion
- UI changes

## Locked invariants

- A caller-supplied satisfied gate is not authority by itself.
- Governed continuation must compare supplied gate evidence to freshly recomputed gate evidence.
- A transition record is mutation evidence only.
- A transition record is not release approval.
- A transition record is not release readiness.
- A transition record is not policy satisfaction.
- Human review remains required before release readiness and release approval.

## Validation target

Focused validation should include:

```powershell
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "WorkflowContinuationRegression" --no-restore --logger "console;verbosity=minimal"
dotnet test IronDev.IntegrationTests.Api\IronDev.IntegrationTests.Api.csproj --filter "WorkflowContinuationApiRegression" --no-restore --logger "console;verbosity=minimal"
dotnet build IronDev.slnx --no-restore -v:minimal
git diff --check
```
