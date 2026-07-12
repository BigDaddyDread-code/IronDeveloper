# LocalTest Test Data And Seed Contract

**Status:** Canonical test-data contract

**Last reviewed:** 13 July 2026

**Programme slice:** CLN-10

## Authority

[`tools/localtest/localtest-seed-contract.json`](../../tools/localtest/localtest-seed-contract.json) is the machine-readable source for stable LocalTest identities. The reset, launcher, UI launcher, and smoke scripts consume it through `tools/localtest/localtest-seed-contract.ps1`.

`tools/localtest/localtest-seed.sql` remains the SQL implementation. A reset is successful only after contract-generated SQL verifies the resulting database rows. Static boundary tests also reject drift between the manifest, scripts, and SQL seed.

## Environment Boundary

| Field | Contract |
| --- | --- |
| Environment | `LocalTest` |
| Database | `IronDeveloper_Test` |
| Reset | Allowed only through the LocalTest reset path |
| Production enablement | `false` |
| Workspace | Exactly `C:\IronDevTestWorkspaces` |
| Logs | Exactly `C:\IronDevTestLogs` |
| Ordinary Development data | Never cleared or seeded by this contract |

The supported command is:

```powershell
.\tools\localtest\start-pr-manual-test.ps1 -Reset -FreshSession -BrowserOnly
```

The reset recreates the bounded database world and all three disposable fixture repositories. Running it repeatedly produces the same stable identities and replaces mutable LocalTest journey state. It is destructive only inside the configured LocalTest database and fixture root.

## Credentials And Tenant

| Identity | ID | Value |
| --- | ---: | --- |
| Tenant | 1 | `Local Test Tenant` (`local-test`) |
| User | 1 | `bob@irondev.local` / `Bob Developer` |
| Password | - | `change-me-local-only` |
| Tenant role | - | `Owner` |

These credentials are intentionally committed for isolated local testing. They are not a Development, shared-host, staging, or production credential.

## Projects

| Key | Project ID | Name | Fixture directory | Required journey |
| --- | ---: | --- | --- | --- |
| `baseline` | 1 | IronDev Local Test Project | `IronDevLocalTestProject` | Tiny deterministic baseline for every PR manual test |
| `bookseller` | 2 | BookSeller Test Fixture | `BookSellerTestFixture` | Provisioning, build, review, apply, and recovery |
| `setup` | 3 | IronDev Setup Test Project | `IronDevSetupTestProject` | Guided setup and honest refusal states |

Callers that do not supply a project ID use the manifest's `baseline` project. They do not embed project ID 1 independently.

## Seeded Work

| Ticket ID | Project ID | Title | Initial state |
| ---: | ---: | --- | --- |
| 3001 | 1 | Add Governed Tool Architecture | Ready |
| 3002 | 1 | Wire Start Sandbox Run | In Review |
| 3003 | 1 | Improve Ticket Workspace UI | Draft |
| 3101 | 2 | Add Search By Author | Ready |

| Run ID | Project ID | Ticket ID | Initial state |
| --- | ---: | ---: | --- |
| `localtest-run-ticket-3002` | 1 | 3002 | Completed disposable evidence fixture |

The run is review evidence, not proof that a new execution happened during the current test. Tests that exercise execution must record the new backend-owned run ID.

## Known Artifact IDs

| Kind | IDs |
| --- | --- |
| Project channels | 101, 102, 103 |
| Channel messages | 10001, 10002, 10003 |
| Direct chat sessions | 4001, 4002 |
| Direct chat messages | 5001, 5002 |
| Project documents | 1001, 1002, 1003 |
| Document versions | 2001, 2002, 2003 |

No patch, apply, approval, or release artifact is pre-seeded. Those IDs must come from the governed action exercised by the test; inventing one would be fake product evidence.

## Change Rule

Change stable identities in the JSON manifest first, update the SQL seed in the same PR, and run:

```powershell
dotnet test .\IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter FullyQualifiedName~BlockC12LocalTestSafetyRegressionTests
.\tools\localtest\reset-localtest-data.ps1
```

The first command proves static consumption and drift boundaries. The reset proves idempotence, target safety, and actual seeded SQL truth.

## Killjoy Line

A familiar project name in a local database is not a fixture contract; a bounded manifest, a reset, and post-seed verification are.
