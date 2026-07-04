# PR79 Real DB ThoughtLedger Governance Reference Smoke Receipt

## Command

```powershell
.\Database\smoke-thoughtledger-governance-reference.ps1 -Server "(localdb)\MSSQLLocalDB" -Database "IronDeveloper_Test" -TrustServerCertificate
```

## Boundary

ThoughtLedger governance reference is evidence only.

- ThoughtLedger governance reference is not approval.
- ThoughtLedger governance reference is not execution permission.
- ThoughtLedger governance reference is not policy satisfaction.
- ThoughtLedger governance reference is not source apply.
- ThoughtLedger governance reference is not memory promotion.
- ThoughtLedger governance reference is not workflow continuation.
- ThoughtLedger governance reference is not release approval.
- ThoughtLedger governance reference is not dogfood receipt creation.
- ThoughtLedger governance reference is not A2A handoff creation.

## Expected result

The smoke script creates one governance event through `governance.AppendGovernanceEvent`, records one ThoughtLedger governance reference through `governance.usp_ThoughtLedgerGovernanceEventReference_Record`, and reads the reference row back from `governance.ThoughtLedgerGovernanceEventReference`.

The script does not create approval decisions, policy decisions, gate decisions, tool requests, dogfood receipts, workflow records, source apply records, A2A handoffs, or memory promotion records.
