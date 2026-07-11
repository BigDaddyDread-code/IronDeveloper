# API-CONTRACT-2 Generated Request Consumption

## Purpose

Close the remaining request-shape split after the generated API baseline reset. Active client writes now consume checked-in OpenAPI components instead of maintaining parallel transport interfaces.

## Backend contract changes

- Chat session writes accept `SaveProjectChatSessionRequest` and map it to the persistence model inside the API boundary.
- Chat message writes accept `SaveProjectChatMessageRequest`; route/project checks and server-owned source attribution remain unchanged.
- Accepted Approval and Policy Satisfaction continue to inspect raw JSON before deserialization so server-owned-field refusals remain intact.
- `GovernedJsonRequestBodyOperationFilter` publishes the typed create schemas for those raw governed bodies without changing runtime binding.

## Client contract changes

Request types in `IronDev.TauriShell/src/api/types.ts` are aliases or constrained aliases of generated components. Constrained aliases keep useful UI-side requiredness while deriving the transport property set from OpenAPI.

Client-owned view state, response composition, and authority-boundary display models remain handwritten. API-CONTRACT-2 does not replace the HTTP facade.

## Proof

`ApiContractGeneratedRequestConsumptionTests` verifies:

- Chat write actions do not accept persistence models;
- the four previously hidden request bodies have named OpenAPI schemas;
- active client transport requests cannot drift back to standalone interfaces.

The normal contract command remains:

```powershell
.\tools\contracts\update-openapi-contract.ps1 -Check
```

Generated request types describe payload shape only. They do not grant approval, execution, source mutation, workflow continuation, release, deployment, or memory authority.
