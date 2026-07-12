# Route And Body Scope Binding Audit

**Status:** Canonical API scope audit

**Last reviewed:** 13 July 2026

**Programme slice:** CLN-11

## Rule

For every `POST`, `PUT`, `PATCH`, or `DELETE` action whose route declares `{projectId}` or `{tenantId}`:

```text
the route is authoritative
body ProjectId/TenantId may be omitted or defaulted
any non-empty body scope must exactly match the route
a mismatch is refused before controller code runs
```

Stable reason codes are:

- `route_body_project_scope_mismatch`
- `route_body_tenant_scope_mismatch`

The refusal is HTTP 400 and includes `Allowed=false`, the reason code, message, blocked reason, correlation ID, route value, and body value. A scope mismatch is invalid request input, matching the established API contract. CLN-13 owns the wider governed refusal envelope; these reason codes remain stable.

## Enforcement

`RouteBodyScopeBindingFilter` is registered once as a global MVC action filter. It:

- runs only for write verbs;
- inspects typed bodies, nested records, arrays, and raw JSON;
- accepts integer, GUID, and string scope values;
- treats zero, empty GUID, null, and omitted scope as absent;
- detects cyclic object graphs without serializing request streams;
- refuses before the action delegate, so no mutation can occur first;
- leaves reads outside the mutation gate.

Controllers may still overwrite an omitted/default body scope with the route value. They may not silently overwrite a conflicting client scope.

## Repository Sweep

The CLN-11 source sweep found **111 write actions** in API controllers:

| Route class | Actions | Contract |
| --- | ---: | --- |
| Project/tenant route scoped | 81 | Globally enforced by `RouteBodyScopeBindingFilter` |
| Unscoped auth, selected-tenant, resource-key, and compatibility writes | 30 | No competing route scope exists; retain their existing token/resource ownership checks |

The 30 unscoped writes are not counted as route/body matches. They comprise:

- authentication and tenant selection;
- project creation under the selected tenant;
- tenant-owned agent profile and AI connection settings;
- resource-key compatibility routes for documents, memory documents, and tickets;
- governance compatibility endpoints whose project is currently in their request contract.

Those routes must not be used as evidence that route scope was checked. Removing or replacing compatibility routes requires usage proof and belongs to compatibility cleanup, not silent deletion in this slice.

## Required Coverage

| Area | Scoped route owners or authoritative boundary |
| --- | --- |
| Chat | `ChatController`, `ProjectChannelsController` |
| Tickets, plans, findings, runs | `TicketsController`, `DiscussionCodeLoopController` |
| Documents | Project document create, upload, save, process, resolve, and archive routes |
| Decisions | `DecisionsController` |
| Memory | Project summary, decisions, search, reindex, documents, plans, and rules routes |
| Approvals | `AcceptedApprovalsV1Controller`, policy satisfaction route |
| Provisioning | Project profile and command mutations; setup actions remain route-owned backend commands |
| Governance and audit | Governed release/continuation routes and project-owned evidence actions |
| Channels and members | Project channels, channel members, project members, notifications |
| Tenant users | Tenant user create, role, and removal routes |

## Proof

`RouteBodyScopeBindingFilterTests` prove:

- typed project mismatch refusal;
- nested JSON tenant GUID mismatch refusal;
- matching and omitted/default scopes reach the action;
- reads are not treated as mutation gates;
- cyclic model-bound objects remain safe;
- exactly one global registration exists.

## Killjoy Line

Overwriting a hostile body ID with the route ID is not validation; it is hiding evidence that the caller asked to mutate a different scope.
