# C06 - JWT Secret Configuration Hardening

## Summary

JWT config may identify auth settings. It must not carry the signing secret.

JWT signing authority must not live in committed appsettings. C06 moves the signing secret to environment/secret configuration and fails closed when it is missing.

This PR removes committed JWT signing-key material from API runtime config and makes API auth setup and token creation use one shared signing-key resolver.

## Boundary

JWT signing key material is authority to mint accepted API tokens.
Committed appsettings may document required JWT configuration, but it must not hold signing authority.

A configured JWT secret is not approval, policy satisfaction, tenant authority, execution permission, merge readiness, release readiness, deployment readiness, or workflow continuation.

Environment secret availability is not authentication success.
A configured signing key is not API authority by itself.
A minted JWT is not tenant authority unless tenant claims and backend validation permit it.
Token validation remains backend-owned.
Frontend and CLI may present tokens. They do not create token authority.

C06 does not redesign authentication, change token claims, change issuer/audience semantics, alter tenant authorization, change frontend auth behavior, or create any new authority path.

## Configuration Scope

Removed JWT signing-key material from committed API runtime config:

- `IronDev.Api/appsettings.json`
- `IronDev.Api/appsettings.LocalTest.json`
- `IronDev.IntegrationTests.Api/appsettings.Test.json`

Kept non-secret JWT metadata in committed config:

- `Jwt:Issuer`
- `Jwt:Audience`
- `Jwt:ExpiryMinutes`

The API test host injects a test-only signing key in memory. The committed test appsettings no longer carries the old placeholder signing key.

The backend configuration inventory now documents that JWT signing key material must be supplied through environment/secret configuration instead of committed appsettings.

## Runtime Behavior

`JwtSigningKeyResolver` resolves the JWT signing key with deterministic precedence:

1. `Jwt:Key` from normal configuration providers, including `Jwt__Key`
2. `IRONDEV_JWT_KEY`
3. fail closed

Missing key failure:

`JWT signing key is not configured. Set Jwt__Key or IRONDEV_JWT_KEY outside committed appsettings.`

Short key failure:

`JWT signing key must be at least 32 characters.`

The key value is not logged, returned, placed in receipts, or included in exception messages.

`Program.cs` and `JwtTokenService` both use the shared resolver.

Issuer, audience, expiry, subject, email, display-name, and tenant claims remain unchanged.

## Forbidden Mutation Paths

- no auth redesign
- no endpoint changes
- no controller changes
- no tenant authorization changes
- no auth policy changes
- no frontend runtime changes
- no generated client changes
- no OpenAPI snapshot changes
- no SQL migrations
- no SQL store/procedure changes
- no governance authority changes
- no source apply
- no commit
- no push
- no PR creation/update
- no release
- no deployment
- no memory write or promotion
- no workflow continuation
- no runtime-generated signing key fallback
- no hard-coded default signing key fallback

## Validation

- Focused C06 tests: 13/13 passed.
- API auth/token tests (`AuthControllerTests|TenantControllerTests`): 13/13 passed.
- API boundary lane: 38/38 passed.
- CLI boundary lane: 41/41 passed.
- Governance-boundary CI script:
  - B-series profile boundary tests: 133/133 passed.
  - BQ-BU compatibility boundary tests: 80/80 passed.
  - API boundary tests: 38/38 passed.
  - CLI boundary tests: 41/41 passed.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed with normal LF/CRLF warnings.
- `git diff --cached --check`: passed with normal LF/CRLF warnings.

## Review Traps

Reject this PR if:

- `appsettings.json` still contains `Jwt:Key`.
- The old placeholder key remains in committed runtime config.
- Missing key falls back to a hard-coded default.
- Runtime generates a random key automatically.
- Tests pass by injecting a production-looking committed secret.
- The key is logged.
- The key appears in exception messages.
- The key appears in API responses.
- Issuer, audience, or lifetime validation is weakened.
- Tenant claim behavior changes.
- Endpoints or controllers are modified.
- Frontend runtime behavior is changed.
- Generated clients or OpenAPI snapshots are changed.
- Auth redesign is smuggled in.
- SQL, governance, memory, source-apply, release, or deploy paths are touched.
- C06 is treated as broader security completion.

## Killjoy

A secret in config is not development convenience; it is leaked signing authority.
