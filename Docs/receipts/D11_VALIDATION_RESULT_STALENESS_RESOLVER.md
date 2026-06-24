# D11 - Validation Result Staleness Resolver

## Purpose

D11 adds a read-only resolver that classifies supplied validation result metadata against supplied staleness rules using a supplied `AsOfUtc`.

It is stacked on `status/evidence-resolver-redaction`.

## Files Changed

- `IronDev.Core/Governance/ValidationStalenessResolverModels.cs`
- `IronDev.Core/Governance/ValidationStalenessResolverValidator.cs`
- `IronDev.Core/Governance/ValidationStalenessResolver.cs`
- `IronDev.IntegrationTests/BlockD11ValidationResultStalenessResolverTests.cs`
- `Docs/receipts/D11_VALIDATION_RESULT_STALENESS_RESOLVER.md`

## Boundary

The validation result staleness resolver classifies supplied validation metadata using supplied staleness rules and supplied AsOfUtc only. It does not run validation, fetch raw validation logs, inspect source or patches, validate freshness of source state, accept approval, satisfy policy, grant authority, choose next safe action, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.

Fresh validation is not authority.

Passed validation is not approval.

Stale or expired validation does not choose next safe action.

Complete validation assessment is not action allowed.

## Supplied-Metadata-Only Behavior

- Tenant, project, operation, validation kind, and correlation ID must match.
- Staleness rules are diagnostic TTL rules only.
- Validation result metadata is assessed without raw logs or command output.
- Redacted validation metadata remains metadata and must carry a redaction reason.
- External reference kind and ID are optional only as a pair.

## Supplied AsOfUtc Behavior

The resolver never reads system time. Age is calculated as:

`Age = AsOfUtc - CompletedAtUtc`

Classification:

- `Fresh`: `Age <= FreshFor`
- `Stale`: `Age > FreshFor && Age <= ExpiresAfter`
- `Expired`: `Age > ExpiresAfter`
- `MissingRule`: no matching validation-kind rule exists
- `Unassessable`: supplied metadata cannot be safely assessed

## Resolution States

- `NoValidationResults`: a valid request contained no validation results.
- `Assessed`: all supplied results were assessed under matching rules.
- `MixedStaleness`: assessed results contain more than one staleness state.
- `MissingRules`: at least one validation result has no matching rule.
- `AmbiguousValidationResults`: duplicate or conflicting rules/results prevent deterministic assessment.
- `Unassessable`: supplied validation metadata cannot be safely assessed.
- `InvalidRequest`: required scope, rule, or validation metadata is missing or unsafe.

Ambiguity never selects a winner. It is diagnostic only and is not denial or approval.

## Non-Authority Boundaries

- `Fresh` is not action allowed.
- `Stale` is not automatic denial.
- `Expired` is not policy decision.
- `Passed + Fresh` is not approval.
- `Failed + Fresh` is not forbidden-action resolution.
- `Passed + Expired` is expired evidence, not proof of failure.
- Validation staleness is not source, patch, base, worktree, or head freshness.
- Validation staleness is not merge, release, or deployment readiness.

## No New Surface

D11 does not add API controllers, frontend files, OpenAPI changes, SQL migrations, stores, repositories, executors, runners, source apply, commit, push, pull request creation, merge, release, deploy, memory promotion, or workflow continuation behavior.

The resolver does not perform D02 lookup, D04 timeline assembly, D05 status projection, D07 missing evidence resolution, D08 forbidden-action resolution, D09 receipt reference resolution, or D10 evidence resolution.

## Validation

Local validation on `status/validation-result-staleness-resolver`:

- D11 focused tests: 85/85 passed
- D10 focused tests: 120/120 passed
- D01-D11 stacked resolver lane: 804/804 passed
- A02 + A05 read-adapter corridor: 61/61 passed
- Governance/status corridor through D11: 984/984 passed for BJ/BK/BL/BT/BZ plus D01-D11
- `dotnet build IronDev.slnx --no-restore -v:minimal`: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed
