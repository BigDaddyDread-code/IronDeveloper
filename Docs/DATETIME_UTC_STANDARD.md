# Date/time UTC Standard

IronDev is designed for international use. Product evidence must be stable across time zones, developer machines, build agents, imported GitHub data, trace viewers, and future remote users.

Core rule:

```text
Persist UTC.
Transmit UTC.
Display UTC-aware dates.
```

## Persistence Rules

- Store product timestamps as UTC.
- Prefer `DateTimeOffset.UtcNow` for new product/audit timestamps.
- `DateTime.UtcNow` is acceptable where existing models use `DateTime`.
- Do not use `DateTime.Now` or `DateTimeOffset.Now` for product/audit timestamps.
- Database defaults should use UTC sources such as `SYSUTCDATETIME()`.
- Existing legacy fields named `CreatedDate`, `UpdatedDate`, `StartedAt`, `EndedAt`, or `CreatedAt` are treated as UTC unless explicitly documented otherwise. Rename them to `CreatedUtc`, `UpdatedUtc`, `StartedUtc`, `FinishedUtc`, or equivalent when a compatible DTO/model migration is practical.

## API And Client Rules

- API DTO timestamp fields should use explicit UTC naming where practical, such as `CreatedUtc`, `UpdatedUtc`, `ImportedUtc`, `VersionCreatedUtc`, `LastIndexedUtc`, `StartedUtc`, and `FinishedUtc`.
- JSON should serialize timestamps as ISO 8601 UTC.
- API responses must not introduce ambiguous local-only timestamps.
- Client models should preserve UTC timestamp values and names.

## CLI And Report Rules

- CLI JSON output must preserve UTC timestamps from the API.
- CLI markdown reports must label UTC timestamps clearly.
- Reports should prefer labels such as `CreatedUtc`, `StartedUtc`, `FinishedUtc`, `ExportedUtc`, and `ImportedUtc`.
- Do not format evidence timestamps as local-only strings.

## UI Rules

- UI may show friendly local display where useful.
- UI must expose UTC in secondary text, metadata, or tooltip for product evidence timestamps.
- Do not display ambiguous dates such as `25/05/2026` without context.
- Use the shared `DateTimeDisplay` helper instead of scattering date formatting in XAML or view models.

Recommended display pattern:

```text
Primary: 25 May 2026, 14:32
Tooltip: 2026-05-25T02:32:00Z UTC
Compact: Updated 12m ago - 2026-05-25 02:32 UTC
```

## Current Helper

Use `IronDev.Core.Time.DateTimeDisplay`:

- `ToUtcMetadata(...)`
- `ToUtcTooltip(...)`
- `ToLocalDisplay(...)`
- `ToRelativeDisplay(...)`
- `ToCompactMetadata(...)`

## Validation

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Assert-UtcDateTimeStandard.ps1
```

This guard fails when product source introduces `DateTime.Now` or `DateTimeOffset.Now`.

Also run focused tests:

```powershell
dotnet test .\IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter FullyQualifiedName~DateTimeUtcStandardTests -p:UseSharedCompilation=false -nr:false
```

## Follow-up Scope

Future slices should apply this standard to Chat, tickets, traces, run reports, imported external references, and any API/client DTOs that still expose legacy timestamp names.
