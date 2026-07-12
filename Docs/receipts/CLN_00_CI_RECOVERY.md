# CLN-00 CI Recovery Receipt

**Status:** Local lane passed; GitHub lane verification required
**Baseline:** `main` at `e94e58a3`
**Date:** 12 July 2026

## Purpose

Restore the failing `skeleton-run-ci` lane without weakening Tester independence, tenant/project scope, or authority boundaries.

## Review Line

The Tester request may contain scoped requirement data. It must not contain anything produced by the Builder.

## Killjoy Line

A green lane achieved by deleting an isolation assertion would conceal the defect rather than recover CI truth.

## Scope

Allowed files:

- `IronDev.IntegrationTests/SkeletonRunTests.cs`
- `Docs/cleanup/GITHUB_CI_FAILURE_INVENTORY.md`
- `Docs/receipts/CLN_00_CI_RECOVERY.md`

Forbidden files:

- runtime services and models;
- workflow authority or approval logic;
- CI filters and workflow definitions;
- generated contracts;
- unrelated tests.

## Diagnosis

GitHub Actions run `29180115063` failed one of 180 selected SkeletonRun tests:

```text
TestAuthoringContract_HasNoChannelForTheBuildersDiff
Expected property count: 5
Actual property count: 6
```

Commit `977000fe` intentionally added `TenantId` to `SkeletonTestAuthoringRequest` so model resolution and test authoring remain tenant-scoped. The exact allow-list test was not updated with that contract change.

Classification: `TestExpectationDrift`.

## Behavior

Preserved:

- tenant and project scope remain in the Tester request;
- Tester input remains limited to scope and ticket requirements;
- proposal, diff, change, content, file, and patch channels remain forbidden;
- test evidence remains evidence, not approval.

Intentionally changed:

- the boundary test now recognizes `TenantId` as allowed scope data;
- the failure message distinguishes scoped requirement data from Builder output.

Runtime behavior changed: **No**.

## Verification

Executed:

```powershell
./Scripts/ci/run-skeleton-run-ci.ps1
```

Result:

```text
Total: 180
Passed: 180
Failed: 0
```

The first local attempt did not reach the tests because the running LocalTest API held shared Debug output DLLs. After stopping only the API listener on port 5000, the unchanged CI script completed successfully. This is an `EnvironmentOnlyFailure` observation for operational cleanup; it is not the cause of the GitHub lane failure and no process-killing behavior was added to CI.

GitHub verification:

- `skeleton-run-ci` passes on the CLN-00 pull request;
- the remaining required lanes do not regress.

## Inventory

The live lane baseline and failure evidence are recorded in `Docs/cleanup/GITHUB_CI_FAILURE_INVENTORY.md`.

## Next Cleanup Slice

`CLN-01` maps every workflow, job, script, selected suite, dependency, artifact, and quarantine behavior from executable repository truth.
