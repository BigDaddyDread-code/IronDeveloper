# CLN-11 Route And Body Scope Binding Receipt

**Date:** 13 July 2026

**Branch:** `cleanup/cln-11-route-body-scope-binding`

## Scope

Audited 111 controller write actions and added one pre-action scope-binding guard for all 81 routes carrying project or tenant scope.

## Contract

- Route scope is authoritative.
- Omitted/default body scope is accepted.
- Matching body scope is accepted.
- Conflicting nested or direct scope is refused with a stable reason code before mutation.
- Read requests are unchanged.
- Thirty writes without route scope are explicitly classified and are not misreported as route/body proof.

## Evidence

```text
dotnet test .\IronDev.IntegrationTests\IronDev.IntegrationTests.csproj --filter FullyQualifiedName~RouteBodyScopeBindingFilterTests
6 passed
```

## Result

Project- and tenant-scoped routes now share one enforceable mismatch contract instead of controller-by-controller silent overwrites.
