# G04 - Authority Profile Evaluator Unit Tests

## Purpose

Seed fast unit coverage for pure authority profile evaluator behavior in `IronDev.UnitTests`.

Review line:

> Authority profile evaluator tests are not authority grants.

Killjoy line:

> Evaluating the rule is not issuing the permission.

## Files Changed

- `IronDev.UnitTests/Authority/AuthorityProfileEvaluatorUnitTests.cs`
- `IronDev.UnitTests/Authority/AuthorityProfileEvaluatorTestFixtures.cs`
- `Docs/receipts/G04_AUTHORITY_PROFILE_EVALUATOR_UNIT_TESTS.md`

## Evaluator Covered

- `OperationEligibilityEvaluator`

## Tests Moved Or Duplicated

No integration tests were moved or deleted.

G04 uses the duplicate-first strategy. Existing integration coverage remains intact; these tests seed cheaper fast-lane coverage for deterministic evaluator behavior only.

## Why The Tests Are Pure

The G04 unit tests:

- use only `IronDev.Core` models and evaluator types
- use fixed in-memory profile, grant, request, file-scope, and validation evidence fixtures
- call `OperationEligibilityEvaluator.Evaluate(...)` directly
- assert deterministic eligibility, blocked reason, missing evidence, forbidden action, and independent-check output
- use a fixed `DateTimeOffset`
- do not use status mappers, SQL, repositories, projections, API host/controller behavior, provider clients, GitHub clients, environment variables, current time, or integration helpers

## Dependencies Excluded

The fast unit project remains scoped to:

- `IronDev.Core`
- MSTest package references

G04 does not add references to API, CLI, Infrastructure, IntegrationTests, SQL, workers, provider projects, test-host packages, or CI helpers.

## Boundary Proofs

G04 proves the selected evaluator paths keep conservative authority semantics:

- `ProposalOnly` can evaluate proposal-safe patch package readiness but still does not grant authority.
- `ProposalOnly` blocks durable mutation operations even when validation evidence exists.
- `AskBeforeMutation` source apply requires separate approval-like, policy, and validation evidence configured as required grant evidence.
- bounded-run authority allows only the operation kind inside the grant envelope.
- repository, branch, and run scope drift blocks eligibility.
- patch-bound operations reject patch hash mismatch.
- expired grants block otherwise valid requests.
- missing and failed validation evidence fail closed.
- mutation budget exhaustion fails closed.
- unknown profile and operation kinds fail closed.
- source apply authority does not authorize commit.
- commit authority does not authorize push.
- push authority does not authorize draft PR creation.
- draft PR authority does not authorize ready-for-review.
- rollback plan evidence does not authorize rollback execution when the grant stops before rollback.

## Commands Run

- `dotnet restore IronDev.slnx`
- `dotnet build IronDev.slnx --no-restore`
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore`
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build --logger "console;verbosity=minimal"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~BlockC11SecretScanningRegressionTests" --logger "console;verbosity=minimal"`
- `git diff --check`
- `git diff --cached --check`

## Reported Validation

- Restore: passed with existing integration package-prune warnings.
- Build: passed with 0 errors and existing warnings.
- Fast unit tests: 58/58 passed.
- C11 secret scan: 9/9 passed.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## GitHub CI

`fast-unit-ci` passed on the first pushed G04 head `48b35635d05f9ba7283cae8088efe477693aa546`.

- Run: `28334254628`
- Job: `83937608377`

Current-head GitHub CI evidence is tracked on the PR checks and PR body after the final push.

## Known Limitations

G04 does not grant authority.
G04 does not test status mappers.
G04 does not test operation projection.
G04 does not test API envelopes.
G04 does not test SQL persistence.
G04 does not test executors.
G04 does not test provider behavior.
G04 does not change production authority semantics.
G04 does not replace integration tests.
G04 does not prove release readiness.

## Next Intended Migration Area

G05 should seed fast unit tests for pure source-apply evaluator behavior.
