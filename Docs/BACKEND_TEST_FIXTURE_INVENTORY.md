# Backend test fixture inventory

PR 49 is test cleanup, not proof weakening. This inventory records the duplicated backend test setup consolidated before the Backend Contract Freeze Report.

No behavior change intended.

No SQL/API/CLI/UI/runtime/persistence/capability changes.

## Consolidated fixtures

| Existing duplicated setup pattern | Test files using it | Shared fixture/helper | Invariant protected | Shared or local | Coverage preserved |
| --- | --- | --- | --- | --- | --- |
| TestingAgent `AgentToolRequest` for a gated `TestRun` | `ManualTesterAgentToolExecutionTests`, `ToolExecutionAuditStoreTests` | `BackendAgentToolRequestFixtures.TestingAgentTestRunRequest` | Tool request is a request form, not execution permission. Test execution still requires gate evidence. | Shared | Existing execution, audit, and SQL-boundary assertions remain in place. |
| ImplementationAgent `AgentToolRequest` for proposal-only patch generation | `ManualImplementationPatchProposalTests`, `ToolExecutionAuditStoreTests` | `BackendAgentToolRequestFixtures.ImplementationAgentPatchProposalRequest` | Proposal is not apply. Patch proposal request does not mutate source and does not claim approval. | Shared | Proposal-only output, audit, and tool-audit assertions remain in place. |
| Allowed gate decision for controlled test run | `ManualTesterAgentToolExecutionTests`, `ToolExecutionAuditStoreTests` | `BackendAgentToolRequestFixtures.GateAllowedForTesterTestRun` | Gate allows a future executor only; the gate does not execute. | Shared | Tests still assert executor call count and blocked dangerous gate fields. |
| Allowed gate decision for proposal-only patch request | `ManualImplementationPatchProposalTests`, `ToolExecutionAuditStoreTests` | `BackendAgentToolRequestFixtures.GateAllowedForPatchProposal` | Gate is not executor. Proposal gate does not apply, write, or mutate source. | Shared | Tests still assert no `RunTool` allowed and no source mutation authority. |
| Manual tester execution request with governance gate approval | `ManualTesterAgentToolExecutionTests`, `ToolExecutionAuditStoreTests` | `BackendManualToolExecutionFixtures.TesterExecutionRequestWithGovernanceGateApproval` | Audit is not approval; execution remains explicit and scripted in tests. | Shared | Existing test-run success, failure, rejection, audit, and SQL audit coverage remains. |
| Manual implementation patch proposal request | `ManualImplementationPatchProposalTests`, `ToolExecutionAuditStoreTests` | `BackendManualToolExecutionFixtures.PatchProposalRequestThatDoesNotApplySource` | Proposal remains proposal-only and never claims patch application or source mutation. | Shared | Existing proposal-only and audit-envelope assertions remain. |
| Safe scripted test executor output | `ManualTesterAgentToolExecutionTests`, `ToolExecutionAuditStoreTests` | `BackendManualToolExecutionFixtures.ScriptedTestExecutorSucceedsWithEvidence` | Test output carries evidence and no dangerous authority/mutation flags. | Shared | Existing unsafe-output rejection tests remain local and explicit. |
| Safe scripted patch proposal generator output | `ManualImplementationPatchProposalTests`, `ToolExecutionAuditStoreTests` | `BackendManualToolExecutionFixtures.ScriptedPatchProposalGeneratorReturnsProposalOnlyPackage` | Fix proposal records do not apply patches, write files, run git, or create authority. | Shared | Existing unsafe package/file/hunk rejection tests remain local and explicit. |
| Proposal-only patch package/file/hunk objects | `ManualImplementationPatchProposalTests`, `ToolExecutionAuditStoreTests` | `PatchProposalPackageThatDoesNotApply`, `ProposedFileChangeThatDoesNotWrite`, `ProposedPatchHunkThatIsNotApplied` | Proposal is not apply; file-change evidence is descriptive only. | Shared | Existing assertions continue to verify proposal-only package details. |

## Intentionally local fixtures

| Local fixture/setup pattern | Test files using it | Why it stays local | Invariant protected |
| --- | --- | --- | --- |
| Exhaustive gate policy and approval contexts | `AgentToolExecutionGateTests` | These tests intentionally spell out each policy and approval combination in the test body. A shared helper would hide gate-specific proof. | Gate is not executor; approval and policy remain separate gates. |
| Direct SQL insert/update/delete helpers | `ToolExecutionAuditStoreTests` | SQL-boundary proof should stay close to the table/procedure test. | SQL remains source of truth and append-only constraints are non-bypassable. |
| Model adapter/sanitiser safe request/response helpers | `AgentModelAdapterBoundaryTests`, `AgentModelAuditSanitisationTests`, model-backed harness tests | Model-boundary tests need local unsafe variants to keep prompt/response authority markers visible. | Model output remains advisory only and raw reasoning is rejected. |
| Memory silo, influence, handoff, proposal, and retrieval setup | `AgentMemory` tests | These fixtures are close to SQL/memory governance tables and should not be collapsed into agent tool fixtures. | Memory safe is not approval; retrieval match is not memory candidate; candidate is not memory. |
| Real-run dogfood and repair loop receipts | `ManualDogfoodHarnessTests`, `ManualRealRunMemoryImprovementTests`, repair loop tests | These receipts are narrative/evidence fixtures and their scenario names carry the proof intent. | Dogfood evidence is not authority; repair proposal is not repair execution. |
| Backend naming normalisation samples | `BackendNamingNormalisationTests` | PR 48 vocabulary proof is clearer with inline terms. | Retrieval match remains distinct from candidate and memory. |

## Boundary confirmation

- No boundary tests were removed.
- No assertions were weakened intentionally.
- Shared fixtures do not auto-approve, auto-promote, auto-apply, auto-execute, auto-persist, fake human approval, or convert retrieval matches into candidates.
- Dangerous states remain explicit in the tests that need them.
- Retrieval match remains distinct from memory candidate.
- Candidate remains distinct from memory.
- Proposal remains distinct from apply.
- Audit remains distinct from approval.
- Gate remains distinct from executor.
- Critic remains distinct from governance.
- Memory safe remains distinct from promotion.
- Vector/index remains retrieval only.
