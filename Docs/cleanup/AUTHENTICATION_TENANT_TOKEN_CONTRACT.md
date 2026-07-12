# Authentication And Tenant Token Contract

**Status:** Canonical cleanup contract

**Programme slice:** CLN-15

## Token Stages

Login issues a base token that identifies one user and carries no `tenant_id`. The base token may access only:

- current identity;
- environment identity;
- accessible tenant listing;
- tenant selection;
- stateless logout.

It cannot read or mutate tenant or project product data.

Tenant selection verifies current membership and issues a new token containing exactly one positive `tenant_id`. Product requests require that selected-tenant claim. A route `tenantId` outside the selected tenant returns a generic not-found refusal without disclosing foreign tenant existence.

Project routes are checked after tenant scope and must resolve through active project membership in the selected tenant. Project creation treats the selected tenant as authoritative; body `tenantId` may be omitted or must match.

## Pipeline

Authenticated writes are attributed before tenant and project scope checks. Cross-tenant and cross-project write attempts are therefore both inert and durably recorded as refused.

Token claims select scope. They do not grant project visibility, administrative role, workflow authority, approval, continuation, or source apply.
