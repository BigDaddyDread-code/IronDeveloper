# Block G Governance Substrate Invariants

Block G is the durable governance substrate. It records facts so later layers can inspect evidence without turning evidence into authority.

## Ledger boundaries

- ToolRequest is request form only.
- ToolGateDecision is gate evaluation only.
- ApprovalDecision is explicit decision record only.
- PolicyDecisionEvent is policy-check evidence only.
- DogfoodReceipt is evidence only.
- ThoughtLedgerGovernanceEventReference is evidence link only.

None of these records execute tools, approve release readiness, mutate source, promote memory, continue workflow, or transfer authority.

## Non-authority rules

- Gate pass is not human approval.
- Approval record is not execution.
- Policy decision is not approval, permission, or policy satisfaction.
- Dogfood receipt is not release approval.
- ThoughtLedger reference is not ownership or authority transfer.
- Audit evidence is not approval.
- Model output remains advisory only.
- Human review remains required for source apply and memory promotion.
