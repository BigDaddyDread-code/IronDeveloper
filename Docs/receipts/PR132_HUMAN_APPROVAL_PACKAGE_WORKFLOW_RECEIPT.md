# PR132 Human Approval Package Workflow Receipt

## Summary

PR132 adds a Human Approval Package candidate workflow for Block M.

The workflow prepares a safe, non-authoritative review package from supplied candidate workflow evidence. It can organize references from workflow step evaluation, dry-run evidence, boxed route suggestions, test failure review, critic review request, implementation proposal package, tool request gate preview, and memory improvement package material.

Approval package is not approval.

Requested decision is not decision made.

Gate hint is not gate satisfaction.

Package output cannot grant authority.

This is a Block M L4 candidate workflow and remains non-mutating.

## Boundary

This workflow is package preparation only.

It does not approve, reject, deny, satisfy approval, satisfy policy, transition workflow state, mutate source, apply patches, invoke tools, dispatch agents, call models, build prompts, create tickets, promote memory, activate retrieval, write SQL, or add runtime wiring.

The output can say that later human/governed review is required. It cannot say that review has happened.

The output can include gate hints. It cannot satisfy a gate.

The output can include a requested decision shape. It cannot make the decision.

The output can include upstream candidate packages. It cannot convert them into authority.

## Implemented contract

- `IHumanApprovalPackageCandidateWorkflow.Prepare(...)`
- `HumanApprovalPackageCandidateRequest`
- `HumanApprovalPackageCandidateResult`
- typed target, approval, requested decision, evidence, candidate package, gate, risk, status, and reason enums

The interface exposes only `Prepare`.

There is no `Approve`, `Reject`, `Deny`, `GrantApproval`, `SatisfyApproval`, `SatisfyPolicy`, `ContinueWorkflow`, `TransitionWorkflow`, `Run`, `Execute`, `Dispatch`, `InvokeTool`, `CallModel`, `BuildPrompt`, `ApplyPatch`, `PromoteMemory`, or `ActivateRetrieval` method.

## Status outcomes

- `InvalidRequest`
- `BlockedByWorkflowGate`
- `MissingRequiredApprovalEvidence`
- `ApprovalPackageProduced`

`ApprovalPackageProduced` means the package was assembled from supplied safe evidence.

It does not mean approval was granted.

It does not mean rejection was recorded.

It does not mean approval was satisfied.

It does not mean policy was satisfied.

It does not mean workflow state changed.

## Authority flags

The result is always package-only.

All authority/action flags remain false:

- `IsApprovalDecision`
- `IsApproved`
- `IsRejected`
- `CanSatisfyApproval`
- `CanSatisfyPolicy`
- `CanTransitionWorkflow`
- `CanMutateSource`
- `CanApplyPatch`
- `CanInvokeTool`
- `CanDispatchAgent`
- `CanCallModel`
- `CanBuildPrompt`
- `CanCreateTicket`
- `CanPromoteMemory`
- `CanActivateRetrieval`
- `CanWriteSql`

## Validation coverage

PR132 adds focused tests for:

- valid package preparation
- invalid identity and enum inputs
- unsafe text rejection without echoing hidden/raw material
- missing approval summary/evidence/gate material
- target-specific missing package material
- runner, dry-run, and route blocker handling
- approval halt snapshots as evidence only
- upstream package blocker handling
- upstream package inclusion as evidence only
- deterministic output
- authority boundary flags
- method surface restrictions
- forbidden payload property restrictions
- receipt wording

## Review line

PR132 prepares the approval folder. It does not sign it.
