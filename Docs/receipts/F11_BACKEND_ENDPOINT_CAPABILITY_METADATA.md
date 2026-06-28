# F11 - Backend Endpoint Capability Metadata

## Review Line

Endpoint capability metadata is not endpoint authority.

## Purpose

Block F11 defines a Core-only contract for describing backend endpoint capability metadata.

Endpoint metadata may describe route shape, HTTP method, capability category, visibility surface, material category, sensitivity, required evidence references, and which separate governance decisions are required before use.

It does not answer whether an actor may call an endpoint.

Knowing what a door is for is not permission to open it.

## Files Changed

- `IronDev.Core/Governance/BackendEndpointCapabilityMetadataModels.cs`
- `IronDev.Core/Governance/BackendEndpointCapabilityMetadataService.cs`
- `IronDev.Core/Governance/BackendEndpointCapabilityMetadataValidator.cs`
- `IronDev.IntegrationTests/BlockF11BackendEndpointCapabilityMetadataTests.cs`
- `Docs/receipts/F11_BACKEND_ENDPOINT_CAPABILITY_METADATA.md`

## Model Summary

F11 adds:

- `BackendEndpointHttpMethodKind`
- `BackendEndpointCapabilityKind`
- `BackendEndpointSensitivityKind`
- `BackendEndpointCapabilityIntent`
- `BackendEndpointCapabilityMetadataEntry`
- `BackendEndpointCapabilityMetadataCatalog`
- `BackendEndpointCapabilityMetadataRequest`
- `BackendEndpointCapabilityClassification`
- `BackendEndpointCapabilityDecision`
- `BackendEndpointCapabilityValidationResult`

The decision model carries explicit false flags for endpoint authority, route access, invocation, route-guard creation, role assignment, visibility authority, access, external access, approval acceptance, policy satisfaction, validation refresh, source-safety proof, diagnostic execution, retry, rollback, recovery, mutation, workflow continuation, merge, release, deployment, redaction bypass, secret disclosure, credential disclosure, raw payload disclosure, and private reasoning disclosure.

## Service Behavior Summary

The classifier:

- validates request shape and safe text
- validates endpoint capability metadata catalog shape
- requires endpoint metadata evidence reference
- requires role catalog evidence reference
- requires visibility matrix evidence reference
- validates the F01 role catalog
- validates the F02 visibility matrix
- finds the requested endpoint metadata entry
- blocks unknown endpoint keys
- blocks unknown intent
- blocks every non-metadata intent
- blocks route access, endpoint invocation, and route-guard intent
- blocks policy, approval, validation, source-safety, diagnostic, retry, rollback, recovery, mutation, workflow, merge, release, deployment, redaction-bypass, and disclosure intent
- blocks raw payload, credential, secret, private-reasoning, mutation-material, and release/deploy-material sensitivity
- requires policy evidence for sensitive metadata
- requires redaction evidence for redacted-summary metadata
- requires tenant-boundary evidence for tenant-scoped and project-scoped metadata
- returns only bounded metadata classifications

High-risk endpoint kinds such as mutation, execution, admin, raw-export, and external-share endpoint metadata can only become metadata-only candidate descriptions. They never authorize invocation, mutation, sharing, export, or access.

## Boundary Rules

- Endpoint capability metadata is not endpoint authority.
- Endpoint metadata is not route access.
- Endpoint metadata is not endpoint invocation.
- HTTP method metadata is not action authority.
- Route template metadata is not a callable route grant.
- Capability kind is not permission.
- Visibility surface is not visibility authority.
- Visibility material kind is not access.
- Required evidence metadata is not satisfied evidence.
- Policy evidence reference is not policy satisfaction.
- Approval metadata is not approval acceptance.
- Validation endpoint metadata is not validation freshness.
- Diagnostic endpoint metadata is not diagnostic execution.
- Mutation endpoint metadata is not mutation authority.
- Workflow endpoint metadata is not workflow continuation.
- Release endpoint metadata is not release authority.
- Deploy endpoint metadata is not deployment authority.
- Redaction metadata is not redaction bypass.
- External endpoint metadata is not external access.
- A visibility classification is not a visibility decision.
- A metadata classification is not an authorization decision.

## Unsafe Markers

The validator rejects endpoint metadata authority-shaped text including:

- `EndpointAccessGranted = true`
- `RouteAccessGranted = true`
- `CanInvokeEndpoint = true`
- `CanCallEndpoint = true`
- `RouteGuardCreated = true`
- `CanCreateRouteGuard = true`
- `RoleAccessGranted = true`
- `ExternalAccessGranted = true`
- `CanSatisfyPolicy = true`
- `PolicySatisfied = true`
- `ApprovalAccepted = true`
- `ValidationRefreshed = true`
- `SourceSafetyProven = true`
- `CanRunDiagnostic = true`
- `CanRetry = true`
- `CanRollback = true`
- `CanRecover = true`
- `CanMutate = true`
- `CanApplyPatch = true`
- `CanCommit = true`
- `CanPush = true`
- `CanCreatePullRequest = true`
- `CanReadyForReview = true`
- `CanMerge = true`
- `CanRelease = true`
- `CanDeploy = true`
- `CanContinueWorkflow = true`
- `CanBypassRedaction = true`
- `CanViewSecrets = true`
- `CanViewCredentials = true`
- `CanViewRawPayload = true`
- `CanViewPrivateReasoning = true`

It also rejects semantic smells such as endpoint metadata grants access, endpoint capability grants permission, route template can be called, GET endpoint is safe to invoke, POST endpoint is allowed, capability metadata satisfies policy, capability metadata approves work, capability metadata refreshes validation, capability metadata proves source safety, capability metadata can mutate, capability metadata continues workflow, capability metadata releases deployment, capability metadata bypasses redaction, and capability metadata reveals secrets.

## Test Summary

Focused tests prove:

- endpoint metadata catalog validates safe entries
- endpoint metadata is metadata-only and descriptive
- endpoint key does not grant endpoint authority
- route template does not grant route access
- HTTP method metadata does not grant invocation or mutation authority
- capability kind does not grant permission
- visibility surface and material kind do not grant visibility/access
- required evidence refs do not satisfy evidence
- read-only metadata, read-only summary, redacted summary, status, receipt, proposal, approval, policy, audit, validation, diagnostic, and release-readiness endpoint metadata produce only candidate classifications
- redacted summary metadata requires redaction evidence
- tenant/project-scoped endpoint metadata requires tenant-boundary evidence
- sensitive endpoint metadata requires policy evidence
- receipt/status/approval/policy/validation/diagnostic/mutation/workflow/release/deploy metadata do not grant downstream authority
- raw payload, credential, secret, and private-reasoning capabilities are blocked
- invocation, route access, route guard, external access, approval, policy, validation, source-safety, diagnostic, retry, rollback, recovery, mutation, workflow, merge, release, deployment, redaction-bypass, secret, raw, and private disclosure intents are blocked
- unknown endpoint and unknown intent fail closed
- missing endpoint metadata, catalog, matrix, redaction, tenant-boundary, and policy evidence fail closed where required
- hostile endpoint capability text is rejected and not echoed
- every decision has all authority and disclosure flags false
- static scan proves no API, CLI, UI, OpenAPI, persistence, provider, endpoint filter, route guard, authorization handler, permission resolver, mutation, workflow, release, deploy, share/export, or redaction-bypass surface was added

## Reported Validation

- F11 focused tests: 84/84 passed
- F10 + F11 compatibility: 182/182 passed
- F10a + F11 compatibility: 164/164 passed
- F09 + F11 compatibility: not run; F09 is intentionally deferred and F11 is based on F10/F10a/F09a
- F09a + F11 compatibility: 177/177 passed
- F08-F11 compatibility: 434/434 passed
- F07-F11 compatibility: 507/507 passed
- F06-F11 compatibility: 551/551 passed
- F05-F11 compatibility: 588/588 passed
- F04-F11 compatibility: 641/641 passed
- F01-F11 compatibility: 1040/1040 passed
- F02 matrix compatibility: 148/148 passed
- F03 hard-stop regressions: 145/145 passed
- E01-E18 corridor: 1630/1630 passed
- C11 secret scan: 9/9 passed
- Build: 0 errors / 4 warnings
- `git diff --check`: passed
- `git diff --cached --check`: passed

## Known Limitations

F11 does not implement backend endpoint creation, runtime endpoint registration, API exposure, CLI exposure, UI exposure, OpenAPI generation, route guards, authorization handlers, permission resolution, access control, identity, role assignment, external access, share links, raw exports, approval acceptance, policy satisfaction, validation refresh, source safety proof, diagnostic execution, retry execution, rollback execution, recovery execution, workflow continuation, source mutation, commit, push, PR mutation, ready-for-review, merge, release, deployment, redaction bypass, secret disclosure, credential disclosure, raw payload disclosure, private reasoning disclosure, persistence, SQL storage, read model projection, or GitHub sync.

F11 does not implement runtime endpoint registration.

F11 does not implement route guards.

F11 does not implement authorization handlers.

F11 does not implement permission resolution.

F11 does not implement access control.

F11 does not implement endpoint invocation, route authorization, API controller wiring, minimal API mapping, OpenAPI metadata generation, UI route guards, permission resolvers, access-control engines, external sharing/export tooling, or endpoint execution behavior.

F09 boundary tests remain intentionally deferred in this stack.

## Stack

- Base branch: `governance/external-viewer-redaction-rules`
- Head branch: `governance/backend-endpoint-capability-metadata`
- Stack: F11 -> F10 -> F10a -> F09a -> F08 -> F07 -> F06 -> F05 -> F04 -> F03 -> F02 -> F01 -> Block E tip -> main roll-up later
- F09 boundary tests are not included and remain deferred.

## Killjoy

Knowing what a door is for is not permission to open it.
