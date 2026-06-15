# PR149 - Backend Operational Health Checks

## Purpose

PR149 adds Backend Operational Health Checks.

Backend Operational Health Checks are read-only.

The report returns safe summaries, dependency checks, warnings, recommendations, and boundary warnings only.

## Boundary

Health check is not release readiness.

Healthy status is not approval.

Dependency status is not authority.

Recommendation is not execution.

Report is not backend repair.

Report is not backend restart.

Report is not migration execution.

Report is not workflow execution.

This PR does not restart backend services, repair backend services, run migrations, rebuild read models, reindex data, flush caches, purge queues, execute workflows, transition workflow, invoke tools, dispatch agents, call models, build prompts, approve release, satisfy policy, create tickets, promote memory, activate retrieval, apply source, apply patches, create governance events, create approval decisions, create policy decisions, create tool requests, create dogfood receipts, execute commands, expose connection strings, expose API keys, expose secrets, expose raw payloads, expose raw prompts/completions/tool outputs, expose source content, expose patch payloads, or expose hidden/private reasoning.

## API routes

- `GET /api/v1/operations/health`
- `GET /api/v1/operations/health/backend`
- `GET /api/v1/operations/health/dependencies`

These routes are GET-only and require authentication.

## Health status

- `Healthy`: read-only dependencies are available.
- `Degraded`: one or more non-critical read-only dependencies are degraded, unavailable, or not configured.
- `Unavailable`: a critical dependency is unavailable.
- `InvalidRequest`: query parameters are invalid or unsafe.

Healthy is operational status only. It is not release approval, deployment permission, workflow execution permission, policy satisfaction, or backend authority.

## Review line

PR149 checks the backend pulse. It does not administer treatment.
