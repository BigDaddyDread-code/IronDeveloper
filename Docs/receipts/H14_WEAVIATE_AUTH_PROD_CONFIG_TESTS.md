# H14 - Weaviate Auth / Production Config Tests

## Purpose

H14 verifies Weaviate auth/prod config boundaries.

It adds focused boundary tests proving Weaviate authentication and production configuration remain fail-closed, secret-safe, environment-aware, and non-authoritative after H13 rebuild hardening.

Review line: Weaviate auth protects the index. It does not make index content authoritative.

Killjoy line: An authenticated vector index is still just an index.

## Outcome

Outcome selected: `BoundaryTestsOnly`.

No production fail-open bug was found during this slice. H14 is test/receipt focused.

## Files Changed

- `IronDev.IntegrationTests/Governance/WeaviateAuthProdConfigBoundaryTests.cs`
- `Docs/receipts/H14_WEAVIATE_AUTH_PROD_CONFIG_TESTS.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`

## Existing C10 Boundary Reused

H14 reuses the existing C10 production auth/config boundary.

C10 already owns the production Weaviate auth implementation seam:

- disabled Weaviate does not require auth
- production-like enabled Weaviate requires an API key
- short API keys fail closed
- placeholder API keys fail closed
- production-like enabled Weaviate requires HTTPS
- production-like enabled Weaviate rejects localhost
- development and local-test localhost may be anonymous
- configuration and `IRONDEV_WEAVIATE_API_KEY` environment source are supported
- committed appsettings API keys are forbidden
- startup exceptions do not expose API key material
- `/api/environment` does not expose Weaviate API keys
- Weaviate semantic service uses configured API key
- auth config does not grant governance authority

H14 does not duplicate C10 blindly. It adds H-block-specific proof around H13 rebuild hardening, default production-like behavior, local/test posture, secret non-disclosure, and non-authoritative authenticated index semantics.

## H13 Rebuild Boundary Checked

H14 checks the H13 rebuild boundary without changing rebuild behavior.

The H14 tests assert:

- H13 rebuild hardening does not bypass `WeaviateAuthConfigValidator`.
- service registration still uses `WeaviateAuthConfigValidator.ResolveOptionsOrThrow(config)`.
- `WeaviateSemanticMemoryService` still consumes configured `WeaviateOptions`.
- the result-returning rebuild path does not directly read configuration or environment variables.
- disabled Weaviate remains explainably blocked for rebuild.
- production invalid auth/config does not become a rebuild authority path.
- rebuild error messages do not expose API key material.
- H13 rebuild result authority flags stay false even when Weaviate auth/config is valid.

## Production-Like Environment Cases

Production-like environments include `Production`, `Staging`, `UAT`, and unset/default environment resolving to production-like behavior.

Production-like enabled Weaviate must fail closed without HTTPS, non-local endpoint, and safe API key.

The H14 tests cover:

- missing API key
- HTTP remote endpoint
- localhost endpoint
- placeholder API key
- short API key
- valid HTTPS remote endpoint with safe configuration key
- unset/default environment resolving to production-like behavior

## Local / Dev / Test Cases

Local anonymous Weaviate is local/test posture only.

The H14 tests cover:

- `Development`, `Test`, and `LocalTest` may allow anonymous localhost HTTP.
- non-local HTTP endpoint is rejected even in local/test posture.
- non-local HTTPS endpoint without a key is rejected.
- production-like localhost anonymous posture remains invalid.

## API Key Source Rules

H14 verifies:

- `IRONDEV_WEAVIATE_API_KEY` remains accepted as environment fallback.
- explicit safe configuration source remains accepted.
- committed appsettings keys remain rejected.
- placeholders remain rejected.
- configuration source is recorded through `ApiKeySource` without leaking key values.

## Secret Non-Disclosure Rules

API keys must not appear in validation output, startup exceptions, environment endpoints, rebuild results, or receipts.

H14 verifies API key material is absent from:

- `WeaviateAuthConfigValidationResult.ToString()`
- `WeaviateAuthConfigResolution.ToString()`
- startup exception text
- rebuild failure/result messages
- `EnvironmentInfoDto`
- `/api/environment` endpoint chain
- committed appsettings files
- this receipt

H14 uses fake test-only values only. It adds no real API keys and no realistic production secrets.

## Authenticated-Index Non-Authority Boundary

H14 does not make Weaviate authoritative.

H14 does not make index content authoritative.

H14 does not validate indexed content.

Weaviate auth protects the index only.

An authenticated vector index is still just an index.

authenticated vector recall is not authority.

authenticated vector recall is not evidence validation.

authenticated vector recall is not retention compliance.

authenticated Weaviate is not approval.

authenticated Weaviate is not policy satisfaction.

authenticated Weaviate is not source-apply authority.

authenticated Weaviate is not workflow continuation authority.

authenticated Weaviate is not merge readiness.

authenticated Weaviate is not release readiness.

authenticated Weaviate is not deployment readiness.

authenticated Weaviate is not rollback authority.

authenticated Weaviate is not retry authority.

authenticated Weaviate is not mutation authority.

authenticated Weaviate does not validate indexed content.

authenticated vector recall is still recall.

SQL remains source of truth.

Weaviate remains a rebuildable derived index.

## What Was Intentionally Not Built

H14 does not change Weaviate rebuild behavior.

H14 does not add a rebuild command.

H14 does not change Docker compose.

H14 does not change deployment config.

H14 does not change API/CLI/UI behavior.

H14 does not add SQL migrations.

H14 does not alter tables.

H14 does not add indexes.

H14 does not alter stored procedures.

H14 does not require live Weaviate in tests.

H14 does not require Docker in tests.

H14 does not change Weaviate runtime availability behavior.

H14 does not change source-apply, rollback, workflow, release, or deployment behavior.

H14 does not grant approval, policy satisfaction, source-apply authority, merge readiness, release readiness, deployment readiness, rollback authority, retry authority, mutation authority, or workflow continuation authority.

## Tests Added

Test class:

- `IronDev.IntegrationTests/Governance/WeaviateAuthProdConfigBoundaryTests.cs`

Categories:

- `Governance`
- `Weaviate`
- `Auth`
- `ProductionConfig`
- `SecretSafety`
- `Boundary`
- `Contract`

Test methods:

- `WeaviateAuth_ProductionEnabledRequiresHttpsRemoteEndpointAndApiKey`
- `WeaviateAuth_DefaultEnvironmentIsProductionLike`
- `WeaviateAuth_DevelopmentAndLocalTestAllowAnonymousLocalhostOnly`
- `WeaviateAuth_ApiKeySourceDoesNotLeakSecretMaterial`
- `WeaviateAuth_CommittedConfigFilesContainNoApiKeyMaterial`
- `WeaviateAuth_EnvironmentEndpointDoesNotExposeApiKey`
- `WeaviateAuth_RebuildResultsDoNotExposeApiKey`
- `WeaviateAuth_RebuildDoesNotBypassProductionValidation`
- `WeaviateAuth_AuthenticatedIndexDoesNotGrantAuthority`
- `WeaviateAuth_DoesNotIntroduceDeploymentOrRuntimeChanges`
- `Receipt_RecordsAuthProdConfigScopeAndLimitations`

## Commands Run

- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --filter FullyQualifiedName~WeaviateAuthProdConfigBoundaryTests --logger "trx;LogFileName=h14-weaviate-auth-prod-config.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~WeaviateAuthProdConfigBoundaryTests --logger "trx;LogFileName=h14-weaviate-auth-prod-config.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC10WeaviateProductionAuthConfigTests --logger "trx;LogFileName=h14-c10-weaviate-auth-boundary.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~WeaviateRebuildCommandHardeningTests|FullyQualifiedName~WeaviateAuthProdConfigBoundaryTests" --logger "trx;LogFileName=h13-h14-weaviate-corridor.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~IntegrationTestCategoryContractTests --logger "trx;LogFileName=h14-category-contract.trx"`
- `dotnet test IronDev.IntegrationTests/IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "trx;LogFileName=h14-c11-secret-scan.trx"`
- `dotnet build IronDev.slnx --no-restore`
- `git diff --check`
- `git diff --cached --check`

## Validation Results

- Initial focused H14 run: 10/11 passed. One receipt wording assertion failed because the receipt used sentence-case `Authenticated Weaviate...` while the spec/test required lowercase `authenticated Weaviate...`.
- Receipt wording corrected to the exact spec phrases.
- Focused H14 no-build rerun: 11/11 passed.
- Final focused H14 no-build rerun after receipt validation update: 11/11 passed.
- C10 Weaviate auth boundary: 17/17 passed.
- H13/H14 Weaviate rebuild/auth corridor: 20/20 passed.
- G13 category contract: 7/7 passed.
- C11 secret scan: 9/9 passed.
- Solution build: 0 errors / 4 existing warnings.
- `git diff --check`: passed with CRLF warning only.
- `git diff --cached --check`: passed after staging exact H14 files.

## Known Limitations

- H14 does not run a live Weaviate instance.
- H14 does not run Docker.
- H14 does not validate production deployment infrastructure.
- H14 does not test actual network authentication against a remote Weaviate server.
- H14 does not implement deployment configuration hardening.
- H14 does not implement raw payload redaction or artifact retention.
- H14 does not alter the H13 rebuild command behavior.

## Next Intended Slice

Block H completion / review pass.

Review line: Block H hardens storage and derived-index boundaries. It does not make derived systems authoritative.

Killjoy: A hardened index is still not authority.
