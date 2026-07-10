# API-CONTRACT-1 Baseline Reset

## Purpose

This change resets the checked-in Swagger and generated TypeScript contracts to the running API after API-CONTRACT-0 proved generation is deterministic.

There are no UX changes, business-logic changes, authority changes, database changes, or opportunistic refactors in this baseline reset.

The refreshed generated response types exposed two existing Chat aliases that intersected generated nullability with UI-normalized values. Those aliases now use `Omit<generated, overridden keys> & UI overrides`, preserving the generated contract as the base without changing runtime behavior or introducing handwritten request shapes.

## Contract Change Summary

| Measure | Previous baseline | Refreshed baseline |
|---|---:|---:|
| OpenAPI paths | 66 | 225 |
| Operations | 85 | 255 |
| Component schemas | 68 | 403 |
| OpenAPI lines | 6,819 | 32,754 |
| Generated TypeScript lines | 4,038 | 15,271 |

Major added contract groups:

- governed agent, approval, apply, memory, tool, workflow, and release-readiness APIs under `/api/v1/*`
- project runs, batch work, provisioning, service status, code index, and authority read models
- project documents, exact versions, upload, processing, and document-to-ticket routes
- project member, channel membership, shared channel, and human message routes
- frontend-readiness operation, evidence, receipt, and patch-package routes
- tenant user administration and invitation routes
- ticket build runs, disposable runs, review packages, evidence, continuation, revision, and apply routes

The old unscoped `/api/chat/sessions/{sessionId}` path is removed. Its project-scoped replacement is `/api/projects/{projectId}/chat/sessions/{sessionId}`.

## Supported Generation Path

Run from the repository root:

```powershell
.\tools\contracts\update-openapi-contract.ps1
```

The script:

1. builds `IronDev.Api` into an isolated temporary directory;
2. starts it on port `5017` in the guarded `Test` environment;
3. uses a test-shaped, intentionally unreachable database connection that Swagger generation never opens;
4. runs the pinned `npm run api:generate` command;
5. stops the API and deletes temporary build output.

It refuses to stop or reuse another process if port `5017` is occupied.

## Enforcement

Frontend contract CI now proves the full chain:

```text
running API -> checked-in OpenAPI -> checked-in generated TypeScript
```

The lane regenerates from the isolated API and fails when either generated file changes. It then runs `git diff --exit-code` against both artifacts.

Clean-main exit check:

```powershell
.\tools\contracts\update-openapi-contract.ps1 -Check
git diff --exit-code -- `
    .\IronDev.TauriShell\openapi\irondev-api.openapi.json `
    .\IronDev.TauriShell\src\api\generated\ironDevApiTypes.ts
```

After this merge, every API-changing PR must regenerate and review both artifacts in the same PR.
