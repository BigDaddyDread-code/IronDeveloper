# Duplicate Truth Ownership

One rule has one decision owner. Callers may project or transport that decision, but must not recalculate it. Similar names do not imply the same rule: project provisioning readiness, ticket build readiness, release readiness, and frontend read-state freshness are distinct contracts.

| Rule | Authoritative owner | Allowed consumers/projections | Removed or forbidden duplicate |
|---|---|---|---|
| Project provisioning readiness | `ProvisioningReadinessEvaluator` over stored project configuration | `ProjectProvisioningReadinessService`, Board and setup read models | UI inference from repository shape or local defaults |
| Ticket build readiness | `BuilderReadinessService` | ticket/work-item read services and API clients | controllers or React components recalculating eligibility |
| Chat route/mode classification | `LlmChatModeClassifier` | `ProjectChatResponseService`; route judge supplies hints only | deleted `ChatModeClassifierService`; prose/action-name inference |
| Governed action authority/gate state | `ConscienceDecisionService`, using `GovernedActionKernelRequirements` | API/infrastructure adapters may submit typed evidence and render the decision | audit, critic, frontend, green CI, or memory granting permission |
| Tenant token scope | `TenantTokenScopeMiddleware` | authenticated API routes consume the established tenant claim | accepting route, body, or client-selected tenant identity as token scope |
| Route/body write binding | `RouteBodyScopeBindingFilter` | write controllers consume the route-bound project and tenant IDs | accepting conflicting body scope |
| Project artifact access | `ProjectArtifactAccessService` | artifact readers consume its membership decision | treating a project ID or client-selected membership as access proof |
| Memory authority ranking | `MemoryAuthorityNormalizer` | memory map/context compilers project the normalized level | per-consumer status-to-authority switches |
| Audit evidence-link safety | `AuditEvidenceLinkSafety` | audit export projector and UI may apply stricter display checks | private exporter-only link rule; cross-project or external links |
| Refusal formatting | `GovernedRefusal.Create` producing `GovernedRefusalEnvelope` | middleware/controllers transport the canonical envelope | local refusal record shapes and ad hoc normalization |

## Changes in CLN-35

The audit export's private link validator was extracted into `AuditEvidenceLinkSafety`, giving backend audit projection one reusable fail-closed owner. Focused tests lock same-project, compatibility-evidence, cross-project, scheme-relative, external, path-normalization traversal, encoded traversal, signed-ID, and invalid-project behavior. A conformance test names all ten owners and prevents the already-deleted chat classifier, private audit-link rule, or second refusal envelope from returning.

This ownership map is not a service locator and does not create runtime authority. It records where each rule is already decided so future cleanup removes projections that begin deciding for themselves.
