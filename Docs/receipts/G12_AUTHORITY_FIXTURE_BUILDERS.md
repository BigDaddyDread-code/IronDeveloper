# G12 - Authority Fixture Builders

## Purpose

Add safe, reusable fast-test fixture builders for authority grants, evidence refs, validation evidence, accepted apply evidence, operation eligibility requests, source-apply authority requests, decisions, and status records.

Fixture builders are not authority builders.

## Files Changed

- `IronDev.UnitTests/Governance/AuthorityFixtureBuilders.cs`
- `IronDev.UnitTests/Governance/AuthorityFixtureBuilderTests.cs`
- `Docs/receipts/G12_AUTHORITY_FIXTURE_BUILDERS.md`

## Builders Added

- Fake identity helpers: repository, branch, run id, patch hash.
- Fake reference helpers: evidence ref, receipt ref, validation evidence ref, fake human approval evidence ref.
- Bounded run grant helpers: default bounded grant, source-apply bounded grant, expired grant, mismatched repository grant, missing validation grant, overbroad file-scope grant.
- Validation helpers: required validation, passed validation evidence, failed validation evidence.
- Eligibility helpers: bounded run authority profile, operation eligibility request, eligible decision, blocked decision.
- Source-apply authority helpers: accepted apply evidence, wrong-patch accepted apply evidence, source-apply authority request.
- Status helper: governed operation status fixture.

## Models Covered

- `BoundedRunAuthorityGrant`
- `BoundedRunAuthorityGrantedBy`
- `BoundedRunAuthorityRequiredValidation`
- `OperationEligibilityRequest`
- `OperationEligibilityValidationEvidence`
- `OperationEligibilityDecision`
- `AcceptedSourceApplyRequestEvidence`
- `SourceApplyAuthorityRequest`
- `GovernedOperationStatus`

## Determinism Rules

All builders use fixed test data and fixed time:

- `ObservedAtUtc = 2026-07-02T12:00:00Z`
- Default expiry is one hour after the fixed observed time.
- IDs and refs use fake `test-*:` prefixes.
- No current clock, environment variables, filesystem target probing, runtime stores, providers, or generated data.

## Test-Only Boundary

All builders live under `IronDev.UnitTests.Governance`.

The builders are internal test helpers. They are not public, not in Core, not in Infrastructure, and not available to production assemblies.

The default grant is intentionally narrow:

- repository: `test-repo:g12`
- branch: `feature/g12`
- run id: `test-run:g12`
- operation: `PatchPackageWrite`
- allowed files: `src/**/*.cs`
- forbidden files: `src/**/Secrets/*.cs`
- patch hash: `test-patch:g12`
- max mutations: `1`
- required validation: `FocusedG12` with `test-validation:` evidence refs
- fixed future expiry

## Non-Authority Boundary

A convenient fake grant is still fake.

A fixture grant is not approval, policy satisfaction, execution authority, source apply, patch authority, git authority, workflow continuation, or production permission.

A fixture evidence ref is not approval.

A fixture receipt ref is not authority and is not a durable receipt.

A fixture accepted apply record is evidence, not source apply execution.

A fixture source apply request is a request object, not source apply.

A fake validation result is not validation truth.

A fake human approval ref is not human approval.

## Dependencies Excluded

- No production code changes.
- No Core fixture builders.
- No Infrastructure fixture builders.
- No API/CLI/SQL/UI dependencies.
- No durable stores.
- No approval writer.
- No policy satisfaction writer.
- No receipt persistence.
- No source apply execution.
- No patch application.
- No git execution.
- No tool execution.
- No workflow continuation.
- No project or package reference changes.
- No integration test deletion.
- No CI rewrite.

## Validation

Local validation completed for this PR:

- `dotnet restore IronDev.slnx`: passed with existing restore warnings.
- `dotnet build IronDev.slnx --no-restore -v:minimal -clp:ErrorsOnly`: passed with existing warnings.
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore -v:minimal -clp:ErrorsOnly`: passed.
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`: 312/312 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BoundedRunAuthority`: 65/65 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~OperationEligibility`: 19/19 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~SourceApplyConsumesBoundedAuthority`: 21/21 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests`: 9/9 passed.
- `git diff --check`: passed.
- `git diff --cached --check`: passed after staging the exact G12 files.
- G12 ASCII check: passed.
- G12 secret-marker scan: passed with no findings.

GitHub `fast-unit-ci` remains separate PR-head evidence.

## Known Limitations

G12 adds test fixture builders only.

G12 does not create production authority, real approvals, policy satisfaction, durable receipts, persistence, source apply execution, patch application, git execution, tool execution, API proof, CLI proof, SQL persistence proof, integration-test replacement, or authority.

## Next Intended Slice

G13 - Integration test category cleanup.

Review line: Test categories are not test quality.

## Killjoy

A convenient fake grant is still fake.
