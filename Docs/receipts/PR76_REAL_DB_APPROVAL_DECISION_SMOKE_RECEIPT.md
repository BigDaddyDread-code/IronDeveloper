# PR76 Real DB Approval Decision Smoke Receipt

PR76 adds a durable SQL-backed approval decision store.

Smoke command:

```powershell
.\Database\smoke-approval-decision.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper" -TrustServerCertificate
.\Database\smoke-approval-decision.ps1 -Server "DESKTOP-KFA0H13" -Database "IronDeveloper_Test" -TrustServerCertificate
```

Expected receipt fields:

- `durableApprovalDecisionRecorded`: true
- `approvalGovernanceEventRecorded`: true
- `approvalDecisionIsExecutionPermission`: false
- `toolExecuted`: false
- `sourceApplied`: false
- `memoryPromoted`: false
- `workflowStarted`: false
- `a2aHandoffCreated`: false
- `dogfoodReceiptCreated`: false
- `externalEffectCreated`: false

The smoke creates a durable tool request and a durable tool gate decision only as evidence setup, then records an explicit `approval.decision.recorded` event and an `ApprovalDecision` row for the scoped subject.

Approval remains evidence. It does not execute tools, mutate source, promote memory, create dogfood receipts, start workflow, or create A2A handoffs.
