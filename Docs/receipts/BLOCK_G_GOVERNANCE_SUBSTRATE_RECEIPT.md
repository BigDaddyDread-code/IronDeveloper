# Block G Governance Substrate Receipt

This report is a receipt, not a trophy.

## 1. Summary

Block G created a durable governance substrate for IronDev.

It records governance-relevant facts in SQL.

It does not approve, execute, promote memory, mutate source, route workflow, transfer authority, or approve release.

Hard claim:

```text
IronDev now has a durable governance substrate.
It records authority-adjacent facts.
It does not grant authority.
```

## 2. What Block G delivered

| PR | Slice | Purpose | Durable object added | Authority boundary preserved | Validation evidence |
| --- | --- | --- | --- | --- | --- |
| PR 72 | GovernanceEvent Store | Add append-only governance event ledger. | `governance.GovernanceEvent` | Event is evidence, not approval or execution. | GovernanceEventStore, DatabaseMigrationReceipt |
| PR 73 | GovernanceEvent Read Model | Add repository and read model for governance events. | No new table beyond `governance.GovernanceEvent` | Read model inspects evidence; it does not become authority. | GovernanceEventStore |
| PR 74 | Durable ToolRequest Store | Replace temporary request cache with durable SQL tool request records. | `governance.ToolRequest` | Tool request is request form, not execution permission. | ToolRequestStore, RealDatabaseToolRequestSmoke |
| PR 74a | Migration Application Receipt | Prove ordered migration apply and verify path. | No new ledger | Migration receipt is not schema authority beyond the manifest. | DatabaseMigrationReceipt |
| PR 74b | Retrospective SQL Inventory | Inventory SQL and runtime dependency surface. | No new ledger | Inventory is not migration, approval, or execution authority. | SqlInventory |
| PR 74c | Real DB ToolRequest Smoke Receipt | Prove tool request storage against real DBs. | No new ledger | Real DB smoke proves storage/retrieval, not release readiness. | RealDatabaseToolRequestSmoke |
| PR 75 | Durable ToolGateDecision Store | Add durable gate decision evidence. | `governance.ToolGateDecision` | Gate pass is not approval and not execution. | ToolGateDecisionStore, RealDatabaseToolGateDecisionSmoke |
| PR 76 | Durable ApprovalDecision Store | Add durable approval decision ledger. | `governance.ApprovalDecision` | Approval record records approval; it does not execute. | ApprovalDecisionStore, RealDatabaseApprovalDecisionSmoke |
| PR 77 | Durable PolicyDecisionEvent Store | Add durable policy check evidence. | `governance.PolicyDecisionEvent` | Policy decision is not approval, permission, or policy satisfaction. | PolicyDecisionEventStore, RealDatabasePolicyDecisionSmoke |
| PR 78 | Durable DogfoodReceipt Store | Add durable dogfood receipt evidence. | `governance.DogfoodReceipt` | Dogfood receipt is evidence, not release approval. | DogfoodReceiptStore, RealDatabaseDogfoodReceiptSmoke |
| PR 79 | ThoughtLedger GovernanceEvent References | Link ThoughtLedger entries to governance events. | `governance.ThoughtLedgerGovernanceEventReference` | ThoughtLedger reference is evidence link only, not authority transfer. | ThoughtLedgerGovernanceReference, RealDatabaseThoughtLedgerGovernanceReferenceSmoke |
| PR 80 | Governance Substrate Contract Tests | Prove the Block G ledgers work together without authority creep. | No new ledger | Contract tests prove the combined substrate remains non-authoritative. | GovernanceSubstrateContract |

## 3. Current durable governance ledgers

| SQL ledger | What it records | What it does not grant | Governance event relation | Append-only | Project-scoped |
| --- | --- | --- | --- | --- | --- |
| `governance.GovernanceEvent` | Canonical governance event envelope, correlation, subject, cause, payload, and payload hash. | Approval, execution, source apply, memory promotion, workflow, release approval, or authority transfer. | It is the event ledger. | Yes | Yes |
| `governance.ToolRequest` | Durable request form for a proposed tool action. | Execution permission, approval, gate pass, policy satisfaction, or tool result. | Emits a ToolRequest governance event. | Yes | Yes |
| `governance.ToolGateDecision` | Durable gate evaluation for a tool request. | Human approval, execution, source apply, memory promotion, workflow progress, or release approval. | Emits a ToolGateDecision governance event and links to ToolRequest. | Yes | Yes |
| `governance.ApprovalDecision` | Durable explicit approval decision record for a scoped subject. | Execution, source apply, memory promotion, workflow continuation, policy satisfaction, or release approval unless the scoped subject explicitly records release approval in a later separate system. | Emits an ApprovalDecision governance event. | Yes | Yes |
| `governance.PolicyDecisionEvent` | Durable policy-check evidence at a point in time. | Approval, permission, policy satisfaction, execution, source apply, memory promotion, workflow, or release approval. | Emits a PolicyDecisionEvent governance event and may reference request/gate/approval evidence. | Yes | Yes |
| `governance.DogfoodReceipt` | Durable dogfood outcome receipt and related evidence. | Release approval, policy satisfaction, execution permission, workflow continuation, source apply, or memory promotion. | Emits a DogfoodReceipt governance event and may reference request/gate/approval/policy evidence. | Yes | Yes |
| `governance.ThoughtLedgerGovernanceEventReference` | Durable reference from visible ThoughtLedger entry IDs to governance events. | Approval, execution, policy satisfaction, workflow progress, source apply, dogfood creation, release approval, A2A handoff, memory promotion, or authority transfer. | Links a ThoughtLedger entry to an existing GovernanceEvent. | Yes | Yes |

## 4. Authority boundary matrix

| Record type | Approval? | Execution? | Source apply? | Memory promotion? | Workflow? | Release approval? |
| --- | --- | --- | --- | --- | --- | --- |
| ToolRequest | No | No | No | No | No | No |
| ToolGateDecision | No | No | No | No | No | No |
| ApprovalDecision | Record only | No | No | No | No | No unless a future scoped release approval record explicitly says so |
| PolicyDecisionEvent | No | No | No | No | No | No |
| DogfoodReceipt | No | No | No | No | No | No |
| ThoughtLedgerGovernanceEventReference | No | No | No | No | No | No |

Important boundary statements:

- ApprovalDecision records approval, but does not execute.
- DogfoodReceipt Passed is not release approval.
- PolicyDecisionEvent NoPolicyBlock is not permission.
- ToolGateDecision Passed is not approval.
- ThoughtLedgerGovernanceEventReference is evidence link only.
- Gate pass is not human approval.
- Audit evidence is not approval.
- Model output is advisory only.

## 5. SQL and migration status

Block G now has:

- ordered migration manifest: `Database/migrations.json`
- migration apply script: `Database/apply-migrations.ps1`
- migration verify script: `Database/verify-migrations.ps1`
- SQL inventory: `Database/sql-inventory.json`
- backend SQL inventory doc: `Docs/BACKEND_SQL_INVENTORY.md`
- inline SQL inventory doc: `Docs/BACKEND_INLINE_SQL_INVENTORY.md`
- real DB smoke receipts under `Docs/receipts`
- verifier coverage for governance tables, stored procedures, append-only triggers, foreign keys, JSON check constraints, version check constraints, and authority-blocking check constraints

The receipt covers both configured real databases:

- `IronDeveloper`
- `IronDeveloper_Test`

## 6. Real DB proof

Real database smoke coverage exists for:

- ToolRequest
- ToolGateDecision
- ApprovalDecision
- PolicyDecisionEvent
- DogfoodReceipt
- ThoughtLedgerGovernanceEventReference

Real DB smoke proves storage and retrieval. It does not prove release readiness.

## 7. API/CLI status

- API/CLI remain exposure surfaces only.
- Tool Request API is SQL-backed.
- Tool Gate API is SQL-backed after PR75 durable gate decision storage.
- Dogfood Loop API is SQL-backed after PR78 durable dogfood receipt storage.
- Approval and policy stores do not add public API/CLI authority unless a later PR explicitly exposes them as evidence-only.
- CLI remains API client only.
- No CLI command bypasses backend policy or writes SQL directly.
- API status is not governance.
- CLI output is not governance.

## 8. ThoughtLedger status

- ThoughtLedger can now reference durable governance events.
- References are evidence links only.
- References do not store hidden chain-of-thought.
- References do not approve, execute, satisfy policy, promote memory, mutate source, start workflow, create dogfood receipts, or transfer authority.

## 9. Explicit non-claims

Block G does not mean:

- IronDev is release-ready.
- L4 agents are ready.
- workflow orchestration exists.
- A2A exists.
- LangGraph is integrated.
- memory promotion is safe or available.
- source apply is available.
- policy engine exists.
- approval evaluator exists.
- project autonomy model exists.
- UI is ready.
- dogfood receipts approve release.
- gate pass approves execution.
- approval records execute anything.
- policy decisions grant permission.

This block does not claim production ready status.
This block does not claim product release ready status.
This block does not claim fully autonomous operation.
This block does not claim L4 complete status.
This block does not claim workflow ready status.
This block does not claim source apply ready status.
This block does not claim memory promotion approved status.
This block does not claim release approved status.
This block does not claim IronDev can ship.

## 10. Known gaps after Block G

Future blocks remain separate work:

- Block H - Project Authority and Approval Policy Model
- Block I - A2A Handoff Contract Spine
- Block J - Workflow State and Checkpoint Spine
- Block K - MemoryImprovementAgent L2/L3
- Block L - Minimal Governed Workflow Runner
- Block M - L4 Candidate Workflows

Known gaps:

- no workflow state yet
- no A2A handoff contracts yet
- no policy evaluator yet
- no memory proposal staging yet
- no source apply path yet
- no release approval gate yet
- no LangGraph integration yet
- no project autonomy policy model yet

## 11. Merge standard evidence

Latest Block G closeout validation evidence:

| Band | Command filter | Result |
| --- | --- | --- |
| Governance substrate contract | `GovernanceSubstrateContract` | Passed 10/10 |
| Migration inventory and real DB smoke | `DatabaseMigrationReceipt|SqlInventory|RealDatabaseToolRequestSmoke|RealDatabaseToolGateDecisionSmoke|RealDatabaseApprovalDecisionSmoke|RealDatabasePolicyDecisionSmoke|RealDatabaseDogfoodReceiptSmoke|RealDatabaseThoughtLedgerGovernanceReferenceSmoke` | Passed 32/32 |
| Governance stores | `GovernanceEventStore|ToolRequestStore|ToolGateDecisionStore|ApprovalDecisionStore|PolicyDecisionEventStore|DogfoodReceiptStore|ThoughtLedgerGovernanceReference` | Passed 85/85 |
| API governance surfaces | `ToolRequestApi|ToolGateApi|DogfoodLoopApi` | Passed 44/44 |
| API/CLI and ThoughtLedger | `ApiCliContract|ApiCliReleaseGate|ThoughtLedger` | Passed 64/64 |
| Build | `dotnet build IronDev.slnx --no-restore -v:minimal` | Passed, 0 errors |
| Whitespace | `git diff --check` | Passed |

Required validation categories included:

- GovernanceSubstrateContract
- DatabaseMigrationReceipt
- SqlInventory
- RealDatabaseToolRequestSmoke
- RealDatabaseToolGateDecisionSmoke
- RealDatabaseApprovalDecisionSmoke
- RealDatabasePolicyDecisionSmoke
- RealDatabaseDogfoodReceiptSmoke
- RealDatabaseThoughtLedgerGovernanceReferenceSmoke
- GovernanceEventStore
- ToolRequestStore
- ToolGateDecisionStore
- ApprovalDecisionStore
- PolicyDecisionEventStore
- DogfoodReceiptStore
- ThoughtLedgerGovernanceReference
- ToolRequestApi
- ToolGateApi
- DogfoodLoopApi
- ApiCliContract
- ApiCliReleaseGate
- ThoughtLedger

## 12. Final receipt statement

Block G is complete as a durable governance substrate.

It gives IronDev durable, project-scoped governance evidence records and contract tests proving those records remain non-authoritative.

It does not make IronDev release-ready.
It does not deliver L4.
It does not deliver workflow orchestration.
It does not deliver autonomous source apply or memory promotion.

The durable governance substrate is now in place and tested. IronDev can safely proceed to the next block because authority-adjacent facts are durable, project-scoped, queryable, and contract-tested as non-authoritative.
