# GitHub CI Failure Inventory

**Status:** Active cleanup inventory
**Baseline:** `main` at `e94e58a3`
**Observed:** 12 July 2026

This inventory records confirmed GitHub Actions failures. A warning, local inconvenience, or historical failure is not listed as an active CI defect without current run evidence.

## Active Baseline

| Lane | Latest observed run | Result | Classification | Disposition |
| --- | --- | --- | --- | --- |
| `fast-unit-ci` | `29180115043` | Passed | None | No recovery work. |
| `frontend-contract-ci` | `29180115057` | Passed | None | No recovery work. |
| `sql-integration-ci` | `29180115132` | Passed | None | No recovery work. |
| `full-sql-integration-ci` | `29180115067` | Passed | None | No recovery work. |
| `governance-boundary-ci` | `29180115069` | Passed | None | No recovery work. |
| `skeleton-run-ci` | `29180115063` | Failed | `TestExpectationDrift` | CLN-00 updates the exact boundary-test allow-list. |

## CLN-00 Finding

| Field | Evidence |
| --- | --- |
| Failing job | `SkeletonRun governed-loop lane / SkeletonRun lane` |
| Run | `29180115063` |
| First meaningful error | `TestAuthoringContract_HasNoChannelForTheBuildersDiff` expected five request properties and observed six. |
| Local reproduction | `./Scripts/ci/run-skeleton-run-ci.ps1` |
| Introducing change | `977000fe` added `TenantId` to `SkeletonTestAuthoringRequest`. |
| Product intent | The Tester receives tenant/project scope and ticket requirement data, never the builder proposal, diff, patch, file content, or change content. |
| Narrow correction | Add `TenantId` to the exact allowed-property set. Preserve all forbidden-channel assertions. |
| Runtime behavior | Unchanged. The scoped runtime contract remains intact. |

## Classification Rules

Confirmed failures use one of:

- `ProductBug`
- `TestExpectationDrift`
- `MissingSeedData`
- `MissingCiLane`
- `EnvironmentOnlyFailure`
- `FlakyOrTiming`
- `ContractMismatch`
- `GeneratedContractDrift`
- `Unknown`

An `Unknown` failure remains open. It cannot be recast as environment noise without evidence.
