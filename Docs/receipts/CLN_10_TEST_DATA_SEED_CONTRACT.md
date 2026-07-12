# CLN-10 Test Data And Seed Contract Receipt

**Date:** 13 July 2026

**Branch:** `cleanup/cln-10-test-data-seed-contract`

## Scope

Created one machine-readable identity contract for the isolated LocalTest tenant, user, projects, tickets, run, artifacts, and credentials.

## Enforcement

- Supported launchers and smoke tooling consume credentials and the baseline project from the JSON manifest.
- Fixture directories are selected by contracted project key.
- The reset refuses any database that does not exactly match `IronDeveloper_Test` and retains explicit test-only path guards.
- The manifest is resettable and explicitly production-disabled.
- Every reset runs generated SQL assertions against the actual tenant, user, project paths, tickets, run, and artifact rows.
- `BlockC12LocalTestSafetyRegressionTests` rejects missing contract classes, hardcoded consumer credentials, and SQL identity drift.

## Truth Boundary

The pre-seeded completed run is stable review evidence. No patch, apply, approval, or release artifact is pre-seeded; tests must obtain those IDs from the backend action they exercise.

## Evidence

- PowerShell parser: 7 LocalTest contract consumers passed.
- Contract load: 3 projects, 4 tickets, 1 run, 16 known artifacts, and 30 generated SQL validation statements.
- `BlockC12LocalTestSafetyRegressionTests`: 12/12 passed.
- Documentation contract: 624 documents passed.
- Full LocalTest schema reset: passed with `PASS LocalTest seed contract.`
- Immediate `-SkipSchema` reseed: passed with the same contracted identities and post-seed assertion.

## Result

LocalTest data is deterministic, environment-bounded, safe to reset within its declared fence, and not silently production-enabled.
