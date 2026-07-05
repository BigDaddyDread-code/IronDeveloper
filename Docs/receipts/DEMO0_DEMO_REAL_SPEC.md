# DEMO-0 - Demo-Real Specification Receipt

## Purpose

Record the DEMO-0 documentation slice that adds the canonical v0.1 Local Alpha demo-real contract.

## Files Changed

```text
Docs/release/v0.1-local-alpha/DEMO_REAL_SPEC.md
Docs/receipts/DEMO0_DEMO_REAL_SPEC.md
```

## Scope

DEMO-0 defines the demo-real standard:

```text
fixture is allowed
fake outcome is forbidden
BookSeller is the release fixture
demo seed must use product APIs/governed paths
UI must show backend truth
repeatability is required
restart persistence is required
implementation remains split into small PRs
```

## Boundary

This PR is documentation only.

It does not:

```text
create demo seed scripts
start API
start UI
start SQL
start Weaviate
create tickets
start governed runs
record approvals
request continuation
apply source
write receipts from runtime
insert SQL state
modify frontend routes
modify backend routes
grant release readiness
grant merge readiness
grant deployment readiness
```

## Non-Authority Statement

The demo-real spec is planning evidence. It is not approval, policy satisfaction, source-apply authority, release authority, deployment readiness, or proof that the demo already works.

## Review Line

BookSeller can be a fixture. The outcome must still be earned by the system.

## Killjoy Line

A fixture is acceptable. A fake outcome is not.

## Validation

To be recorded by the PR:

```text
git diff --check
git diff --cached --check
```

No build or test execution is required for this docs-only slice.

## Next Intended Slice

DEMO-1 - API-driven demo seed command.

Review line: Demo seed may replay history; it may not invent authority.
