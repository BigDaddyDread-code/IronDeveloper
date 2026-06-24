# D08 -- Forbidden Action Resolver

## Purpose

D08 adds a read-only forbidden action resolver for governed operations.

It classifies supplied scoped diagnostic facts for a requested action and explains which supplied facts block the action or make the diagnostic set ambiguous. It consumes D01 operation identity, D05 projected status vocabulary, and D07 missing evidence status vocabulary without calculating those contracts itself.

## Stack

Base branch while stacked: `status/missing-evidence-resolver`

Branch: `status/forbidden-action-resolver`

## Files Changed

- `IronDev.Core/Governance/ForbiddenActionResolverModels.cs`
- `IronDev.Core/Governance/ForbiddenActionResolverValidator.cs`
- `IronDev.Core/Governance/ForbiddenActionResolver.cs`
- `IronDev.IntegrationTests/BlockD08ForbiddenActionResolverTests.cs`
- `Docs/receipts/D08_FORBIDDEN_ACTION_RESOLVER.md`

## Boundary

The forbidden action resolver explains supplied diagnostic facts that block a requested action. It does not grant permission, accept approval, satisfy policy, validate freshness, resolve evidence, choose next safe action, execute mutation, retry, rollback, merge, release, deploy, promote memory, or continue workflow.

No forbidden facts observed is not action allowed. Forbidden is diagnostic, not policy satisfaction. Ambiguous facts are not denial or approval.

## Supplied-Facts-Only Behavior

D08 only classifies facts supplied in the request:

- blocking facts become findings
- duplicate or conflicting fact IDs become ambiguity
- supplied ambiguous evidence facts become ambiguity
- non-blocking facts remain diagnostic metadata
- missing evidence status and projected status inputs never grant permission

D08 does not calculate missing evidence, validation freshness, patch freshness, worktree state, base/head movement, role capability, approval, policy satisfaction, or next safe action.

## No Expansion

D08 adds no API, SQL, UI, stores, repository wiring, raw evidence payload readers, receipt resolvers, evidence resolvers, validation freshness resolvers, patch/base freshness resolvers, worktree/base/head freshness code, blocked-state formatters, next-safe-action formatters, authority-warning formatters, executors, source apply, commit, push, PR creation, merge, release, deploy, memory promotion, workflow continuation, or CI behavior.

## Validation

Local validation on `status/forbidden-action-resolver`:

- D08 focused tests: 63/63 passed
- D07 focused tests: 81/81 passed
- D06 focused tests: 62/62 passed
- D05 focused tests: 89/89 passed
- D01-D04 focused tests: 216/216 passed
- A02 + A05 read-adapter corridor: 61/61 passed
- governance/status corridor through D08: 737/737 passed
- `dotnet build IronDev.slnx --no-restore -v:minimal`: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed
