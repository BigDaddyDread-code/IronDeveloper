# Backend Configuration and Dependency Inventory

PR 55 is configuration/dependency cleanup, not capability expansion.

No new capability introduced.

No SQL/schema/proc shape/API/CLI/UI/runtime/persistence behaviour changes.

This inventory records backend configuration keys, dependency registrations, package references, and known debt before the PR 56 Backend Contract Freeze Report. SQL remains source of truth. Vector/index/retrieval remains lookup only. Proposal is not apply. Candidate is not memory. Retrieval match is not memory candidate. Audit is not approval. Gate is not executor. Critic is not governance. Memory safe is not approval. Tool request is request form, not execution permission. Model output is advisory only. Human review remains required for source apply and memory promotion.

## Configuration files

| File path | Owning backend area | Runtime or test-only | Status | Changed in PR 55 | Reason for change | Behaviour unchanged |
| --- | --- | --- | --- | --- | --- | --- |
| `IronDev.Api/appsettings.json` | API host defaults | Runtime | Active | No | Documents API defaults for JWT and code proposal mode; shared SQL connection string is intentionally blank. | Yes |
| `IronDev.Api/appsettings.Development.json` | API host local development | Runtime | Active | No | Documents a generic LocalDB development example only; machine-specific SQL belongs in environment or user-secret overrides. | Yes |
| `IronDev.Api/appsettings.Development.Local.json` | API host developer-local override | Runtime local development only | Ignored/untracked | No | Optional Development-only override loaded after committed development settings and before user secrets, environment variables, and command-line. Added in J02 as local convenience only. | Yes when absent |
| `IronDev.Api/appsettings.LocalTest.json` | LocalTest API host | Runtime test environment | Active | No | Documents isolated LocalTest database/workspace/log roots with a generic LocalDB test example. | Yes |
| `IronDev.IntegrationTests/appsettings.Test.json` | integration tests | Test-only | Active | No | Documents a generic LocalDB test example; machine-specific SQL belongs in `ConnectionStrings__IronDeveloperDb`. | Yes |
| `IronDev.IntegrationTests.Api/appsettings.Test.json` | API integration tests | Test-only | Active | No | Documents a generic LocalDB API-test example; machine-specific SQL belongs in `ConnectionStrings__IronDeveloperDb`. | Yes |
| `IronDev.IntegrationTests.Api/ApiTestBase.cs` | API test host setup | Test-only | Active | No | Supplies test host overrides for JWT, generic test connection string, workspace root, and logs root. | Yes |
| `Scripts/local/bootstrap-local.ps1` | local developer bootstrap diagnostics | Local developer tooling | Active | No | J04 check-only bootstrap script for repository/tool/local override status and optional explicit restore/install actions. | Yes when not invoked |

No configuration file was removed or renamed in PR 55.

## J02 development local override

`IronDev.Api/appsettings.Development.Local.json` is an ignored, optional developer-local override source. The API host loads it only when `IWebHostEnvironment.IsDevelopment()` is true.

Precedence for the API host is:

1. `appsettings.json`
2. `appsettings.Development.json`
3. `appsettings.Development.Local.json`
4. API user secrets, when available
5. environment variables
6. command-line arguments

The local override file is never required for CI, tests, production-like environments, LocalTest, or shared development. It is not evidence, not authority, not bootstrap, not environment validation, and not permission to run SQL, Weaviate, source mutation, evidence mutation, sandbox mutation, release, deployment, or workflow behavior.

## J08 redacted config summary

J08 adds a Core-only redacted configuration summary contract. It reports source presence/order, safe SQL-derived metadata, AI provider/model status, AI base URL host/port, Weaviate enabled/auth status, local root status, and feature flag state without emitting raw connection strings, secret values, local override file contents, or full user-local paths.

Connection strings may be parsed into derived metadata such as database name, server kind, authentication mode, and whether a credential key exists. The raw connection string is never emitted. Sensitive keys such as API keys, JWT keys, tokens, authorization values, client secrets, private keys, and credential-shaped values are redacted.

Root safety integration is intentionally read-only. Until J10 root-safety validation exists, configured roots are reported as `NotEvaluated`; J08 does not invent root safety or validate roots itself.

J08 adds no endpoint, no startup logging, no bootstrap behavior, no SQL connectivity check, no Weaviate connectivity check, no schema change, and no source-apply, approval, critic, workflow, release, or deployment authority.

Boundary: a config summary is diagnostic evidence for a human. It is not approval, authority, policy satisfaction, root safety proof, or permission to mutate anything.

## J04 safe local bootstrap script

J04 adds `Scripts/local/bootstrap-local.ps1` as a safe local developer bootstrap entrypoint.

Default behavior is check-only:

- verify repository shape
- verify .NET SDK and Git command presence
- report frontend package status
- report ignored/untracked local override status
- report whether the J08 redacted config summary Core contract is present
- report J10 root safety as `NotEvaluated` unless a future root-safety contract is present
- print next safe actions

The default check-only path does not create files or directories, restore packages, install frontend packages, start the UI, call SQL, create or rebuild databases, start or rebuild Weaviate, write evidence, invoke source apply, invoke rollback, run release/deploy paths, or continue workflow.

Prepare mode can copy `IronDev.Api/appsettings.Development.Local.example.json` to the ignored `IronDev.Api/appsettings.Development.Local.json` only when `-Prepare -CreateLocalOverride` is supplied and the target file is absent. It does not overwrite an existing local override.

Prepare mode can run `dotnet restore IronDev.slnx` only when `-Prepare -RestoreDotNet` is supplied. It can run frontend package install inside `IronDev.TauriShell` only when `-Prepare -InstallFrontend` is supplied. It does not start the API, start the UI, call Docker, call SQL, or call Weaviate.

The bootstrap script does not print raw connection strings, API keys, JWT keys, token values, authorization headers, local override contents, or full user-local paths.

Boundary: The local bootstrap script prepares local convenience. It is not evidence, approval, root safety proof, policy satisfaction, or permission to mutate source, SQL, Weaviate, evidence, or sandbox repositories.

## Backend configuration keys

| Key | Owning option class or consumer | Required or optional | Default behaviour | Test coverage | Status | Changed in PR 55 |
| --- | --- | --- | --- | --- | --- | --- |
| `ConnectionStrings:IronDeveloperDb` | `SqlConnectionFactory`, `IntegrationTestBase`, API test host | Required for SQL-backed runtime/tests | Empty or missing connection fails when SQL connection is used. | SQL-backed integration and API host tests. | Active | No |
| `Jwt:Key` / `Jwt__Key` / `IRONDEV_JWT_KEY` | `JwtSigningKeyResolver`, `JwtTokenService`, API auth setup | Required for API host, but must be supplied by environment/secret configuration rather than committed appsettings. | API startup throws if missing or too short. | API auth tests and C06 JWT configuration hardening tests. | Active | No |
| `Jwt:Issuer` | API auth setup, `JwtTokenService` | Required for token validation semantics | Uses configured issuer for JWT validation. | API auth tests. | Active | No |
| `Jwt:Audience` | API auth setup, `JwtTokenService` | Required for token validation semantics | Uses configured audience for JWT validation. | API auth tests. | Active | No |
| `Jwt:ExpiryMinutes` | `JwtTokenService` | Optional | Token service uses configured value where present. | API auth tests. | Active | No |
| `CodeProposal:Mode` | `Program.cs`, `TicketReviewService` | Optional | Defaults to deterministic generator unless set to model-assisted. | builder/ticket tests. | Active | No |
| `Ai:Provider` | `Program.cs`, `ProjectServicesController` | Optional | Defaults to OpenAI service selection path when available, fake fallback for unknown provider. | API service metadata and LLM tests. | Active | No |
| `Ai:Model` | `ProjectServicesController` | Optional | Reported as configured model metadata. | API/service metadata tests. | Active | No |
| `Ai:ApiKey` | `LlmOptions`, `OpenAiLlmService` | Optional until OpenAI provider is used | Falls back to `OPENAI_API_KEY` environment variable in `Program.cs`. | LLM provider tests. | Active | No |
| `OPENAI_API_KEY` | `Program.cs`, `AgentLlmClient`, embedding options | Optional environment fallback | Supplies API key when config omits one. | LLM/provider and code intelligence tests. | Active | No |
| `LOCAL_OPENAI_API_KEY` | `AgentLlmClient` | Optional environment fallback | Supplies local-compatible key when local provider path is used. | LLM/provider tests. | Active | No |
| `LocalTest:WeaviatePrefix` | `EnvironmentInfoDto` | Optional | Empty when not configured. | environment endpoint tests. | Active | No |
| `LocalTest:WorkspaceRoot` | environment safety, disposable run services, test host | Required in LocalTest | LocalTest startup rejects unsafe/missing isolated root. | API environment and disposable-run tests. | Active | No |
| `LocalTest:LogsRoot` | environment safety, run report/evidence roots | Required in LocalTest | LocalTest startup rejects unsafe/missing isolated root. | API environment and run evidence tests. | Active | No |
| `LocalTest:DangerRealRepoWritesEnabled` | environment safety | Optional | Defaults false; LocalTest rejects true. | API environment safety tests. | Active | No |
| `DisposableBuild:WorkspaceRoot` | disposable build/run services | Optional | Falls back to `LocalTest:WorkspaceRoot`. | disposable run tests. | Active | No |
| `DisposableBuild:EvidenceRoot` | run report/evidence services | Optional | Falls back to `LocalTest:LogsRoot`, then service default. | run report/evidence tests. | Active | No |
| `DisposableBuild:*` profile keys | `TicketBuildRunService` | Optional | Reads build profile values by key. | ticket build tests. | Active | No |
| `Embedding:*` | `CodeIntelligenceServiceCollectionExtensions`, `EmbeddingOptions` | Optional | Uses options defaults and `OPENAI_API_KEY` fallback. | code intelligence tests. | Active | No |
| `Weaviate:*` | `CodeIntelligenceServiceCollectionExtensions`, `WeaviateOptions` | Optional unless Weaviate path is enabled | Disabled/empty options prevent active vector client use. | semantic memory/index boundary tests. | Active | No |
| `SemanticRanking:*` | `SemanticRankingOptions` | Optional | Uses option defaults. | semantic ranking tests. | Active | No |
| redacted config summary request values | `RedactedConfigSummaryService` | Optional diagnostic input | Values are summarized as non-sensitive status, redacted values, or safe derived metadata only. | J08 config summary tests. | Diagnostic only | No |

No configuration key was renamed, removed, or given a new default in PR 55.

## DI registration inventory

| Registration area | File path | Runtime or test-only | Status | Changed in PR 55 | Reason for change | Behaviour unchanged |
| --- | --- | --- | --- | --- | --- | --- |
| API composition root | `IronDev.Api/Program.cs` | Runtime | Active | Yes | Registers existing manual agent dependencies required by existing stored manual wrappers. | Yes |
| SQL connection factory | `IronDev.Api/Program.cs` | Runtime | Active | No | Provides `IDbConnectionFactory` for SQL-backed stores. | Yes |
| Project/chat/ticket services | `IronDev.Api/Program.cs` | Runtime | Active | No | Existing API service graph. | Yes |
| Code intelligence services | `IronDev.Infrastructure/DependencyInjection/CodeIntelligenceServiceCollectionExtensions.cs` | Runtime | Active | No | Existing code intelligence, embedding, semantic memory, and retrieval registrations. | Yes |
| Governed tool services | `IronDev.Infrastructure/DependencyInjection/GovernedToolsServiceCollectionExtensions.cs` | Runtime | Active | No | Existing governed tool registry and policy services. | Yes |
| Run report/evidence services | `IronDev.Api/Program.cs` | Runtime | Active | No | Existing file/SQL run evidence graph. | Yes |
| Agent run audit services | `IronDev.Api/Program.cs` | Runtime | Active | No | Existing durable audit store/read/query graph. | Yes |
| Manual critic service | `IronDev.Api/Program.cs` | Runtime | Active | Yes | Existing `StoredManualIndependentCriticAgentService` requires `IManualIndependentCriticAgentService`. | Yes |
| Manual memory-improvement service | `IronDev.Api/Program.cs` | Runtime | Active | Yes | Existing `StoredManualMemoryImprovementAgentService` requires `IManualMemoryImprovementAgentService`. | Yes |
| Manual stored execution validator | `IronDev.Api/Program.cs` | Runtime | Active | Yes | Existing stored wrappers accept `ManualAgentExecutionStoreValidator`; registration makes construction explicit. | Yes |
| API test host overrides | `IronDev.IntegrationTests.Api/ApiTestBase.cs` | Test-only | Active | No | Existing test environment/config overrides. | Yes |

PR 55 fixes the known API DI construction failure by registering existing deterministic manual services and the existing stored execution validator. It does not add an endpoint, controller, scheduler, runner, tool router, source apply path, memory promotion path, or model authority path.

## Service lifetime review

| Service | Lifetime | Reason | Hazard check | Changed in PR 55 |
| --- | --- | --- | --- | --- |
| `IManualIndependentCriticAgentService` -> `ManualIndependentCriticAgentService` | Scoped | Matches stored wrapper and request-oriented API service lifetime. | Does not hold per-run global state; no singleton depending on scoped service. | Yes |
| `IManualMemoryImprovementAgentService` -> `ManualMemoryImprovementAgentService` | Scoped | Matches stored wrapper and request-oriented API service lifetime. | Does not hold per-run global state; no singleton depending on scoped service. | Yes |
| `ManualAgentExecutionStoreValidator` | Scoped | Used by scoped stored wrappers; keeps construction explicit. | Stateless validator; scoped is conservative and avoids singleton/scoped ambiguity. | Yes |
| `IStoredManualIndependentCriticAgentService` -> `StoredManualIndependentCriticAgentService` | Scoped | Existing registration; depends on scoped manual service and scoped audit store. | Construction now has all intended dependencies. | No |
| `IStoredManualMemoryImprovementAgentService` -> `StoredManualMemoryImprovementAgentService` | Scoped | Existing registration; depends on scoped manual service and scoped audit store. | Construction now has all intended dependencies. | No |
| `IAgentRunAuditEnvelopeStore` -> `SqlAgentRunAuditEnvelopeStore` | Scoped | Existing SQL store registration. | No new dependency introduced. | No |

No broad lifetime rewrite was performed.

## Package reference inventory

| Project | Package references | Runtime or test-only | Status | Changed in PR 55 | Notes |
| --- | --- | --- | --- | --- | --- |
| `IronDev.Core/IronDev.Core.csproj` | `CommunityToolkit.Mvvm` | Runtime/library | Active | No | Retained; no package cleanup without proof. |
| `IronDev.Infrastructure/IronDev.Infrastructure.csproj` | `BCrypt.Net-Next`, `Dapper`, `Markdig`, `Microsoft.Build.Framework`, `Microsoft.Build.Locator`, `Microsoft.CodeAnalysis.CSharp.Workspaces`, `Microsoft.CodeAnalysis.Workspaces.MSBuild`, `Microsoft.Data.SqlClient`, `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `OpenAI`, `Weaviate.Client` | Runtime | Active/uncertain by area | No | Retained; packages map to existing SQL, build, markdown, LLM, and vector/index areas. |
| `IronDev.Api/IronDev.Api.csproj` | `BCrypt.Net-Next`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.OpenApi`, `Serilog.AspNetCore`, `Serilog.Sinks.File`, `Swashbuckle.AspNetCore` | Runtime | Active | No | Retained; no API package behaviour change. |
| `IronDev.IntegrationTests/IronDev.IntegrationTests.csproj` | `Dapper`, `Microsoft.Extensions.Configuration.Json`, `Microsoft.Extensions.DependencyInjection`, `Moq`, `MSTest` | Test-only | Active/uncertain | No | Retained even where pruning warnings exist; removal needs separate proof. |
| `IronDev.IntegrationTests.Api/IronDev.IntegrationTests.Api.csproj` | `BCrypt.Net-Next`, `Dapper`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Data.SqlClient`, `Microsoft.NET.Test.Sdk`, `MSTest.TestAdapter`, `MSTest.TestFramework`, `coverlet.collector` | Test-only | Active | No | Retained for API host and SQL integration tests. |

No package reference was removed or upgraded in PR 55. NuGet pruning warnings for `Microsoft.Extensions.Configuration.Json` and `Microsoft.Extensions.DependencyInjection` in `IronDev.IntegrationTests` remain intentionally unresolved because package removal is not proven safe in this PR.

## External service clients

| Client/service | File path | Runtime or test-only | Status | Changed in PR 55 | Boundary note |
| --- | --- | --- | --- | --- | --- |
| OpenAI/local/Ollama LLM services | `IronDev.Infrastructure/Services/*LlmService.cs`, `IronDev.Api/Program.cs` | Runtime | Active | No | Existing provider selection unchanged. Model output remains advisory only. |
| Agent LLM client | `IronDev.Infrastructure/Services/Agents/AgentLlmClient.cs` | Runtime | Active | No | Existing environment key fallback unchanged. |
| Weaviate semantic memory service | `IronDev.Infrastructure/Services/SemanticMemory/WeaviateSemanticMemoryService.cs` | Runtime | Active but retrieval/index only | No | Vector/index/retrieval is not truth, authority, approval, or promotion. |
| Weaviate memory indexer | `IronDev.Infrastructure/AgentMemory/*` | Runtime | Active but indexing only | No | Index writing does not create memory authority. |
| SQL clients/stores | `IronDev.Infrastructure/**/*Sql*.cs`, `IronDev.Api/Program.cs` | Runtime | Active | No | SQL remains source of truth. |

No external client was added, removed, upgraded, or newly exposed in PR 55.

## Test-only registrations and fixtures

| Area | File path | Status | Changed in PR 55 | Boundary note |
| --- | --- | --- | --- | --- |
| API host test setup | `IronDev.IntegrationTests.Api/ApiTestBase.cs` | Active | No | Provides test configuration only. |
| integration DB/schema support | `IronDev.IntegrationTests/IntegrationTestBase.cs` and schema helpers | Active | No | Existing SQL test reset support unchanged. |
| manual agent test fakes | `IronDev.IntegrationTests/Agents/*` | Active | No | Test-only fakes do not leak into API runtime registrations. |
| PR55 DI construction guard | `IronDev.IntegrationTests/BackendConfigurationDependencyTests.cs` | Test-only | Yes | Uses in-memory audit store only inside test to prove constructor shape. |

## Known configuration/dependency debt

| Issue | Affected tests or services | Why it matters | PR 56 posture | Planned follow-up |
| --- | --- | --- | --- | --- |
| Broad governance/memory/architecture lanes still fail in full solution runs | legacy governance, memory boundary, L4 report, architecture scans | These are broader contract-drift lanes, not PR55 DI wiring. | Must be fixed or explicitly listed as freeze exceptions. | PR 56 freeze report or targeted cleanup PRs. |
| Legacy runtime DDL/bootstrap ownership exceptions | runtime store/bootstrap areas listed in PR 51 inventory | Runtime schema ownership remains noisy. | Freeze exception unless fixed before PR 56. | Runtime bootstrap ownership cleanup. |
| Uncertain package references | especially test package pruning warnings | Removing packages without proof could break test lanes. | Allowed as documented uncertainty. | Post-freeze or dedicated package cleanup. |
| Uncertain config keys | provider/vector/build keys that are active but not exhaustively tested in this PR | Risky to rename or delete before freeze. | Leave in place. | Dedicated config-contract cleanup if needed. |
| Ugly names left from previous inventories | naming inventory and entity/table inventory | Renaming before freeze can break serialized or SQL contracts. | Documented freeze exception or post-freeze cleanup. | Naming migration only with proof. |

## PR 55 changes

Changed in this PR:

- `IronDev.Api/Program.cs` registers existing manual critic and memory-improvement services required by existing stored manual wrappers.
- `IronDev.Api/Program.cs` registers existing `ManualAgentExecutionStoreValidator` explicitly for the stored wrappers.
- `Docs/BACKEND_CONFIGURATION_DEPENDENCY_INVENTORY.md` records configuration, DI, packages, external clients, test registrations, and remaining debt.
- `IronDev.IntegrationTests/BackendConfigurationDependencyTests.cs` guards the inventory and construction boundary.

Not changed in this PR:

- configuration keys
- package references
- SQL schema
- stored procedure result shapes
- API endpoints
- CLI commands
- UI
- source apply logic
- memory promotion logic
- approval logic
- tool execution logic
- vector/index behaviour
- agent capability
