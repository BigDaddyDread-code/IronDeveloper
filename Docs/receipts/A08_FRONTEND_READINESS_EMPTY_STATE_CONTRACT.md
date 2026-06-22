# A08 — Frontend Readiness Null / Empty-State Contract

## Purpose

Block A08 adds a canonical frontend-readiness read state for backend read surfaces.

The read state makes null, missing, empty, unavailable, redacted, stale, not visible, invalid, and unknown conditions explicit so frontend callers do not infer success from absence.

Review line:

> Unknown is a state. It is not permission to guess.

## Scope

This slice updates the existing frontend-readiness read contracts and API envelopes.

Changed surfaces:

- `IFrontendReadinessReadApi`
- `FrontendReadinessReadState`
- `FrontendReadinessReadStateKind`
- `FrontendReadinessReadStateClassifier`
- `BackendFrontendReadinessReadApi`
- `FrontendReadinessController`
- focused A08 integration tests

This slice does not add UI, durable SQL projection, raw payload reading, mutation handlers, executors, source apply, commit, push, PR creation, merge, release, deploy, memory promotion, or workflow continuation.

## Contract

Every frontend-readiness API envelope now carries a `ReadState`.

Read states include:

- `Available`
- `NotFound`
- `Empty`
- `Redacted`
- `Unavailable`
- `Invalid`
- `Stale`
- `NotVisible`
- `Unknown`

Each read state carries:

- whether data is present
- whether the state is final
- whether the value is redacted
- whether the value is stale
- reasons
- missing refs
- warnings
- next safe actions
- a read-only boundary

Every read state is non-authoritative:

- `IsAuthorityGrant = false`
- `AllowsMutation = false`
- `Boundary = ReadOnlyStatus`

## Boundary

A frontend-readiness read state is not approval.

A frontend-readiness read state is not policy satisfaction.

A frontend-readiness read state is not source apply authority.

A frontend-readiness read state is not commit, push, PR, merge, release, deployment, rollback, memory promotion, or workflow-continuation authority.

Unavailable, unknown, stale, empty, redacted, invalid, and not-visible states must fail closed.

Compact output must still preserve the read state, missing refs, forbidden actions, warnings, and boundary.

Tenant visibility failures must not fall through to fallback optimism.

## Validation

Focused A08 validation:

```text
dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter "FullyQualifiedName~BlockA08FrontendReadinessEmptyStateContractTests" --logger "console;verbosity=minimal"
Passed: 54/54
```

## Killjoy

Null must not become optimism.
