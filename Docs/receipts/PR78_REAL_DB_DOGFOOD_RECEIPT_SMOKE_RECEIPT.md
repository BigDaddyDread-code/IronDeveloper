# PR78 Real DB Dogfood Receipt Smoke Receipt

## Purpose

This receipt documents the real database smoke path for PR78 durable dogfood receipt storage.

The smoke script records one dogfood receipt through `governance.usp_DogfoodReceipt_Record` and verifies that the receipt is linked to a `dogfood.receipt.recorded` governance event.

## Commands

Run database-backed smoke commands sequentially.

```powershell
.\Database\apply-migrations.ps1 -Server ".\SQLEXPRESS" -Database "IronDeveloper_Test" -TrustServerCertificate
.\Database\verify-migrations.ps1 -Server ".\SQLEXPRESS" -Database "IronDeveloper_Test" -TrustServerCertificate
.\Database\smoke-dogfood-receipt.ps1 -Server ".\SQLEXPRESS" -Database "IronDeveloper_Test" -TrustServerCertificate
```

Repeat against `IronDeveloper` only after the test database receipt is clean.

## Expected proof fields

- `durableDogfoodReceiptRecorded = true`
- `dogfoodGovernanceEventRecorded = true`
- `dogfoodReceiptIsReleaseApproval = false`
- `dogfoodReceiptIsExecutionPermission = false`
- `policyDecisionCreated = false`
- `approvalDecisionCreated = false`
- `gateDecisionCreated = false`
- `toolRequestCreated = false`
- `toolExecuted = false`
- `sourceApplied = false`
- `memoryPromoted = false`
- `workflowStarted = false`
- `a2aHandoffCreated = false`

## Boundary

Dogfood receipt is evidence only.

Dogfood receipt is not release approval.

Dogfood receipt is not execution permission.

Dogfood receipt is not policy satisfaction.

Dogfood receipt is not source apply.

Dogfood receipt is not memory promotion.

Dogfood receipt is not workflow continuation.

Dogfood receipt is not A2A handoff.

Human review remains required for source apply and memory promotion.
