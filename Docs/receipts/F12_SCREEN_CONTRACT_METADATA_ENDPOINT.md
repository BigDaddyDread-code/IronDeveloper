# F12 — Screen Contract Metadata Endpoint

## Purpose

F12 adds the smallest read-only backend endpoint that exposes screen contract metadata for frontend readiness work.

## Files Changed

- `IronDev.Core/Governance/ScreenContractMetadataModels.cs`
- `IronDev.Core/Governance/ScreenContractMetadataService.cs`
- `IronDev.Core/Governance/ScreenContractMetadataValidator.cs`
- `IronDev.Api/Governance/ScreenContractMetadataEndpoint.cs`
- `IronDev.IntegrationTests/BlockF12ScreenContractMetadataEndpointTests.cs`
- `Docs/receipts/F12_SCREEN_CONTRACT_METADATA_ENDPOINT.md`

## Endpoint

- `GET /api/governance/screen-contract-metadata`
- Optional filter: `?screenKey=<safe-screen-key>`

Unknown or unsafe screen keys return a bounded metadata response with no entries.

## Boundary

Screen contract metadata is not UI authority.

A screen contract is not a screen permission.

F12 may describe screen keys, frontend route patterns, owning subsystems, screen kinds, visibility hints, sensitivity hints, endpoint keys, required evidence references, and boundary statements.

F12 does not grant screen access, route access, permissions, role assignment, visibility, approval, policy satisfaction, validation freshness, source safety, diagnostic execution, retry, rollback, recovery, source mutation, action invocation, workflow continuation, merge, release, deployment, redaction bypass, raw payload display, secret display, credential display, or private reasoning display.

F12 does not implement:

- CLI
- UI
- OpenAPI contract lock work
- persistence or SQL projection
- providers
- authorization handlers
- permission resolvers
- identity or role assignment
- frontend permission decisions
- source apply, commit, push, PR, merge, release, deploy, rollback, retry, recovery, or workflow continuation

Existing API authentication is preserved. F12 does not add anonymous access.

F12 does not implement authorization handlers, permission resolution, access control, identity, persistence, providers, or runtime mutation surfaces.

## Known Stack Gap

F09 boundary tests remain intentionally deferred.

## Validation

Local validation on this branch:

- F12 focused: 43/43
- F11 + F12: 127/127
- F10-F12: 225/225
- F10a + F12: 123/123
- F09 + F12: not run; F09 boundary tests remain intentionally deferred
- F09a + F12: 136/136
- F08-F12: 477/477
- F07-F12: 550/550
- F06-F12: 594/594
- F05-F12: 631/631
- F04-F12: 684/684
- F01-F12: 1083/1083
- F02 matrix: 148/148
- F03 hard-stop regressions: 145/145
- Exact E01-E18 corridor: 1630/1630
- C11 secret scan: 9/9
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed
