# C09 - Explicit CORS Policy

## Summary

C09 makes browser-origin exposure deliberate and testable.

The API now registers one named CORS policy, `IronDevCors`, with allowed origins supplied only from `Cors:AllowedOrigins` configuration. Production-like configuration defaults to no browser origins. Local development/test may configure explicit local origins.

## Boundary

CORS is browser-origin exposure control. It is not authentication, authorization, tenant authority, approval, policy satisfaction, execution permission, release readiness, deployment readiness, or workflow continuation.

An allowed origin may call the API from a browser, but the backend still owns identity, authorization, tenant access, and mutation authority.

C09 does not redesign authentication, weaken JWT validation, alter token claims, change tenant authorization, change endpoint behavior, change frontend runtime behavior, modify generated clients/OpenAPI, touch SQL, alter governance authority, mutate source, create commits, push, create PRs, merge, release, deploy, promote memory, or continue workflow.

## Configuration Scope

Configuration key:

- `Cors:AllowedOrigins`

Environment override examples:

- `Cors__AllowedOrigins__0`
- `Cors__AllowedOrigins__1`

Committed production-like config uses:

```json
"Cors": {
  "AllowedOrigins": []
}
```

Local development/test config may include:

- `http://localhost:1420`
- `http://127.0.0.1:1420`

Configured origins are validated at startup. Missing or empty origin configuration allows no browser origins.

Rejected origin entries:

- `*`
- blank or whitespace entries
- schemes other than `http` or `https`
- wildcard origins such as `https://*.example.com`
- duplicate origins
- origins with trailing slashes
- origins with paths, queries, fragments, or user info
- localhost origins in production-like environments

## Runtime Behavior

The API registers `IronDevCors` with:

- exact configured origins only
- headers: `Authorization`, `Content-Type`
- methods: `GET`, `POST`, `PUT`, `DELETE`
- no credentials

The middleware order is:

```text
UseHttpsRedirection
UseCors("IronDevCors")
UseAuthentication
UseAuthorization
```

The policy does not use:

- `AllowAnyOrigin`
- `AllowAnyHeader`
- `AllowAnyMethod`
- `AllowCredentials`
- `SetIsOriginAllowed(_ => true)`
- origin trust inferred from request headers

## Browser-Origin Scope

Allowed origins receive the expected CORS headers.

Disallowed origins do not receive `Access-Control-Allow-Origin`.

Allowed preflight requests succeed with the configured methods and headers.

Disallowed preflight requests do not receive allow-origin headers.

CORS does not make protected endpoints anonymous. `/api/environment` remains protected by C08. `/health` remains anonymous.

## Forbidden Mutation Paths

- no auth redesign
- no JWT signing-key behavior change
- no token claim change
- no tenant authorization change
- no frontend runtime change
- no generated client change
- no OpenAPI snapshot change
- no SQL migration
- no SQL store/procedure change
- no governance authority change
- no source apply
- no commit
- no push
- no PR creation/update
- no merge
- no release
- no deployment
- no memory write or promotion
- no workflow continuation
- no deployment origin provisioning
- no cookie auth or CSRF design
- no rate limiting or security-header expansion

## Validation

- Focused C09 static tests: 8/8 passed.
- API CORS behavior/config tests: 18/18 passed.
- API environment/auth endpoint tests (`ApiHarnessTests|AuthControllerTests|TenantControllerTests`): 18/18 passed.
- API boundary lane: 38/38 passed.
- C06/C07/C08 security boundary tests: 32/32 passed.
- CLI boundary lane: 41/41 passed.
- Governance-boundary CI script:
  - B-series profile boundary tests: 133/133 passed.
  - BQ-BU compatibility boundary tests: 80/80 passed.
  - API boundary tests: 38/38 passed.
  - CLI boundary tests: 41/41 passed.
- Frontend contract CI script: passed.
- Build: passed with 0 errors / 2 warnings.
- `git diff --check`: passed with normal LF/CRLF warnings.
- `git diff --cached --check`: passed with normal LF/CRLF warnings.

## Review Traps

Reject this PR if:

- `AllowAnyOrigin` is used
- `AllowAnyOrigin` is combined with credentials
- `SetIsOriginAllowed(_ => true)` is used
- wildcard origins are accepted
- blank origins are accepted
- origins are inferred from the request
- CORS is treated as authentication
- CORS is treated as tenant authority
- auth/JWT behavior changes
- `/api/environment` becomes anonymous again
- `/health` is accidentally broken
- frontend runtime behavior changes
- generated clients/OpenAPI snapshots change without contract reason
- SQL/governance/memory/source-apply/release/deploy paths are touched
- production config commits a real sensitive deployment origin without intent
- C09 becomes deployment secret/config provisioning

## Killjoy

AllowAnyOrigin with credentials is not convenience; it is an ambient browser trust leak.
