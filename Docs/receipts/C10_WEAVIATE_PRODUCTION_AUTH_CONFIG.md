# C10 - Production Weaviate Auth Config

## Summary

C10 makes Weaviate authentication explicit and production-safe.

The backend already carried `WeaviateOptions.ApiKey`. C10 resolves that key from safe configuration, applies it to the Weaviate client builder, and fails closed when production-like enabled Weaviate would otherwise run anonymously.

## Boundary

Weaviate authentication protects the retrieval index. It does not make Weaviate a source of truth, grant memory authority, approve memory promotion, satisfy policy, or authorize execution.

Weaviate remains an index/retrieval system. SQL and backend governance remain the source of truth.

C10 does not redesign semantic memory, change ranking behavior, promote memory, alter SQL ownership, change frontend behavior, modify API contracts, create source-apply authority, create commit/push/PR authority, merge, release, deploy, or continue workflow.

## Configuration Scope

Configuration keys:

```json
"Weaviate": {
  "Enabled": false,
  "Endpoint": "",
  "GrpcPort": 50051,
  "ApiKey": "",
  "CollectionPrefix": "IronDevContextChunks"
}
```

API-key source precedence:

1. `Weaviate:ApiKey`, including normal environment-provider overrides such as `Weaviate__ApiKey`
2. `IRONDEV_WEAVIATE_API_KEY`
3. missing

Checked-in appsettings files keep `Weaviate:ApiKey` empty.

## Production Behavior

Production-like enabled Weaviate must fail closed unless:

- endpoint is configured
- endpoint is not localhost
- endpoint uses HTTPS
- API key is configured from safe configuration
- API key is not a placeholder or short local value

Production-like enabled Weaviate must not rely on anonymous access.

## Local Development Behavior

Anonymous Weaviate access is local/development posture only.

Development, Test, and LocalTest may use anonymous Weaviate only when the endpoint is local:

- `localhost`
- `127.0.0.1`
- `::1`

The existing local Weaviate posture remains opt-in and disabled by default.

## Client Auth Wiring

`WeaviateSemanticMemoryService` applies the configured key to the official Weaviate client builder using API-key credentials when a key is present.

The service no longer stores an unused key-only option path.

## Forbidden Secret Leakage

The Weaviate API key must not be:

- committed to appsettings
- logged
- returned by `/api/environment`
- written to receipts
- included in validation metadata
- included in startup/config exception messages

Validation metadata records only source classification and issue names.

## Forbidden Mutation Paths

- no SQL migration
- no SQL store/procedure change
- no memory promotion
- no governed memory approval/policy change
- no semantic ranking change
- no embedding behavior change
- no frontend/Tauri runtime change
- no OpenAPI/generated-client change
- no JWT behavior change
- no CORS behavior change
- no environment endpoint behavior change
- no source apply
- no commit
- no push
- no PR creation/update
- no merge
- no release
- no deployment
- no workflow continuation

## Validation

- Focused C10 tests: 17/17 passed.
- C06/C07/C08/C09 security boundary tests plus C10 and semantic memory boundary tests: 66/66 passed.
- Governance-boundary CI script:
  - B-series profile boundary tests: 133/133 passed.
  - BQ-BU compatibility boundary tests: 80/80 passed.
  - API boundary tests: 38/38 passed.
  - CLI boundary tests: 41/41 passed.
- Build: passed with 0 errors / 4 warnings.
- `git diff --check`: passed with normal LF/CRLF warnings.
- `git diff --cached --check`: passed.

## Review Traps

Reject C10 if:

- Weaviate API key is committed to appsettings
- Weaviate API key appears in logs
- Weaviate API key appears in exceptions
- Weaviate API key appears in receipts
- Weaviate API key appears in `/api/environment`
- production-like enabled Weaviate can run anonymously
- production-like enabled Weaviate can use HTTP
- production-like enabled Weaviate can use localhost
- service stores an API key option but does not pass it to the Weaviate client
- local anonymous Weaviate is presented as production-safe
- semantic ranking behavior changes
- memory promotion authority changes
- SQL source-of-truth behavior changes
- frontend/OpenAPI/generated clients change
- JWT/CORS/environment endpoint behavior changes
- governance/source-apply/release/deploy paths are touched

## Killjoy

Anonymous Weaviate is acceptable for local experiments. In production it is an unguarded memory-index door.
