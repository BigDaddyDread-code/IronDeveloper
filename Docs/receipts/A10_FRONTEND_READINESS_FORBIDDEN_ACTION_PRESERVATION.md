# A10 - Frontend Readiness Forbidden-Action Preservation Tests

## Review Line

A forbidden action hidden from the frontend is a bypass invitation.

## Purpose

A10 adds frontend-readiness forbidden-action preservation tests. It proves frontend-readiness forbidden actions, missing evidence, authority warnings, read-state warnings, freshness warnings, and read-only boundary flags remain visible to frontend consumers.

## Files Changed

- `IronDev.IntegrationTests/BlockA10FrontendReadinessForbiddenActionPreservationTests.cs`
- `Docs/receipts/A10_FRONTEND_READINESS_FORBIDDEN_ACTION_PRESERVATION.md`

## Boundary

A10 adds frontend-readiness forbidden-action preservation tests.
It does not add a new backend read adapter.
It does not add UI.
It does not create approval.
It does not satisfy policy.
It does not grant source apply authority.
It does not execute source apply, rollback, commit, push, PR, merge, release, deployment, memory promotion, or workflow continuation.
Forbidden actions, missing evidence, authority warnings, read-state warnings, and freshness warnings must remain visible to frontend consumers.

## Preservation Behavior

- Forbidden actions survive API envelope wrapping.
- Missing evidence survives API envelope wrapping.
- Authority warnings survive API envelope wrapping.
- Read-state warnings survive available, not-found, empty, redacted, unavailable, invalid, expired, stale, not-visible, and unknown states.
- Freshness warnings survive current, stale, expired, unknown, and not-applicable markers.
- Compact mode adds its compact warning without hiding forbidden actions, missing evidence, warnings, read state, freshness, or boundary.
- Canonical source data wins over fallback for forbidden actions, missing evidence, and authority warnings.
- Redacted, stale, expired, invalid, unavailable, and not-visible canonical states are not replaced by cleaner-looking fallback data.
- True not-found may fall through to fallback only when read-state and boundary warnings remain visible.

## No-Authority Behavior

Forbidden action text is not an action surface.
Next safe action text is guidance only.
Missing evidence is not approval.
Authority warning text is not authority.
Read-state warning text is not authority.
Freshness warning text is not authority.
Read-only frontend readiness output cannot create approval, satisfy policy, execute, mutate source, commit, push, create PRs, merge, release, deploy, promote memory, or continue workflow.

## Validation

- Focused A10 tests: 68/68 passed.
- A08/A09 compatibility tests: 137/137 passed.
- A01-A10 read adapter stack: 454/454 passed.
- Frontend readiness lane plus A01-A10: 642/642 passed.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Review Traps

Reject this slice if:

- forbidden actions disappear in compact mode
- missing evidence disappears in compact mode
- authority warnings disappear in compact mode
- read-state warnings disappear in compact mode
- freshness warnings disappear in compact mode
- fallback overwrites canonical forbidden actions, missing evidence, authority warnings, redacted data, stale data, expired data, invalid data, unavailable data, or not-visible data
- any read state enables approval, policy satisfaction, source apply, commit, push, PR creation, merge, release, deployment, memory promotion, or workflow continuation
- UI files are touched
- mutation endpoints are added
- executors are wired
- raw payload readers are added
- action request creation is added
- validation refresh, run, retry, or repair is added

## Intentionally Unwired

A10 does not add a new read adapter, durable store, SQL migration, UI, action request flow, validation execution, validation refresh, source apply execution, rollback execution, commit, push, PR creation, merge, release, deployment, memory promotion, or workflow continuation.

## Killjoy

If the UI cannot see the boundary, the UI will eventually bypass it.
