# UI Test Fixtures

Deterministic UI tests must seed product state through `IronDev.Api`. They must not write directly to SQL, Infrastructure services, local JSON files, or browser storage as the source of truth.

## Required Seed Scenarios

The future UI shell needs seeded scenarios for:

- User with one tenant
- User with multiple tenants
- Project with tickets
- Project with documents
- Generated ticket review queue
- Build/run report sample
- Memory search sample

## Seed Contract

Seed data should be created by authenticated API calls or dedicated test-only API seed endpoints guarded to Development/Test environments.

Required properties:

- Deterministic slugs and IDs where the UI journey needs stable selectors.
- Repeatable setup and teardown.
- Tenant-aware state.
- No dependency on local machine paths unless the scenario explicitly tests local import.
- Clear mapping from fixture scenario to journey spec.

## Missing Endpoints

The following seed endpoints do not exist yet and must be added before these journeys can become real passing tests:

- TODO(#68/#69): create a test-only seed endpoint for a user with one tenant.
- TODO(#68/#69): create a test-only seed endpoint for a user with multiple tenants.
- TODO(#68/#69): create a test-only seed endpoint for a project with tickets.
- TODO(#68/#69): create a test-only seed endpoint for a project with documents.
- TODO(#68/#69): create a test-only seed endpoint for generated ticket review queue state.
- TODO(#68/#69): create a test-only seed endpoint for build/run report samples.
- TODO(#68/#69): create a test-only seed endpoint for memory search samples.
