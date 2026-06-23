# C05 - GitHub Actions Frontend Contract CI

## Summary

Frontend contract CI proves the Tauri/API contract still checks.

Frontend contract CI reports evidence only.

This PR adds a separate frontend contract CI workflow and script. The lane runs the existing Tauri frontend TypeScript type-check and a check-only OpenAPI generated-client drift check against committed artifacts.

## Boundary

Frontend contract CI is not frontend authority.
Frontend contract CI is not API authority.
Frontend contract CI is not generated-client approval.
Frontend contract CI is not merge readiness.
Frontend contract CI is not release readiness.
Frontend contract CI is not deployment readiness.
Frontend contract CI is not policy satisfaction.
Frontend contract CI is not execution permission.
Frontend contract CI is not package publication.
Frontend contract CI is not workflow continuation.

Frontend contract CI reports evidence only. It is not frontend authority, API authority, generated-client approval, merge readiness, release readiness, deployment readiness, policy satisfaction, execution permission, package publication, or workflow continuation.

Frontend reads backend truth. Frontend does not own authority.
OpenAPI describes API shape. It does not approve API shape.

No frontend behavior, backend API behavior, OpenAPI source of truth, generated client, SQL, executor, approval, policy, source-apply, commit, push, PR, release, deploy, package publication, memory, or workflow-continuation path was added.

## Workflow Scope

- pull_request to main
- `workflow_dispatch`
- `contents: read`
- no write permissions
- separate `frontend-contract-ci` workflow
- Windows-hosted runner
- Node.js setup only
- no SQL Server service
- no Docker service
- no production secrets
- no live hosted API process

## Frontend/Tauri Scope

The frontend lane runs against the existing `IronDev.TauriShell` project.
The lane uses the existing `package-lock.json`.
The lane installs dependencies with `npm ci`.
The lane runs TypeScript in no-emit mode.
The lane does not run `tauri build`.
The lane does not build installers.
The lane does not sign, notarize, bundle, publish, release, or deploy.
The lane does not change frontend runtime behavior.

## OpenAPI Drift Scope

The drift check uses the existing committed OpenAPI snapshot:

- `IronDev.TauriShell/openapi/irondev-api.openapi.json`

The drift check compares generated output in a temporary directory against the existing committed generated API type file:

- `IronDev.TauriShell/src/api/generated/ironDevApiTypes.ts`

OpenAPI drift success means the committed contract and generated/client surface match. It does not approve API shape or client changes.

The drift check is check-only. It does not fetch a live API, silently update snapshots, write generated clients into source, commit generated files, upload generated files, or publish packages.

## CI Lane

The workflow runs:

- `./Scripts/ci/run-frontend-contract-ci.ps1`

The script runs:

- `npm ci`
- `npx tsc --noEmit`
- `npx openapi-typescript openapi/irondev-api.openapi.json -o <temp-file>`
- exact comparison between committed generated client types and temporary generated output

If drift is detected, the script fails with:

`OpenAPI/client drift detected. This is evidence only. Update the API contract/client in a separate reviewed PR.`

## Forbidden Mutation Paths

- no git push
- no git commit
- no git tag
- no PR mutation
- no issue mutation
- no label mutation
- no release
- no deployment
- no package publish
- no generated client publish
- no generated client source update
- no OpenAPI snapshot update
- no artifact upload
- no installer build
- no signing
- no notarization
- no memory writes
- no receipt writes from CI
- no workflow continuation
- no frontend behavior change
- no backend API behavior change
- no API schema source-of-truth change
- no generated client commit
- no SQL integration expansion

## Validation

- Frontend type-check: passed.
- OpenAPI generated-client drift check: passed.
- Frontend contract CI script: passed.
- Focused C05 static boundary tests: 8/8 passed.
- C01/C02/C03/C04/C05 static CI proof lane: 40/40 passed.
- Build: 0 errors / 2 warnings.
- Initial OpenAPI drift comparison exposed line-ending-only drift; the script now normalizes CRLF/LF before content comparison so the gate remains check-only and content-strict without committing generated output.
- `git diff --check`: passed.
- `git diff --cached --check`: passed.

## Review Traps

Reject this PR if:

- Type-check success is treated as frontend authority.
- OpenAPI drift success is treated as API authority.
- Generated output is treated as approved client change.
- A clean CI run is treated as approval.
- A clean CI run is treated as merge readiness.
- A clean CI run is treated as release readiness.
- A clean CI run is treated as deployment readiness.
- A clean CI run is treated as policy satisfaction.
- A clean CI run is treated as execution permission.
- A clean CI run is treated as workflow continuation.
- The workflow gains write permissions.
- The workflow uploads artifacts.
- The workflow publishes packages.
- The workflow runs `tauri build`.
- The workflow signs, notarizes, bundles installers, releases, or deploys.
- The workflow starts SQL Server.
- The workflow starts Docker.
- The workflow calls external AI or provider systems.
- The workflow requires production secrets.
- The workflow starts a live hosted API process.
- The workflow pushes commits or tags.
- The workflow comments on PRs.
- The workflow labels PRs.
- C05 changes frontend runtime behavior, backend API behavior, generated clients, OpenAPI snapshots, SQL CI, governance/API/CLI production code, executors, UI behavior, memory, source-apply, commit, push, PR, release, or deployment code.

## Killjoy

A clean type-check and OpenAPI drift check is evidence, not permission to change API shape, generate clients, publish artifacts, release, deploy, or bypass backend authority.
