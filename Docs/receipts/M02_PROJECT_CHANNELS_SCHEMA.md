# M02 Project Channels Schema Receipt

## Purpose

M02 adds the durable project-channel schema foundation and Core channel contract models for the future Channels feature.

Project channels are collaboration state.

Channels are where people discuss.

Gates are where authority happens.

## Required Boundary Line

Project channels are collaboration state. Channel messages, pins, summaries, context links, and assistant answers do not create approval, authority, policy satisfaction, source apply, workflow continuation, memory promotion, release readiness, or deployment readiness.

## Files Changed

- `Database/migrate_project_channels.sql`
- `IronDev.Core/Channels/ProjectChannelModels.cs`
- `IronDev.IntegrationTests/Governance/ProjectChannelsSchemaTests.cs`
- `Docs/receipts/M02_PROJECT_CHANNELS_SCHEMA.md`
- `Docs/testing/INTEGRATION_TEST_CATEGORIES.md`
- `Docs/testing/SLOW_TEST_QUARANTINE_REGISTER.md`
- `IronDev.IntegrationTests/Governance/SlowQuarantineCategoryContractTests.cs`

The slow/quarantine register and contract changed only because M02 adds one SQL-backed `RequiresRealDatabase` / `LongRunning` schema test class.

## What Was Added

- `dbo.ProjectChannels`
- `dbo.ProjectChannelMembers`
- `dbo.ProjectChannelMessages`
- `dbo.ProjectChannelMessageContextLinks`
- `dbo.ProjectChannelAssistantTurns`
- `dbo.ProjectChannelMessageReads`
- `dbo.ProjectChannelPins`
- Core constants for channel kinds, roles, statuses, message roles, link kinds, and assistant turn statuses.
- Core entity-style models for channel headers, membership, messages, links, assistant turns, read markers, and pins.

## SQL Boundary

M02 proves the first channel migration can create the channel tables, indexes, foreign keys, check constraints, active-slug uniqueness, and no-authority boundary defaults.

The child tables use tenant/project/channel composite foreign keys so child rows cannot silently point at a channel in a different project scope.

`dbo.Projects` receives `UQ_Projects_IdTenant` only to support a tenant/project composite foreign key for channel rows.

## Authority Boundary

A channel message is not approval.

A channel pin is not policy.

A context link is navigation only.

A read marker is unread-count convenience only.

A channel assistant turn is advisory context only.

An assistant answer is not authority.

A system notice is not workflow continuation.

An event link does not grant authority over the linked object.

Channel rows do not satisfy approval gates.

Channel rows do not satisfy policy.

Channel rows do not authorize source apply.

Channel rows do not authorize memory promotion.

Channel rows do not create release readiness.

Channel rows do not create deployment readiness.

## What Was Intentionally Not Built

M02 does not add API endpoints.

M02 does not add UI.

M02 does not implement channel list/read/create services.

M02 does not implement message posting services.

M02 does not implement Ask IronDev.

M02 does not call the LLM.

M02 does not add real-time sockets.

M02 does not add notifications.

M02 does not add reactions.

M02 does not add emoji-as-approval.

M02 does not add workflow commands in chat.

M02 does not add approval from chat.

M02 does not add release from chat.

M02 does not add deployment from chat.

M02 does not add memory promotion from chat.

M02 does not migrate existing ProjectChatSessions.

M02 does not bridge existing ChatMessages into channels.

M02 does not change current project chat behavior.

## Tests Added

- `ProjectChannelContracts_ExposeConstantsEntitiesAndBoundaryText`
- `ProjectChannelMigration_CreatesTablesForeignKeysChecksAndIndexes`
- `ProjectChannelMigration_DefaultBoundariesDenyAuthority`
- `ProjectChannelMigration_EnforcesUniqueActiveSlugPerTenantProject`
- `ProjectChannelMigration_AllowsArchivedSlugReuseWithoutTreatingArchiveAsAuthority`
- `ProjectChannelMigration_EnforcesTenantProjectScopedChildRows`
- `ProjectChannelMessages_EnforceRolesAndUserAuthorRule`
- `ProjectChannelContextPinsReads_AreConvenienceOnly`
- `ProjectChannelSchema_DoesNotIntroduceReactionsUiApiOrAssistantAutoAnswerSurface`
- `Receipt_RecordsM02ScopeAndAuthorityBoundary`

## Validation

- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter FullyQualifiedName~ProjectChannelsSchemaTests --logger "console;verbosity=minimal"`: passed, 10/10.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~ProjectChannelsSchemaTests|FullyQualifiedName~IntegrationTestCategoryContractTests|FullyQualifiedName~SlowQuarantineCategoryContractTests" --logger "console;verbosity=minimal"`: passed, 27/27.
- `dotnet test IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --no-build --filter FullyQualifiedName~BlockC11SecretScanningRegressionTests --logger "console;verbosity=minimal"`: passed, 9/9.
- `dotnet restore IronDev.slnx`: passed with existing warnings.
- `dotnet build IronDev.slnx --no-restore`: passed, 0 errors / 7 warnings.
- GitHub CI: tracked by the draft PR checks and PR body; this receipt records the local validation run before PR creation.

## Next Intended Slice

M03 - Channel read/list/create API.

Review line: Channels let humans collaborate and let IronDev answer in context. They do not let chat become authority.

Killjoy line: If a channel can accidentally approve work, the chat feature has become an authority leak.
