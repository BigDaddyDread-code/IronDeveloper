using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;
using IronDev.Infrastructure.Governance;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("GovernanceSubstrateContract")]
[TestCategory("GovernanceSubstrateAuthorityBoundary")]
[TestCategory("GovernanceSubstrateStaticBoundary")]
public sealed class GovernanceSubstrateContractTests : IntegrationTestBase
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private IToolRequestStore _toolRequests = default!;
    private IToolGateDecisionStore _gateDecisions = default!;
    private IApprovalDecisionStore _approvalDecisions = default!;
    private IPolicyDecisionEventStore _policyDecisions = default!;
    private IDogfoodReceiptStore _dogfoodReceipts = default!;
    private IThoughtLedgerGovernanceEventReferenceStore _thoughtLedgerReferences = default!;
    private IGovernanceEventStore _governanceEvents = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropGovernanceSchemaAsync();
        await ApplyGovernanceMigrationsAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _toolRequests = new SqlToolRequestStore(connectionFactory);
        _gateDecisions = new SqlToolGateDecisionStore(connectionFactory);
        _approvalDecisions = new SqlApprovalDecisionStore(connectionFactory);
        _policyDecisions = new SqlPolicyDecisionEventStore(connectionFactory);
        _dogfoodReceipts = new SqlDogfoodReceiptStore(connectionFactory);
        _thoughtLedgerReferences = new SqlThoughtLedgerGovernanceEventReferenceStore(connectionFactory);
        _governanceEvents = new SqlGovernanceEventStore(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropGovernanceSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public async Task GovernanceSubstrate_CanRecordFullEvidenceChain()
    {
        var projectId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var chain = await CreateFullEvidenceChainAsync(projectId, correlationId);

        Assert.IsNotNull(await _toolRequests.GetAsync(chain.ToolRequest.ToolRequestId));
        Assert.IsNotNull(await _gateDecisions.GetAsync(TenantId, projectId, chain.GateDecision.ToolGateDecisionId));
        Assert.IsNotNull(await _approvalDecisions.GetAsync(chain.ApprovalDecision.ApprovalDecisionId));
        Assert.IsNotNull(await _policyDecisions.GetAsync(chain.PolicyDecision.PolicyDecisionEventId));
        Assert.IsNotNull(await _dogfoodReceipts.GetAsync(chain.DogfoodReceipt.DogfoodReceiptId));
        Assert.IsNotNull(await _thoughtLedgerReferences.GetAsync(chain.ThoughtLedgerReference.ThoughtLedgerGovernanceEventReferenceId));

        Assert.AreEqual(chain.ToolRequest.ToolRequestId, chain.GateDecision.ToolRequestId);
        Assert.AreEqual(chain.ToolRequest.ToolRequestId.ToString("D"), chain.ApprovalDecision.SubjectId);
        Assert.AreEqual(chain.ToolRequest.ToolRequestId, chain.PolicyDecision.RelatedToolRequestId);
        Assert.AreEqual(chain.GateDecision.ToolGateDecisionId, chain.PolicyDecision.RelatedToolGateDecisionId);
        Assert.AreEqual(chain.ApprovalDecision.ApprovalDecisionId, chain.PolicyDecision.RelatedApprovalDecisionId);
        Assert.AreEqual(chain.PolicyDecision.PolicyDecisionEventId, chain.DogfoodReceipt.RelatedPolicyDecisionEventId);
        Assert.AreEqual(chain.DogfoodReceipt.GovernanceEventId, chain.ThoughtLedgerReference.GovernanceEventId);

        var events = await GovernanceRowsForCorrelationAsync(projectId, correlationId);
        CollectionAssert.IsSubsetOf(
            new[]
            {
                "tool.request.created",
                "tool.gate.decision.recorded",
                "approval.decision.recorded",
                "policy.decision.recorded",
                "dogfood.receipt.recorded"
            },
            events.Select(row => row.EventType).ToArray());

        AssertCausation(events, chain.GateDecision.GovernanceEventId, chain.ToolRequest.GovernanceEventId);
        AssertCausation(events, chain.ApprovalDecision.GovernanceEventId, chain.GateDecision.GovernanceEventId);
        AssertCausation(events, chain.PolicyDecision.GovernanceEventId, chain.ApprovalDecision.GovernanceEventId);
        AssertCausation(events, chain.DogfoodReceipt.GovernanceEventId, chain.PolicyDecision.GovernanceEventId);

        var causedByPolicy = await _governanceEvents.ListCausedByAsync(new GovernanceEventsCausedByQuery { CausationId = chain.PolicyDecision.GovernanceEventId });
        Assert.IsTrue(causedByPolicy.Any(row => row.EventId == chain.DogfoodReceipt.GovernanceEventId));
    }

    [TestMethod]
    public async Task GovernanceSubstrate_ReadModelsCanInspectEvidenceWithoutPayloadDump()
    {
        var chain = await CreateFullEvidenceChainAsync(Guid.NewGuid(), Guid.NewGuid());

        AssertNoProperty<GovernanceEventSummary>("PayloadJson");
        AssertNoProperty<ToolRequestSummary>("RequestPayloadJson");
        AssertNoProperty<ApprovalDecisionSummary>("EvidenceJson");
        AssertNoProperty<PolicyDecisionSummary>("EvidenceJson");
        AssertNoProperty<DogfoodReceiptSummary>("EvidenceJson");
        AssertNoProperty<ThoughtLedgerGovernanceEventReferenceSummary>("MetadataJson");

        Assert.AreEqual(5, (await _governanceEvents.ListForCorrelationAsync(new GovernanceEventsForCorrelationQuery { CorrelationId = chain.ToolRequest.CorrelationId!.Value, Take = 20 })).Count);
        Assert.AreEqual(1, (await _toolRequests.ListForProjectAsync(new ToolRequestsForProjectQuery { ProjectId = chain.ToolRequest.ProjectId, Take = 20 })).Count);
        Assert.AreEqual(1, (await _approvalDecisions.ListForProjectAsync(new ApprovalDecisionsForProjectQuery { ProjectId = chain.ToolRequest.ProjectId, Take = 20 })).Count);
        Assert.AreEqual(1, (await _policyDecisions.ListForProjectAsync(new PolicyDecisionsForProjectQuery { ProjectId = chain.ToolRequest.ProjectId, Take = 20 })).Count);
        Assert.AreEqual(1, (await _dogfoodReceipts.ListForProjectAsync(new DogfoodReceiptsForProjectQuery { ProjectId = chain.ToolRequest.ProjectId, Take = 20 })).Count);
        Assert.AreEqual(1, (await _thoughtLedgerReferences.ListForCorrelationAsync(new ThoughtLedgerGovernanceReferencesForCorrelationQuery { ProjectId = chain.ToolRequest.ProjectId, CorrelationId = chain.ToolRequest.CorrelationId!.Value, Take = 20 })).Count);
    }

    [TestMethod]
    public async Task GovernanceSubstrate_ProjectQueriesDoNotLeakOtherProjectRecords()
    {
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var otherCorrelationId = Guid.NewGuid();

        var chain = await CreateFullEvidenceChainAsync(projectId, correlationId);
        var other = await CreateFullEvidenceChainAsync(otherProjectId, otherCorrelationId);

        Assert.IsTrue((await _toolRequests.ListForProjectAsync(new ToolRequestsForProjectQuery { ProjectId = projectId, Take = 20 })).All(row => row.ProjectId == projectId));
        Assert.IsTrue((await _gateDecisions.ListForProjectAsync(new ToolGateDecisionProjectQuery(TenantId, projectId, 20))).All(row => row.ProjectId == projectId));
        Assert.IsTrue((await _approvalDecisions.ListForProjectAsync(new ApprovalDecisionsForProjectQuery { ProjectId = projectId, Take = 20 })).All(row => row.ProjectId == projectId));
        Assert.IsTrue((await _policyDecisions.ListForProjectAsync(new PolicyDecisionsForProjectQuery { ProjectId = projectId, Take = 20 })).All(row => row.ProjectId == projectId));
        Assert.IsTrue((await _dogfoodReceipts.ListForProjectAsync(new DogfoodReceiptsForProjectQuery { ProjectId = projectId, Take = 20 })).All(row => row.ProjectId == projectId));
        Assert.IsTrue((await _thoughtLedgerReferences.ListForCorrelationAsync(new ThoughtLedgerGovernanceReferencesForCorrelationQuery { ProjectId = projectId, CorrelationId = correlationId, Take = 20 })).All(row => row.ProjectId == projectId));
        Assert.IsFalse((await _toolRequests.ListForProjectAsync(new ToolRequestsForProjectQuery { ProjectId = projectId, Take = 20 })).Any(row => row.ToolRequestId == other.ToolRequest.ToolRequestId));
        Assert.IsFalse((await _governanceEvents.ListForCorrelationAsync(new GovernanceEventsForCorrelationQuery { CorrelationId = otherCorrelationId, Take = 20 })).Any(row => row.ProjectId == projectId));
        Assert.IsNull(await _gateDecisions.GetAsync(TenantId, projectId, other.GateDecision.ToolGateDecisionId));
        Assert.IsNotNull(await _gateDecisions.GetAsync(TenantId, projectId, chain.GateDecision.ToolGateDecisionId));
    }

    [TestMethod]
    public async Task GovernanceSubstrate_RejectsCrossProjectEvidenceLinks()
    {
        var projectId = Guid.NewGuid();
        var other = await CreateFullEvidenceChainAsync(Guid.NewGuid(), Guid.NewGuid());

        await ExpectThrowsAsync<SqlException>(() => _gateDecisions.RecordAsync(ValidGateDecision(projectId, other.ToolRequest.ToolRequestId, Guid.NewGuid())));
        await ExpectThrowsAsync<SqlException>(() => _policyDecisions.RecordAsync(ValidPolicyDecision(projectId, Guid.NewGuid().ToString("D")) with { RelatedToolRequestId = other.ToolRequest.ToolRequestId }));
        await ExpectThrowsAsync<SqlException>(() => _policyDecisions.RecordAsync(ValidPolicyDecision(projectId, Guid.NewGuid().ToString("D")) with { RelatedToolGateDecisionId = other.GateDecision.ToolGateDecisionId }));
        await ExpectThrowsAsync<SqlException>(() => _policyDecisions.RecordAsync(ValidPolicyDecision(projectId, Guid.NewGuid().ToString("D")) with { RelatedApprovalDecisionId = other.ApprovalDecision.ApprovalDecisionId }));
        await ExpectThrowsAsync<SqlException>(() => _dogfoodReceipts.RecordAsync(ValidDogfoodReceipt(projectId, Guid.NewGuid(), Guid.NewGuid().ToString("D")) with { RelatedPolicyDecisionEventId = other.PolicyDecision.PolicyDecisionEventId }));
        await ExpectThrowsAsync<SqlException>(() => _thoughtLedgerReferences.RecordAsync(ValidThoughtLedgerReference(projectId, other.DogfoodReceipt.GovernanceEventId, Guid.NewGuid())));
    }

    [TestMethod]
    public async Task GovernanceSubstrate_AppendOnlyTablesRejectUpdateAndDelete()
    {
        var chain = await CreateFullEvidenceChainAsync(Guid.NewGuid(), Guid.NewGuid());

        await AssertUpdateDeleteBlockedAsync("governance.GovernanceEvent", "EventId", chain.ToolRequest.GovernanceEventId, "EventType = N'changed'");
        await AssertUpdateDeleteBlockedAsync("governance.ToolRequest", "ToolRequestId", chain.ToolRequest.ToolRequestId, "Status = N'Cancelled'");
        await AssertUpdateDeleteBlockedAsync("governance.ToolGateDecision", "ToolGateDecisionId", chain.GateDecision.ToolGateDecisionId, "ReasonCode = N'CHANGED'");
        await AssertUpdateDeleteBlockedAsync("governance.ApprovalDecision", "ApprovalDecisionId", chain.ApprovalDecision.ApprovalDecisionId, "ReasonCode = N'CHANGED'");
        await AssertUpdateDeleteBlockedAsync("governance.PolicyDecisionEvent", "PolicyDecisionEventId", chain.PolicyDecision.PolicyDecisionEventId, "ReasonCode = N'CHANGED'");
        await AssertUpdateDeleteBlockedAsync("governance.DogfoodReceipt", "DogfoodReceiptId", chain.DogfoodReceipt.DogfoodReceiptId, "SummaryCode = N'CHANGED'");
        await AssertUpdateDeleteBlockedAsync("governance.ThoughtLedgerGovernanceEventReference", "ThoughtLedgerGovernanceEventReferenceId", chain.ThoughtLedgerReference.ThoughtLedgerGovernanceEventReferenceId, "ReasonCode = N'CHANGED'");
    }

    [TestMethod]
    public async Task GovernanceSubstrate_AuthoritySeparation_NoLedgerRecordCreatesSideEffectsOrAuthority()
    {
        var chain = await CreateFullEvidenceChainAsync(Guid.NewGuid(), Guid.NewGuid());

        AssertPayloadFlagsFalse(await PayloadJsonAsync(chain.GateDecision.GovernanceEventId), "grantsApproval", "grantsExecution", "mutatesSource", "promotesMemory");
        AssertPayloadFlagsFalse(await PayloadJsonAsync(chain.ApprovalDecision.GovernanceEventId), "grantsExecution", "mutatesSource", "promotesMemory", "startsWorkflow");
        AssertPayloadFlagsFalse(await PayloadJsonAsync(chain.PolicyDecision.GovernanceEventId), "grantsApproval", "grantsExecution", "mutatesSource", "promotesMemory", "startsWorkflow", "satisfiesPolicy", "transfersAuthority");
        AssertPayloadFlagsFalse(await PayloadJsonAsync(chain.DogfoodReceipt.GovernanceEventId), "approvesRelease", "grantsApproval", "grantsExecution", "mutatesSource", "promotesMemory", "startsWorkflow", "satisfiesPolicy", "transfersAuthority");
        AssertPayloadFlagsFalse(chain.ThoughtLedgerReference.MetadataJson, "grantsApproval", "grantsExecution", "mutatesSource", "promotesMemory", "startsWorkflow", "satisfiesPolicy", "transfersAuthority");

        Assert.AreEqual(0, await CountRowsIfTableExistsAsync("governance.WorkflowStep"));
        Assert.AreEqual(0, await CountRowsIfTableExistsAsync("governance.A2aHandoff"));
        Assert.AreEqual(0, await CountRowsIfTableExistsAsync("governance.SourceApply"));
        Assert.AreEqual(0, await CountRowsIfTableExistsAsync("governance.MemoryPromotion"));
        Assert.AreEqual(0, await CountRowsIfTableExistsAsync("governance.ReleaseApproval"));
        Assert.AreEqual(0, await CountRowsIfTableExistsAsync("governance.ToolExecution"));
    }

    [TestMethod]
    public async Task GovernanceSubstrate_RejectsHiddenReasoningAcrossPayloadEvidenceAndMetadata()
    {
        var projectId = Guid.NewGuid();
        var chain = await CreateFullEvidenceChainAsync(projectId, Guid.NewGuid());

        await ExpectThrowsAsync<ArgumentException>(() => _toolRequests.CreateAsync(ValidToolRequest(projectId, Guid.NewGuid()) with { RequestPayloadJson = "{\"schemaVersion\":1,\"note\":\"chain-of-thought\"}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _gateDecisions.RecordAsync(ValidGateDecision(projectId, chain.ToolRequest.ToolRequestId, Guid.NewGuid()) with { EvidenceJson = "{\"schemaVersion\":1,\"note\":\"private reasoning\"}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _approvalDecisions.RecordAsync(ValidApprovalDecision(projectId, chain.ToolRequest.ToolRequestId, Guid.NewGuid()) with { EvidenceJson = "{\"schema\":\"approval.decision.evidence.v1\",\"rawPrompt\":\"secret\"}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _policyDecisions.RecordAsync(ValidPolicyDecision(projectId, chain.ToolRequest.ToolRequestId.ToString("D")) with { EvidenceJson = "{\"schema\":\"policy.decision.evidence.v1\",\"scratchpad\":\"secret\"}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _dogfoodReceipts.RecordAsync(ValidDogfoodReceipt(projectId, Guid.NewGuid(), "dogfood-hidden") with { EvidenceJson = "{\"schema\":\"dogfood.receipt.evidence.v1\",\"rawCompletion\":\"secret\"}" }));
        await ExpectThrowsAsync<ArgumentException>(() => _thoughtLedgerReferences.RecordAsync(ValidThoughtLedgerReference(projectId, chain.DogfoodReceipt.GovernanceEventId, Guid.NewGuid()) with { MetadataJson = "{\"schema\":\"thoughtledger.reference.v1\",\"rawPrompt\":\"secret\"}" }));
    }

    [TestMethod]
    public void GovernanceSubstrate_MigrationManifestVerifierAndSqlInventoryCoverAllBlockGLedgers()
    {
        var root = FindRepositoryRoot();
        var manifest = File.ReadAllText(Path.Combine(root, "Database", "migrations.json"));
        var inventory = File.ReadAllText(Path.Combine(root, "Database", "sql-inventory.json"));
        var verifier = File.ReadAllText(Path.Combine(root, "Database", "verify-migrations.ps1"));
        var doc = File.ReadAllText(Path.Combine(root, "Docs", "BLOCK_G_GOVERNANCE_SUBSTRATE_INVARIANTS.md"));

        foreach (var migration in GovernanceMigrationPaths)
            StringAssert.Contains(manifest, migration);

        foreach (var table in GovernanceTables)
        {
            StringAssert.Contains(inventory, table);
            StringAssert.Contains(verifier, table);
        }

        StringAssert.Contains(verifier, "governance.TR_GovernanceEvent_BlockUpdateDelete trigger");
        StringAssert.Contains(verifier, "governance.TR_ThoughtLedgerGovernanceEventReference_BlockUpdateDelete trigger");
        StringAssert.Contains(verifier, "FK_ToolGateDecision_ToolRequest");
        StringAssert.Contains(verifier, "FK_DogfoodReceipt_PolicyDecisionEvent");
        StringAssert.Contains(verifier, "CK_GovernanceEvent_PayloadJson_IsJson");
        StringAssert.Contains(verifier, "CK_ThoughtLedgerGovernanceEventReference_MetadataJson_IsJson");
        StringAssert.Contains(doc, "None of these records execute tools, approve release readiness, mutate source, promote memory, continue workflow, or transfer authority.");
    }

    [TestMethod]
    public void GovernanceSubstrate_Static_NoFutureMachineryOrHiddenConsumersAreWired()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));
        var cli = File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "IronDevCli.cs"));
        var apiControllerNames = Directory.GetFiles(Path.Combine(root, "IronDev.Api", "Controllers"), "*.cs").Select(path => Path.GetFileName(path) ?? string.Empty).ToArray();
        var infrastructure = Directory.GetFiles(Path.Combine(root, "IronDev.Infrastructure"), "*.cs", SearchOption.AllDirectories).Where(path => !path.Contains(Path.Combine("Governance"), StringComparison.OrdinalIgnoreCase)).Select(File.ReadAllText).ToArray();
        var coreAgents = Directory.GetFiles(Path.Combine(root, "IronDev.Core", "Agents"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText).ToArray();

        AssertNoForbiddenTokens(program, "IWorkflowRunner", "LangGraph", "IA2a", "A2aHandoff", "MemoryPromotionService", "SourceApplyService", "ReleaseApprovalService");
        AssertNoForbiddenTokens(cli, "approval-decisions", "policy-decisions", "source-apply", "memory-promotion", "release-approval", "workflow-run");
        Assert.IsFalse(apiControllerNames.Any(name => name.Contains("ApprovalDecision", StringComparison.OrdinalIgnoreCase) || name.Contains("PolicyDecision", StringComparison.OrdinalIgnoreCase) || name.Contains("SourceApply", StringComparison.OrdinalIgnoreCase) || name.Contains("MemoryPromotion", StringComparison.OrdinalIgnoreCase) || name.Contains("ReleaseApproval", StringComparison.OrdinalIgnoreCase)));
        Assert.IsFalse(infrastructure.Any(text => text.Contains("IApprovalDecisionStore", StringComparison.Ordinal) || text.Contains("IPolicyDecisionEventStore", StringComparison.Ordinal)), "Non-governance infrastructure must not consume approval/policy ledgers as hidden authority.");
        Assert.IsFalse(coreAgents.Any(text => text.Contains("IApprovalDecisionStore", StringComparison.Ordinal) || text.Contains("IPolicyDecisionEventStore", StringComparison.Ordinal) || text.Contains("IDogfoodReceiptStore", StringComparison.Ordinal)), "Agents must not consume governance ledgers as hidden authority.");
    }

    [TestMethod]
    public void GovernanceSubstrate_ApiCliWording_UsesEvidenceAndBoundaryLanguageOnly()
    {
        var root = FindRepositoryRoot();
        var toolGateController = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "ToolGatesV1Controller.cs"));
        var dogfoodController = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Controllers", "DogfoodLoopsV1Controller.cs"));
        var contractMatrix = File.ReadAllText(Path.Combine(root, "Docs", "API_CLI_CONTRACT_MATRIX.md"));

        StringAssert.Contains(toolGateController, "ToolExecuted = false");
        StringAssert.Contains(toolGateController, "RequestApproved = false");
        StringAssert.Contains(dogfoodController, "DogfoodReceiptIsReleaseApproval = false");
        StringAssert.Contains(dogfoodController, "ToolExecuted = false");
        StringAssert.Contains(dogfoodController, "RequestApproved = false");
        StringAssert.Contains(dogfoodController, "GateExecuted = false");
        StringAssert.Contains(contractMatrix, "Dogfood receipt is evidence, not release approval.");
        StringAssert.Contains(contractMatrix, "Gate evaluation is not execution.");

        AssertNoForbiddenTokens(toolGateController, "CanExecute = true", "ExecutionAllowed = true", "AuthorizationGranted = true", "PermissionGranted = true");
        AssertNoForbiddenTokens(dogfoodController, "ReleaseReady = true", "CanShip = true", "ReleaseApproved = true", "PolicySatisfied = true", "DogfoodReceiptIsReleaseApproval = true");
    }

    private async Task<GovernanceSubstrateChain> CreateFullEvidenceChainAsync(Guid projectId, Guid correlationId)
    {
        var toolRequest = await _toolRequests.CreateAsync(ValidToolRequest(projectId, correlationId));
        var gate = await _gateDecisions.RecordAsync(ValidGateDecision(projectId, toolRequest.ToolRequestId, correlationId) with { CausationId = toolRequest.GovernanceEventId });
        var approval = await _approvalDecisions.RecordAsync(ValidApprovalDecision(projectId, toolRequest.ToolRequestId, correlationId) with { CausationId = gate.GovernanceEventId });
        var policy = await _policyDecisions.RecordAsync(ValidPolicyDecision(projectId, toolRequest.ToolRequestId.ToString("D")) with
        {
            RelatedToolRequestId = toolRequest.ToolRequestId,
            RelatedToolGateDecisionId = gate.ToolGateDecisionId,
            RelatedApprovalDecisionId = approval.ApprovalDecisionId,
            CorrelationId = correlationId,
            CausationId = approval.GovernanceEventId
        });
        var receipt = await _dogfoodReceipts.RecordAsync(ValidDogfoodReceipt(projectId, correlationId, "dogfood-loop-" + toolRequest.ToolRequestId.ToString("N")) with
        {
            RelatedToolRequestId = toolRequest.ToolRequestId,
            RelatedToolGateDecisionId = gate.ToolGateDecisionId,
            RelatedApprovalDecisionId = approval.ApprovalDecisionId,
            RelatedPolicyDecisionEventId = policy.PolicyDecisionEventId,
            CausationId = policy.GovernanceEventId
        });
        var reference = await _thoughtLedgerReferences.RecordAsync(ValidThoughtLedgerReference(projectId, receipt.GovernanceEventId, correlationId) with { CausationId = receipt.GovernanceEventId });

        return new(toolRequest, gate, approval, policy, receipt, reference);
    }

    private static ToolRequestCreateRequest ValidToolRequest(Guid projectId, Guid correlationId) => new()
    {
        ProjectId = projectId,
        ToolName = "workspace.diff",
        OperationName = "inspect",
        RequestedByActorType = "agent",
        RequestedByActorId = "governance-substrate-tests",
        CorrelationId = correlationId,
        Purpose = "Record Block G request evidence only.",
        RequestPayloadVersion = 1,
        RequestPayloadJson = "{\"schema\":\"tool.request.evidence.v1\",\"schemaVersion\":1,\"purpose\":\"contract test\",\"evidenceRefs\":[\"trace:substrate\"]}"
    };

    private static ToolGateDecisionRecordRequest ValidGateDecision(Guid projectId, Guid toolRequestId, Guid correlationId) => new(
        TenantId,
        projectId,
        toolRequestId,
        nameof(ToolGateDecisionValue.RequiresApproval),
        "tool-request-gate",
        1,
        "system",
        "governance-substrate-tests",
        "HUMAN_APPROVAL_REQUIRED",
        JsonSerializer.Serialize(new { schemaVersion = 1, gate = "tool-request-gate", grantsApproval = false, grantsExecution = false, mutatesSource = false, promotesMemory = false }))
    {
        CorrelationId = correlationId
    };

    private static ApprovalDecisionRecordRequest ValidApprovalDecision(Guid projectId, Guid toolRequestId, Guid correlationId) => new()
    {
        ProjectId = projectId,
        ApprovalScope = ApprovalDecisionScopes.ToolExecution,
        SubjectType = "tool_request",
        SubjectId = toolRequestId.ToString("D"),
        Decision = nameof(ApprovalDecisionValue.Approved),
        ReasonCode = "HUMAN_REVIEWED",
        Reason = "Human reviewed explicit evidence only.",
        DecidedByActorType = "human",
        DecidedByActorId = "human-reviewer",
        CorrelationId = correlationId,
        EvidenceVersion = 1,
        EvidenceJson = JsonSerializer.Serialize(new { schema = "approval.decision.evidence.v1", schemaVersion = 1, grantsExecution = false, mutatesSource = false, promotesMemory = false, startsWorkflow = false })
    };

    private static PolicyDecisionRecordRequest ValidPolicyDecision(Guid projectId, string subjectId) => new()
    {
        ProjectId = projectId,
        PolicyScope = PolicyDecisionScopes.ToolExecution,
        PolicyName = "tool-execution-policy",
        PolicyVersion = 1,
        SubjectType = "tool_request",
        SubjectId = subjectId,
        Decision = nameof(PolicyDecisionValue.NoPolicyBlock),
        RequirementCode = "NO_POLICY_BLOCK",
        ReasonCode = "POLICY_CHECK_RECORDED",
        Reason = "Policy check evidence only; no permission is granted.",
        DecidedByActorType = "system",
        DecidedByActorId = "governance-substrate-tests",
        EvidenceVersion = 1,
        EvidenceJson = JsonSerializer.Serialize(new { schema = "policy.decision.evidence.v1", schemaVersion = 1, grantsApproval = false, grantsExecution = false, mutatesSource = false, promotesMemory = false, startsWorkflow = false, satisfiesPolicy = false, transfersAuthority = false })
    };

    private static DogfoodReceiptRecordRequest ValidDogfoodReceipt(Guid projectId, Guid correlationId, string subjectId) => new()
    {
        ProjectId = projectId,
        ReceiptType = "dogfood_loop",
        SubjectType = "dogfood_loop",
        SubjectId = subjectId,
        Outcome = nameof(DogfoodReceiptOutcome.Passed),
        SummaryCode = "DOGFOOD_LOOP_RECORDED",
        Summary = "Dogfood receipt evidence for human review only.",
        RecordedByActorType = "system_test_fixture",
        RecordedByActorId = "governance-substrate-tests",
        CorrelationId = correlationId,
        EvidenceVersion = 1,
        EvidenceJson = "{\"schema\":\"dogfood.receipt.evidence.v1\",\"schemaVersion\":1,\"approvesRelease\":false,\"grantsApproval\":false,\"grantsExecution\":false,\"satisfiesPolicy\":false,\"mutatesSource\":false,\"promotesMemory\":false,\"startsWorkflow\":false,\"transfersAuthority\":false}"
    };

    private static ThoughtLedgerGovernanceEventReferenceRecordRequest ValidThoughtLedgerReference(Guid projectId, Guid governanceEventId, Guid correlationId) => new()
    {
        ProjectId = projectId,
        ThoughtLedgerEntryId = "thought-ledger-entry-" + governanceEventId.ToString("N"),
        GovernanceEventId = governanceEventId,
        ReferenceType = nameof(ThoughtLedgerGovernanceReferenceType.Observed),
        ReasonCode = "LEDGER_CITES_EVENT",
        Reason = "Visible ThoughtLedger entry cites durable governance evidence only.",
        CorrelationId = correlationId,
        CreatedByActorType = "system_test_fixture",
        CreatedByActorId = "governance-substrate-tests",
        MetadataVersion = 1,
        MetadataJson = "{\"schema\":\"thoughtledger.governance_event_reference.v1\",\"schemaVersion\":1,\"grantsApproval\":false,\"grantsExecution\":false,\"mutatesSource\":false,\"promotesMemory\":false,\"startsWorkflow\":false,\"satisfiesPolicy\":false,\"transfersAuthority\":false}"
    };

    private async Task<IReadOnlyList<GovernanceEventRow>> GovernanceRowsForCorrelationAsync(Guid projectId, Guid correlationId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var rows = await connection.QueryAsync<GovernanceEventRow>(
            """
            SELECT EventId, ProjectId, EventType, CorrelationId, CausationId, PayloadJson
            FROM governance.GovernanceEvent
            WHERE ProjectId = @ProjectId AND CorrelationId = @CorrelationId
            ORDER BY CreatedUtc, EventId
            """,
            new { ProjectId = projectId, CorrelationId = correlationId });
        return rows.ToArray();
    }

    private async Task<string> PayloadJsonAsync(Guid eventId)
    {
        await using var connection = new SqlConnection(ConnectionString);
        return await connection.ExecuteScalarAsync<string>("SELECT PayloadJson FROM governance.GovernanceEvent WHERE EventId = @EventId", new { EventId = eventId })
            ?? throw new InvalidOperationException("Governance event payload was not found.");
    }

    private async Task<int> CountRowsIfTableExistsAsync(string tableName)
    {
        await using var connection = new SqlConnection(ConnectionString);
        var sql = $"IF OBJECT_ID(N'{tableName.Replace("'", "''", StringComparison.Ordinal)}', N'U') IS NULL SELECT 0 ELSE SELECT COUNT(1) FROM {tableName}";
        return await connection.ExecuteScalarAsync<int>(sql);
    }

    private async Task AssertUpdateDeleteBlockedAsync(string tableName, string keyColumn, Guid keyValue, string setClause)
    {
        await ExpectThrowsAsync<SqlException>(() => ExecuteSqlAsync($"UPDATE {tableName} SET {setClause} WHERE {keyColumn} = @Id", new { Id = keyValue }));
        await ExpectThrowsAsync<SqlException>(() => ExecuteSqlAsync($"DELETE FROM {tableName} WHERE {keyColumn} = @Id", new { Id = keyValue }));
    }

    private async Task ExecuteSqlAsync(string sql, object? parameters = null)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(sql, parameters);
    }

    private static void AssertCausation(IReadOnlyList<GovernanceEventRow> events, Guid eventId, Guid expectedCausationId)
    {
        var row = events.Single(item => item.EventId == eventId);
        Assert.AreEqual(expectedCausationId, row.CausationId);
    }

    private static void AssertPayloadFlagsFalse(string json, params string[] flagNames)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var flagName in flagNames)
        {
            Assert.IsTrue(document.RootElement.TryGetProperty(flagName, out var value), $"Payload is missing flag {flagName}: {json}");
            Assert.AreEqual(JsonValueKind.False, value.ValueKind, $"Flag {flagName} must be false: {json}");
        }
    }

    private static void AssertNoProperty<T>(string propertyName)
    {
        Assert.IsFalse(typeof(T).GetProperties().Any(property => property.Name.Equals(propertyName, StringComparison.Ordinal)), $"{typeof(T).Name} must not expose {propertyName}.");
    }

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        Assert.Fail($"Expected {typeof(TException).Name}.");
    }

    private async Task ApplyGovernanceMigrationsAsync()
    {
        foreach (var migration in GovernanceMigrationPaths)
            await ApplySqlFileAsync(migration);
    }

    private async Task ApplySqlFileAsync(string relativePath)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(batch))
                continue;

            await connection.ExecuteAsync(batch);
        }
    }

    private async Task DropGovernanceSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF SCHEMA_ID(N'governance') IS NULL RETURN;

            DECLARE @sql NVARCHAR(MAX) = N'';
            SELECT @sql = @sql + N'DROP PROCEDURE governance.' + QUOTENAME(name) + N';' + CHAR(13)
            FROM sys.procedures
            WHERE schema_id = SCHEMA_ID(N'governance');
            EXEC sp_executesql @sql;

            IF OBJECT_ID(N'governance.ThoughtLedgerGovernanceEventReference', N'U') IS NOT NULL DROP TABLE governance.ThoughtLedgerGovernanceEventReference;
            IF OBJECT_ID(N'governance.DogfoodReceipt', N'U') IS NOT NULL DROP TABLE governance.DogfoodReceipt;
            IF OBJECT_ID(N'governance.PolicyDecisionEvent', N'U') IS NOT NULL DROP TABLE governance.PolicyDecisionEvent;
            IF OBJECT_ID(N'governance.ApprovalDecision', N'U') IS NOT NULL DROP TABLE governance.ApprovalDecision;
            IF OBJECT_ID(N'governance.ToolGateDecision', N'U') IS NOT NULL DROP TABLE governance.ToolGateDecision;
            IF OBJECT_ID(N'governance.ToolRequest', N'U') IS NOT NULL DROP TABLE governance.ToolRequest;
            IF OBJECT_ID(N'governance.GovernanceEvent', N'U') IS NOT NULL DROP TABLE governance.GovernanceEvent;

            IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IronDevGovernanceEventRuntimeRole' AND type = N'R')
                DROP ROLE IronDevGovernanceEventRuntimeRole;
            IF SCHEMA_ID(N'governance') IS NOT NULL
                              IF OBJECT_ID(N'governance.usp_AcceptedApproval_Save', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_Save;
              IF OBJECT_ID(N'governance.usp_AcceptedApproval_Get', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_Get;
              IF OBJECT_ID(N'governance.usp_AcceptedApproval_ListByTarget', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_ListByTarget;
              IF OBJECT_ID(N'governance.usp_AcceptedApproval_ListByCorrelation', N'P') IS NOT NULL DROP PROCEDURE governance.usp_AcceptedApproval_ListByCorrelation;
              IF OBJECT_ID(N'governance.TR_AcceptedApproval_ValidateInsert', N'TR') IS NOT NULL DROP TRIGGER governance.TR_AcceptedApproval_ValidateInsert;
              IF OBJECT_ID(N'governance.TR_AcceptedApproval_BlockUpdateDelete', N'TR') IS NOT NULL DROP TRIGGER governance.TR_AcceptedApproval_BlockUpdateDelete;
              IF OBJECT_ID(N'governance.AcceptedApproval', N'U') IS NOT NULL DROP TABLE governance.AcceptedApproval;                EXEC(N'DROP SCHEMA governance');
            """);
    }

    private static void AssertNoForbiddenTokens(string text, params string[] tokens)
    {
        foreach (var token in tokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Unexpected token: {token}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not find repository root.");
    }

    private static readonly string[] GovernanceMigrationPaths =
    [
        "Database/migrate_governance_event.sql",
        "Database/migrate_tool_request.sql",
        "Database/migrate_tool_gate_decision.sql",
        "Database/migrate_approval_decision.sql",
        "Database/migrate_policy_decision_event.sql",
        "Database/migrate_dogfood_receipt.sql",
        "Database/migrate_thoughtledger_governance_event_reference.sql"
    ];

    private static readonly string[] GovernanceTables =
    [
        "governance.GovernanceEvent",
        "governance.ToolRequest",
        "governance.ToolGateDecision",
        "governance.ApprovalDecision",
        "governance.PolicyDecisionEvent",
        "governance.DogfoodReceipt",
        "governance.ThoughtLedgerGovernanceEventReference"
    ];

    private sealed record GovernanceSubstrateChain(
        ToolRequestReadModel ToolRequest,
        ToolGateDecisionReadModel GateDecision,
        ApprovalDecisionReadModel ApprovalDecision,
        PolicyDecisionReadModel PolicyDecision,
        DogfoodReceiptReadModel DogfoodReceipt,
        ThoughtLedgerGovernanceEventReference ThoughtLedgerReference);

    private sealed record GovernanceEventRow(Guid EventId, Guid ProjectId, string EventType, Guid? CorrelationId, Guid? CausationId, string PayloadJson);
}
