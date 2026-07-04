# PR77 Real DB Policy Decision Smoke Receipt

PR77 adds a durable SQL-backed policy decision event store.

Smoke command:

```powershell
.\Database\smoke-policy-decision.ps1 -Server "(localdb)\MSSQLLocalDB" -Database "IronDeveloper" -TrustServerCertificate
.\Database\smoke-policy-decision.ps1 -Server "(localdb)\MSSQLLocalDB" -Database "IronDeveloper_Test" -TrustServerCertificate
```

Expected receipt fields:

- `durablePolicyDecisionRecorded`: true
- `policyGovernanceEventRecorded`: true
- `policyDecisionIsApproval`: false
- `policyDecisionIsExecutionPermission`: false
- `toolExecuted`: false
- `sourceApplied`: false
- `memoryPromoted`: false
- `workflowStarted`: false
- `a2aHandoffCreated`: false
- `dogfoodReceiptCreated`: false
- `externalEffectCreated`: false

The smoke creates durable tool request, tool gate decision, and approval decision rows only as evidence setup, then records an explicit `policy.decision.recorded` event and a `PolicyDecisionEvent` row for the scoped subject.

Policy decision remains evidence. It does not approve, execute tools, mutate source, promote memory, create dogfood receipts, start workflow, satisfy policy, transfer authority, or create A2A handoffs.
