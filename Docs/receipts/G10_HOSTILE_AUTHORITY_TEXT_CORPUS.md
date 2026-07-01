# G10 - Hostile Authority Text Corpus Tests

## Purpose

Add a fast, test-only hostile text corpus for memory, status, and UI authority claims.

G10 proves that selected hostile phrases stay non-authoritative across the existing Core memory/status/UX seams and the existing Tauri UI authority firewall.

## Files Changed

- `IronDev.UnitTests/Governance/HostileAuthorityTextCorpusFixtures.cs`
- `IronDev.UnitTests/Governance/HostileAuthorityTextCorpusTests.cs`
- `IronDev.TauriShell/tests/ui-authority-hostile-corpus.spec.ts`
- `Docs/receipts/G10_HOSTILE_AUTHORITY_TEXT_CORPUS.md`

## What Landed

- Memory hostile text cases for approval, policy satisfaction, execution, source mutation, workflow continuation, memory promotion, and release-readiness claims.
- Status hostile text cases for memory, UI, receipt, evidence, tests-passed, policy-by-status, and execution-by-status claims.
- Authority UX semantic leak cases for mutation completion, old authority permission use, memory permission use, workflow transfer, explanation verdict changes, explanation authority grants, and unsafe next actions.
- UI firewall hostile corpus tests for authority-looking copy and forbidden action labels.
- Negative-boundary tests proving safe denial text can mention authority without becoming authority.

## Boundary

Hostile text tests are not hostile text immunity.

The corpus is evidence only. It does not create a general text sanitizer, language model guard, runtime authorization policy, API guard, frontend permission system, workflow gate, or executor.

Memory may explain context. It must not approve, satisfy policy, execute, mutate source, continue workflow, promote memory, release, deploy, rollback, merge, commit, push, or publish packages.

Status may explain a governed operation state. It must not become approval, policy satisfaction, execution authority, source-apply authority, workflow continuation authority, release authority, deployment authority, rollback authority, merge authority, commit authority, push authority, or memory-promotion authority.

UI scanning may catch known dangerous labels and claims. It is not backend truth, runtime policy, authorization, access control, redaction, validation, source safety, workflow continuation, release readiness, or execution authority.

## What Did Not Land

- No production Core behavior change.
- No Infrastructure behavior change.
- No API/CLI/SQL/store/projection behavior change.
- No source apply, rollback, commit, push, PR, merge, release, deploy, provider, model, tool, memory retrieval, or workflow execution change.
- No CI workflow or project/package reference change.
- No broad text immunity claim.

## Validation

Validation run for this PR:

- `dotnet restore IronDev.slnx`: passed with existing NU1510 warnings.
- `dotnet build IronDev.slnx --no-restore`: passed, 0 errors / existing warnings.
- `dotnet build IronDev.UnitTests/IronDev.UnitTests.csproj --no-restore`: passed, 0 errors / existing warnings.
- `dotnet test IronDev.UnitTests/IronDev.UnitTests.csproj --no-build`: 254/254 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~MemoryNonAuthority`: 54/54 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~AuthorityUx`: 42/42 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~AuthorityProfileStatus`: 27/27 passed.
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests`: 9/9 passed.
- `cd IronDev.TauriShell && npm test -- tests/ui-authority-firewall.spec.ts`: 13/13 passed.
- `cd IronDev.TauriShell && npm test -- tests/ui-authority-hostile-corpus.spec.ts`: 5/5 passed.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Review Line

Hostile text tests are not hostile text immunity.

## Killjoy

Catching this lie does not catch every lie.
