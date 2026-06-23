# C07 - JWT Startup Validation

## Summary

JWT startup validation checks that signing-key configuration is present, safe-length, and sourced outside committed appsettings. It does not create signing authority.

C07 adds an explicit startup validation path for the JWT signing key after C06 moved signing authority out of committed config. The API host now validates the signing-key source and safety before `builder.Build()`.

## Boundary

JWT signing key material is authority to mint accepted API tokens.

Startup validation is not signing authority.
Source metadata is not signing authority.
A configured JWT secret is not approval, policy satisfaction, tenant authority, execution permission, merge readiness, release readiness, deployment readiness, or workflow continuation.

Frontend and CLI may present tokens. They do not create token authority.
Token validation remains backend-owned.

C07 does not redesign authentication, change token claims, change issuer/audience/lifetime semantics, alter tenant authorization, add endpoints, change frontend auth behavior, modify generated clients, or introduce key rotation.

## Startup Validation Scope

Startup validation verifies that the JWT signing key:

- is present
- is at least 32 characters
- is not the old committed placeholder
- is not loaded from committed `appsettings*.json` when provider metadata makes that detectable
- comes from a known accepted source

Accepted sources:

- `Jwt:Key` from safe configuration providers, including environment-backed `Jwt__Key`
- `IRONDEV_JWT_KEY`
- test-only in-memory configuration in API tests

Missing, short, placeholder, committed-appsettings, or unknown-source keys fail closed before the API host is built.

## Source Metadata

JWT key source metadata is diagnostic evidence only. It must never expose the signing key.

C07 records internal source metadata as:

- `JwtSigningKeySource.Configuration`
- `JwtSigningKeySource.IronDevJwtKeyEnvironment`
- `JwtSigningKeySource.Missing`

Length state is classified as:

- `JwtSigningKeyLengthClassification.Valid`
- `JwtSigningKeyLengthClassification.TooShort`
- `JwtSigningKeyLengthClassification.Missing`

The resolved key value is carried only inside the resolver result for API startup/token setup. It is not logged, returned, written to receipts, or exposed as an API surface.

## Runtime Behavior

`Program.cs` calls `JwtStartupConfigurationValidator.Validate(...)` before `builder.Build()`.

Startup validation failure message:

`JWT signing key startup validation failed. Set Jwt__Key or IRONDEV_JWT_KEY outside committed appsettings.`

Startup success log template:

`JWT signing key startup validation passed using source {JwtSigningKeySource}.`

The success log records the source kind only. It does not log the key value, key prefix, key suffix, key hash, bearer token, generated token, or environment variable value.

`JwtTokenService` and API startup continue to use the same `JwtSigningKeyResolver` rules.

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
- no key rotation
- no production secret provisioning automation

## Validation

- Focused C07 tests: 14/14 passed.
- C06 JWT configuration tests: 13/13 passed.
- API auth/token tests (`AuthControllerTests|TenantControllerTests`): 13/13 passed.
- API boundary lane: 38/38 passed.
- CLI boundary lane: 41/41 passed.
- Governance-boundary CI script:
  - B-series profile boundary tests: 133/133 passed.
  - BQ-BU compatibility boundary tests: 80/80 passed.
  - API boundary tests: 38/38 passed.
  - CLI boundary tests: 41/41 passed.
- Frontend contract CI script: passed.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed with normal LF/CRLF warnings.
- `git diff --cached --check`: passed with normal LF/CRLF warnings.

## Review Traps

Reject this PR if:

- the JWT key value appears in logs
- the JWT key value appears in exceptions
- the JWT key value appears in receipts
- the JWT key value appears in API responses
- the old placeholder is accepted
- missing key falls back to a default
- runtime generates a random key
- short keys are accepted
- issuer/audience/lifetime validation is weakened
- tenant claims change
- endpoints/controllers are modified
- frontend auth behavior changes
- generated clients or OpenAPI snapshots change
- SQL/governance/memory/source-apply/release/deploy paths are touched
- source metadata becomes an API surface
- source metadata is treated as authority
- C07 becomes key rotation or deployment secret management

## Killjoy

Knowing where the signing key came from is diagnostic evidence, not signing authority.
