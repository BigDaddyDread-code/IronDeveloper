# API-CONTRACT-0 Generator Drift Diagnosis

## Decision

OpenAPI generation is deterministic. The large diff is primarily real contract backlog in the checked-in baseline, with secondary line-ending noise. It is not evidence of random schema ordering.

Generated artifacts are intentionally not refreshed in this change. API-CONTRACT-1 owns the baseline reset and the live regeneration gate.

## Reproduction

Tested from clean `main` at `56cf91d4` against the supported LocalTest API on 2026-07-11.

Toolchain:

- .NET SDK `10.0.301`
- Node.js `24.16.0`
- npm `11.13.0`
- `openapi-typescript` `7.13.0`
- `Microsoft.AspNetCore.OpenApi` `10.0.5`
- `Swashbuckle.AspNetCore` `10.1.7`

Commands:

```powershell
.\tools\localtest\start-alpha-localtest.ps1 -Reset -FreshSession -BrowserOnly
Set-Location .\IronDev.TauriShell
npm ci
npm run api:diagnose -- --output ..\artifacts\api-contract-0\diagnostic.json
```

Two fetch-and-generation passes against the same API process produced byte-identical files:

| Artifact | First SHA-256 | Second SHA-256 | Result |
|---|---|---|---|
| OpenAPI | `5b100ac8125cf48e2f58c02708c430bf650129b74c5bb3a1b5f1907cb3a4db79` | same | Deterministic |
| TypeScript | `79bf6d85870dd6ba0f6ada08df9b0f9597e65b566d01dfd2293ca29140d8d53f` | same | Deterministic |

## Drift Classification

| Measure | Checked in | Running API | Difference |
|---|---:|---:|---:|
| OpenAPI paths | 66 | 225 | +160 / -1 |
| Operations | 85 | 255 | +170 |
| Component schemas | 68 | 403 | +335 / -0 |
| OpenAPI lines | 6,819 | 32,754 | +25,935 |
| Generated TypeScript lines | 4,038 | 15,271 | +11,233 |

The single removed path is the old unscoped `/api/chat/sessions/{sessionId}` route. Its project-scoped replacement is present under `/api/projects/{projectId}/chat/sessions/{sessionId}`.

The 160 added paths are concentrated in real product contracts:

- 80 `/api/v1/*` governance, agent, approval, apply, memory, tool, and workflow paths
- 62 project-scoped paths, including runs, documents, members, channels, provisioning, tools, and ticket execution
- 8 frontend-readiness paths
- 4 tenant administration paths
- 6 environment, governance, run, and workflow paths

Only nine existing paths changed. They add project patch operations, project-scoped Chat details, document and ticket fields, and request/response shape evolution. Only nine of the 68 pre-existing schemas changed; 335 schemas are entirely new. There are no removed schemas.

The OpenAPI document remains version `3.0.4`, title `IronDev.Api`, API version `1.0`. Fresh output uses LF. The existing Windows checkout used CRLF, so generated-file line endings are now fixed to LF through `.gitattributes`.

### Cause Matrix

| Suspected cause | Finding |
|---|---|
| Real missing endpoints | Primary cause. The live API contains 160 paths absent from the snapshot. |
| Nondeterministic ordering | Not observed. Two complete OpenAPI and TypeScript generations are byte-identical. |
| Schema naming changes | Additive, not a global rename. There are 335 new schemas, zero removed schemas, and nine changed common schemas. |
| Nullable/default changes | Mostly proportional to the added contracts. `nullable` occurrences rise from 389 to 2,086 and `default` occurrences from 9 to 102; only nine common schemas changed. |
| Swagger configuration changes | Not identified as the cause. OpenAPI version, title, API version, and pinned Swagger package versions are stable. |
| Line endings or formatting | Secondary noise. The old Windows checkout is CRLF and fresh generation is LF; `.gitattributes` now fixes generated files to LF. |

The checked-in snapshot was last touched on 2026-07-06. Product API work continued after that point without a live Swagger comparison, which explains the additive shape of the drift.

## Why CI Stayed Green

`run-frontend-contract-ci.ps1` regenerates TypeScript from the checked-in OpenAPI file and compares those two checked-in layers. It does not start the API, fetch live Swagger, or compare the snapshot with the running application.

That lane proves:

```text
checked-in OpenAPI -> checked-in generated TypeScript
```

It does not currently prove:

```text
running API -> checked-in OpenAPI -> checked-in generated TypeScript
```

API-CONTRACT-1 must add the second proof after committing the dedicated baseline refresh.

## Platform Finding

`npm ci` could not replace `node_modules/@esbuild/win32-x64/esbuild.exe` while Vite was running because Windows held the executable open. Stopping only the process listening on port `5173` allowed the same pinned install to pass immediately. This is not generator nondeterminism, but it confirms the live-process/build-output isolation item already assigned to PLATFORM-BASELINE-1.

## Pinned Inputs

This diagnosis pins the inputs that can otherwise move underneath regeneration:

- `global.json` fixes .NET SDK `10.0.301` with roll-forward disabled.
- `.node-version` and CI fix Node.js `24.16.0`.
- `packageManager` and CI fix npm `11.13.0`.
- `openapi-typescript` is exact at `7.13.0`; `package-lock.json` retains its integrity hash.
- Generated OpenAPI and TypeScript use LF on every platform.

## API-CONTRACT-1 Handoff

The baseline-reset PR should contain no UX or business logic. It should:

1. Start the API through a supported isolated contract-generation path.
2. Run `npm run api:generate` with the pinned toolchain.
3. Commit the complete OpenAPI and TypeScript refresh.
4. Add a CI step that runs `npm run api:diagnose -- --require-clean` against that supported API.
5. Fail when either the live Swagger snapshot or generated TypeScript differs.

Exit command after API-CONTRACT-1:

```powershell
npm run api:generate
git diff --exit-code -- openapi/irondev-api.openapi.json src/api/generated/ironDevApiTypes.ts
```
