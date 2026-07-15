# CLN-41 Reset and Support Bundle Receipt

**Status:** Historical receipt

**Date:** 15 July 2026

## Outcome

One guarded LocalTest entrypoint now resets the test tenant/projects, resets a contained disposable workspace, or exports a bounded redacted support bundle that retains correlation IDs.

## Evidence

- `tools/localtest/Invoke-LocalTestResetAndSupport.ps1`
- `Scripts/ci/verify-localtest-reset-support-contract.ps1`
- `Scripts/ci/run-governance-boundary-ci.ps1`
- `Docs/cleanup/LOCALTEST_RESET_AND_SUPPORT_BUNDLE.md`
- Behavioral contract verification exported and reopened a bounded fixture bundle, retained its correlation ID, proved representative secret shapes absent, and rejected unsafe reset/output targets.

## Boundary

Reset is LocalTest-only and explicitly confirmed. Support export is diagnostic evidence, excludes secrets, and grants no repair, cleanup, apply, or release authority.
