# C14 — Sensitive API Rate-Limit / Auth Hardening

## Purpose

C14 adds explicit rate limiting and auth-boundary proof for sensitive API surfaces.

Review line:

> Rate limiting slows abuse. Authorization controls access. Neither grants authority.

## Scope

Changed surfaces:

- `IronDev.Api/Program.cs`
- `IronDev.Api/Controllers/AuthController.cs`
- `IronDev.Api/Controllers/ToolRequestsV1Controller.cs`
- `IronDev.Api/Controllers/ToolGatesV1Controller.cs`
- `IronDev.Api/Controllers/AgentRunsV1Controller.cs`
- `IronDev.Api/Controllers/PatchArtifactsV1Controller.cs`
- `IronDev.Api/Controllers/ApplyPreviewController.cs`
- `IronDev.Api/Controllers/ManualMemoryImprovementsV1Controller.cs`
- `IronDev.Api/Controllers/ReleaseReadinessDecisionRecordsController.cs`
- `IronDev.IntegrationTests.Api/SensitiveApiRateLimitTests.cs`
- `IronDev.IntegrationTests.Api/SensitiveApiAuthBoundaryTests.cs`
- `IronDev.IntegrationTests/BlockC14SensitiveApiRateLimitAuthBoundaryTests.cs`
- `Scripts/ci/run-governance-boundary-ci.ps1`
- `Docs/receipts/C14_SENSITIVE_API_RATE_LIMIT_AUTH_HARDENING.md`

## Boundary

Rate limiting slows request abuse. It does not authenticate users, authorize tenants, grant authority, approve execution, satisfy policy, create release readiness, create deployment readiness, or continue workflow.

A rate-limited sensitive endpoint is still not safe unless backend authorization and authority gates approve the requested action.

C14 adds:

- `AuthLoginPolicy` for `/api/auth/login`.
- `SensitiveApiPolicy` for selected sensitive API surfaces.
- `UseRateLimiter` after authentication so sensitive partitions can use user and tenant claims when present.
- IP fallback partitioning for anonymous login and unauthenticated sensitive probes.
- Static proof that rate-limit keys do not use raw bearer tokens, passwords, API keys, or request bodies.

C14 preserves:

- login is anonymous but not unlimited.
- `/api/auth/me` and `/api/auth/logout` require authorization.
- `/health` remains anonymous and is not sensitive-rate-limited.
- `/api/environment` remains authorized and is sensitive-rate-limited.
- selected sensitive controllers remain `[Authorize]` and are sensitive-rate-limited.

## Validation

- Focused C14 API behavior: 16/16 passed.
- Focused C14 static boundary: 11/11 passed.
- C06-C14 security boundary lane: 97/97 passed.
- Governance boundary CI script:
  - B-series profile boundary tests: 133/133 passed.
  - BQ-BU compatibility boundary tests: 80/80 passed.
  - Security boundary tests: 40/40 passed.
  - API boundary tests: 38/38 passed.
  - CLI boundary tests: 41/41 passed.
- Build: 0 errors / 4 warnings.
- `git diff --check`: passed with normal LF/CRLF warnings.
- `git diff --cached --check`: passed.

## Review Traps

Reject C14 if:

- login becomes unauthenticated and unlimited.
- sensitive endpoints lose `[Authorize]`.
- `/api/environment` becomes anonymous.
- `/health` starts requiring auth or sensitive rate limiting.
- CORS preflight is broken.
- rate-limit keys use raw bearer tokens, passwords, API keys, or request bodies.
- rate limiting is described as approval, policy satisfaction, execution authority, readiness, deployment safety, or workflow continuation.
- rate limiting hides or replaces backend authorization, tenant isolation, or governed action gates.

## Killjoy

A sensitive API without explicit auth and throttling is not "internal"; it is merely waiting for a bad client loop.
