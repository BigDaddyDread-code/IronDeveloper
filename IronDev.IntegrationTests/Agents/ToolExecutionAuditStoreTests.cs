using System.Data;
using Dapper;
using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using IronDev.Data;
using IronDev.Infrastructure.ToolExecutionAudit;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Agents;

[TestClass]
public sealed class ToolExecutionAuditStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset RequestedAt = new(2026, 6, 11, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GateEvaluatedAt = RequestedAt.AddSeconds(1);
    private static readonly DateTimeOffset CreatedAt = RequestedAt.AddSeconds(2);

    private SqlToolExecutionAuditStore _store = default!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropToolExecutionAuditSchemaAsync();
        await ApplyToolExecutionAuditMigrationAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _store = new SqlToolExecutionAuditStore(connectionFactory);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        await DropToolExecutionAuditSchemaAsync();
        await base.TestCleanup();
    }

    [TestMethod]
    public void ToolExecutionAuditContracts_ExposeAppendOnlyStoreShape()
    {
        Assert.IsNotNull(typeof(ToolExecutionAuditRecord));
        Assert.IsNotNull(typeof(ToolExecutionAuditPayload));
        Assert.IsNotNull(typeof(ToolExecutionAuditScope));
        Assert.IsNotNull(typeof(ToolExecutionAuditActor));
        Assert.IsNotNull(typeof(ToolExecutionAuditTool));
        Assert.IsNotNull(typeof(ToolExecutionAuditGate));
        Assert.IsNotNull(typeof(ToolExecutionAuditOutcome));
        Assert.IsNotNull(typeof(ToolExecutionAuditEvidence));
        Assert.IsNotNull(typeof(ToolExecutionAuditValidator));
        Assert.IsNotNull(typeof(ToolExecutionAuditRecordFactory));
        Assert.IsNotNull(typeof(IToolExecutionAuditStore));

        CollectionAssert.AreEquivalent(
            new[]
            {
                ToolExecutionAuditAppendStatus.Appended,
                ToolExecutionAuditAppendStatus.AlreadyExists,
                ToolExecutionAuditAppendStatus.Conflict,
                ToolExecutionAuditAppendStatus.Rejected
            },
            Enum.GetValues<ToolExecutionAuditAppendStatus>());

        var methods = typeof(IToolExecutionAuditStore).GetMethods().Select(method => method.Name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "AppendAsync", "GetAsync", "ListByRunAsync" },
            methods);
    }

    [TestMethod]
    public void ToolExecutionAuditFactory_BuildsTesterSuccessAndFailureRecords()
    {
        var succeeded = ToolExecutionAuditRecordFactory.FromManualTesterResult(SuccessfulTesterResult(), CreatedAt);
        var failed = ToolExecutionAuditRecordFactory.FromManualTesterResult(FailedTesterResult(), CreatedAt);

        Assert.AreEqual(AgentToolKind.TestRun, succeeded.ToolKind);
        Assert.AreEqual(AgentToolRequestType.TestExecutionRequest, succeeded.RequestType);
        Assert.AreEqual(AgentKind.TestingAgent, succeeded.AgentKind);
        Assert.AreEqual(ToolExecutionAuditRecordFactory.ManualTesterPayloadKind, succeeded.PayloadKind);
        Assert.IsTrue(succeeded.Succeeded);
        Assert.IsTrue(failed.Status == ManualTesterAgentToolExecutionStatus.Failed.ToString());
        Assert.IsFalse(failed.Succeeded);
        AssertNoUnsafeFlags(succeeded);
        AssertNoUnsafeFlags(failed);
        AssertNoValidationIssues(succeeded);
        AssertNoValidationIssues(failed);
    }

    [TestMethod]
    public void ToolExecutionAuditFactory_BuildsImplementationPatchProposalRecord()
    {
        var record = ToolExecutionAuditRecordFactory.FromManualImplementationPatchProposalResult(SuccessfulImplementationResult(), CreatedAt);

        Assert.AreEqual(AgentToolKind.PatchProposal, record.ToolKind);
        Assert.AreEqual(AgentToolRequestType.PatchProposalRequest, record.RequestType);
        Assert.AreEqual(AgentKind.ImplementationAgent, record.AgentKind);
        Assert.AreEqual(ToolExecutionAuditRecordFactory.ManualImplementationPatchProposalPayloadKind, record.PayloadKind);
        Assert.IsTrue(record.Succeeded);
        AssertNoUnsafeFlags(record);
        AssertNoValidationIssues(record);
    }

    [TestMethod]
    public void ToolExecutionAuditFactory_RejectsIncompleteOrUnsafeManualResults()
    {
        var blockedTester = new ManualTesterAgentToolExecutionService(SuccessExecutor())
            .Execute(ValidTesterRequest() with
            {
                GateDecision = AllowedGate(ValidTesterToolRequest()) with
                {
                    Decision = AgentToolExecutionGateDecisionType.Blocked,
                    GrantsExecution = false
                }
            });

        var failedImplementation = new ManualImplementationAgentPatchProposalService(new ScriptedPatchProposalGenerator(_ => new PatchProposalGenerationResult
        {
            Succeeded = false,
            Summary = "Generator failed safely.",
            Issues =
            [
                new ManualImplementationPatchProposalIssue
                {
                    Code = ManualImplementationPatchProposalValidator.OutputUnsafe,
                    Severity = "error",
                    Message = "No proposal was produced."
                }
            ]
        })).Propose(ValidImplementationRequest());

        ExpectArgumentException(() => ToolExecutionAuditRecordFactory.FromManualTesterResult(blockedTester, CreatedAt));
        ExpectArgumentException(() => ToolExecutionAuditRecordFactory.FromManualImplementationPatchProposalResult(failedImplementation, CreatedAt));
    }

    [TestMethod]
    public void ToolExecutionAuditValidator_RejectsMissingFieldsUnsupportedShapesUnsafeFlagsAndText()
    {
        var valid = ToolExecutionAuditRecordFactory.FromManualTesterResult(SuccessfulTesterResult(), CreatedAt);
        var cases = new Dictionary<string, ToolExecutionAuditRecord>
        {
            [ToolExecutionAuditValidator.ToolAuditIdRequired] = valid with { ToolExecutionAuditId = "" },
            [ToolExecutionAuditValidator.ToolAuditScopeRequired] = valid with { TenantId = "" },
            [ToolExecutionAuditValidator.ToolAuditAgentRequired] = valid with { AgentId = "" },
            [ToolExecutionAuditValidator.ToolAuditToolRequestRequired] = valid with { ToolRequestId = "" },
            [ToolExecutionAuditValidator.ToolAuditGateRequired] = valid with { GateDecisionId = "" },
            [ToolExecutionAuditValidator.ToolAuditPayloadRequired] = valid with { PayloadJson = "" },
            [ToolExecutionAuditValidator.ToolAuditPayloadKindInvalid] = valid with { PayloadKind = "RawToolOutput" },
            [ToolExecutionAuditValidator.ToolAuditToolKindInvalid] = valid with { ToolKind = AgentToolKind.SourceApply },
            [ToolExecutionAuditValidator.ToolAuditRequestTypeInvalid] = valid with { RequestType = AgentToolRequestType.SourceMutationRequest },
            [ToolExecutionAuditValidator.ToolAuditToolRequestMismatch] = valid with { AgentKind = AgentKind.ImplementationAgent },
            [ToolExecutionAuditValidator.ToolAuditHashRequired] = valid with { PayloadSha256 = "" },
            [ToolExecutionAuditValidator.ToolAuditHashInvalid] = valid with { PayloadSha256 = new string('a', 64) },
            [ToolExecutionAuditValidator.ToolAuditEvidenceRequired] = valid with { EvidenceRefs = [] },
            [ToolExecutionAuditValidator.ToolAuditRawReasoningBlocked] = valid with { ContainsRawPrivateReasoning = true },
            [ToolExecutionAuditValidator.ToolAuditSecretBlocked] = valid with { ContainsSecret = true },
            [ToolExecutionAuditValidator.ToolAuditApprovalClaimBlocked] = valid with { ClaimsApproval = true },
            [ToolExecutionAuditValidator.ToolAuditMemoryPromotionClaimBlocked] = valid with { ClaimsMemoryPromotion = true },
            [ToolExecutionAuditValidator.ToolAuditUnsafeEffectBlocked] = valid with { MutatesSource = true },
            [ToolExecutionAuditValidator.ToolAuditPayloadTextUnsafe] = RehashPayload(valid with { PayloadJson = "{\"summary\":\"raw private reasoning: unsafe\"}" }),
            [ToolExecutionAuditValidator.ToolAuditEnvelopeTextUnsafe] = RehashEnvelope(valid with { AuditEnvelopeJson = "{\"summary\":\"grant authority\"}" })
        };

        var validator = new ToolExecutionAuditValidator();
        foreach (var pair in cases)
        {
            var issues = validator.Validate(pair.Value);
            AssertHasIssue(issues, pair.Key);
        }
    }

    [TestMethod]
    public async Task ToolExecutionAuditStore_AppendsReadsAndListsTesterAndImplementationRecords()
    {
        var tester = ToolExecutionAuditRecordFactory.FromManualTesterResult(SuccessfulTesterResult(), CreatedAt);
        var implementation = ToolExecutionAuditRecordFactory.FromManualImplementationPatchProposalResult(SuccessfulImplementationResult(), CreatedAt.AddSeconds(1));

        var testerAppend = await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = tester });
        var implementationAppend = await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = implementation });

        Assert.AreEqual(ToolExecutionAuditAppendStatus.Appended, testerAppend.Status);
        Assert.AreEqual(ToolExecutionAuditAppendStatus.Appended, implementationAppend.Status);

        var read = await _store.GetAsync(new ToolExecutionAuditQuery
        {
            TenantId = tester.TenantId,
            ProjectId = tester.ProjectId,
            ToolExecutionAuditId = tester.ToolExecutionAuditId
        });
        Assert.IsTrue(read.Found);
        Assert.AreEqual(tester.PayloadSha256, read.Record!.PayloadSha256);

        var listed = await _store.ListByRunAsync(new ToolExecutionAuditRunQuery
        {
            TenantId = tester.TenantId,
            ProjectId = tester.ProjectId,
            RunId = tester.RunId!
        });
        Assert.AreEqual(2, listed.Count);
        Assert.IsTrue(listed.Any(record => record.PayloadKind == ToolExecutionAuditRecordFactory.ManualTesterPayloadKind));
        Assert.IsTrue(listed.Any(record => record.PayloadKind == ToolExecutionAuditRecordFactory.ManualImplementationPatchProposalPayloadKind));
    }

    [TestMethod]
    public async Task ToolExecutionAuditStore_IsIdempotentForSameHashAndConflictsForDifferentHash()
    {
        var record = ToolExecutionAuditRecordFactory.FromManualTesterResult(SuccessfulTesterResult(), CreatedAt);

        var first = await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = record });
        var second = await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = record });
        var changed = RehashPayload(record with { PayloadJson = record.PayloadJson.Replace("Scripted test-plan executor", "Changed scripted test-plan executor", StringComparison.Ordinal) });
        var conflict = await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = changed });

        Assert.AreEqual(ToolExecutionAuditAppendStatus.Appended, first.Status);
        Assert.AreEqual(ToolExecutionAuditAppendStatus.AlreadyExists, second.Status);
        Assert.AreEqual(ToolExecutionAuditAppendStatus.Conflict, conflict.Status);
        AssertHasIssue(conflict.Issues, ToolExecutionAuditValidator.ToolAuditStoreConflict);
    }

    [TestMethod]
    public async Task ToolExecutionAuditStore_IsScopedByTenantProjectAndRun()
    {
        var record = ToolExecutionAuditRecordFactory.FromManualTesterResult(SuccessfulTesterResult(), CreatedAt);
        await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = record });

        var wrongProject = await _store.GetAsync(new ToolExecutionAuditQuery
        {
            TenantId = record.TenantId,
            ProjectId = "other-project",
            ToolExecutionAuditId = record.ToolExecutionAuditId
        });

        var wrongRun = await _store.ListByRunAsync(new ToolExecutionAuditRunQuery
        {
            TenantId = record.TenantId,
            ProjectId = record.ProjectId,
            RunId = "other-run"
        });

        Assert.IsFalse(wrongProject.Found);
        Assert.AreEqual(0, wrongRun.Count);
    }

    [TestMethod]
    public async Task ToolExecutionAuditSqlBoundary_DirectUnsafeFlagsAndUpdatesAreBlocked()
    {
        var record = ToolExecutionAuditRecordFactory.FromManualTesterResult(SuccessfulTesterResult(), CreatedAt);
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { ContainsRawPrivateReasoning = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { ClaimsApproval = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { ClaimsPolicyApproval = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { ClaimsHumanApproval = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { ClaimsMemoryPromotion = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { ExecutesTool = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { MutatesSource = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { WritesFiles = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { RunsGit = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { CallsExternalSystem = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { SubmitsGitHubReview = true }));
        await ExpectSqlFailsAsync(() => DirectInsertAsync(record with { WritesWeaviate = true }));

        await _store.AppendAsync(new ToolExecutionAuditAppendRequest { Record = record });
        await ExpectSqlFailsAsync(async () =>
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.ExecuteAsync(
                "UPDATE toolaudit.ToolExecutionAuditRecord SET Status = N'Tampered' WHERE ToolExecutionAuditId = @ToolExecutionAuditId",
                new { record.ToolExecutionAuditId });
        });
        await ExpectSqlFailsAsync(async () =>
        {
            await using var connection = new SqlConnection(ConnectionString);
            await connection.ExecuteAsync(
                "DELETE FROM toolaudit.ToolExecutionAuditRecord WHERE ToolExecutionAuditId = @ToolExecutionAuditId",
                new { record.ToolExecutionAuditId });
        });
    }

    [TestMethod]
    public void ToolExecutionAuditStore_StaticBoundary_UsesStoredProceduresAndNoExecutorRuntimeWiring()
    {
        var root = FindRepositoryRoot();
        var storeText = File.ReadAllText(Path.Combine(root, "IronDev.Infrastructure", "ToolExecutionAudit", "SqlToolExecutionAuditStore.cs"));
        var migrationText = File.ReadAllText(Path.Combine(root, "Database", "migrate_tool_execution_audit.sql"));
        var coreText = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Agents", "ToolExecutionAuditModels.cs"));
        var apiProgramText = File.ReadAllText(Path.Combine(root, "IronDev.Api", "Program.cs"));
        var cliText = File.Exists(Path.Combine(root, "tools", "IronDev.Cli", "IronDevCli.cs"))
            ? File.ReadAllText(Path.Combine(root, "tools", "IronDev.Cli", "IronDevCli.cs"))
            : string.Empty;

        StringAssert.Contains(storeText, "CommandType.StoredProcedure");
        AssertNoForbiddenTokens(storeText, "INSERT INTO toolaudit.ToolExecutionAuditRecord", "UPDATE toolaudit.ToolExecutionAuditRecord", "DELETE FROM toolaudit.ToolExecutionAuditRecord", "MERGE toolaudit.ToolExecutionAuditRecord", "ProcessStartInfo", "File.Copy", "File.Delete");
        AssertNoForbiddenTokens(coreText, "SqlConnection", "Dapper", "ProcessStartInfo", "File.Copy", "File.Delete", "HttpClient");
        AssertNoForbiddenTokens(apiProgramText, "SqlToolExecutionAuditStore", "IToolExecutionAuditStore");
        AssertNoForbiddenTokens(cliText, "SqlToolExecutionAuditStore", "IToolExecutionAuditStore");
        AssertNoForbiddenTokens(migrationText, "CREATE TABLE dbo.ToolExecutionAudit");
        StringAssert.Contains(migrationText, "CREATE OR ALTER PROCEDURE toolaudit.AppendToolExecutionAuditRecord");
        StringAssert.Contains(migrationText, "DENY INSERT, UPDATE, DELETE ON OBJECT::toolaudit.ToolExecutionAuditRecord");
    }

    private static ManualTesterAgentToolExecutionResult SuccessfulTesterResult() =>
        new ManualTesterAgentToolExecutionService(SuccessExecutor()).Execute(ValidTesterRequest());

    private static ManualTesterAgentToolExecutionResult FailedTesterResult()
    {
        var executor = new ScriptedTestRunPlanExecutor(request => new TestRunPlanExecutionResult
        {
            Succeeded = false,
            ExecutionId = request.ExecutionId,
            ExitCode = 1,
            Summary = "Scripted test-plan executor found failures.",
            Outcome = "failed",
            TestsPassed = 7,
            TestsFailed = 2,
            Duration = TimeSpan.FromSeconds(3),
            EvidenceRefs = ["test-failure-1", request.TestPlanRef]
        });

        return new ManualTesterAgentToolExecutionService(executor).Execute(ValidTesterRequest() with { ManualExecutionId = "manual-test-execution-failed" });
    }

    private static ManualImplementationPatchProposalResult SuccessfulImplementationResult() =>
        new ManualImplementationAgentPatchProposalService(SuccessGenerator()).Propose(ValidImplementationRequest());

    private static ManualTesterAgentToolExecutionRequest ValidTesterRequest()
    {
        var toolRequest = ValidTesterToolRequest();
        return new ManualTesterAgentToolExecutionRequest
        {
            ManualExecutionId = "manual-test-execution-1",
            ToolRequest = toolRequest,
            GateDecision = AllowedGate(toolRequest),
            RequestedByUserId = "user-1",
            TestPlanRef = "test-plan-1",
            TestPlanPath = "tools/dogfood/test-agent-plans/irondev-code-standards-alpha.json",
            WorkingDirectory = "workspace/run-1",
            Parameters = new Dictionary<string, string> { ["filter"] = "IronDevCliTests" },
            RequestedAtUtc = RequestedAt
        };
    }

    private static AgentToolRequest ValidTesterToolRequest() =>
        new()
        {
            ToolRequestId = "tool-request-test-run-1",
            Status = AgentToolRequestStatus.PendingGate,
            RequestType = AgentToolRequestType.TestExecutionRequest,
            ToolKind = AgentToolKind.TestRun,
            RiskLevel = AgentToolRiskLevel.Medium,
            Scope = new AgentToolRequestScope
            {
                TenantId = "tenant-1",
                ProjectId = "project-1",
                CampaignId = "campaign-1",
                RunId = "run-1",
                AgentRunId = "agent-run-1",
                CorrelationId = "correlation-1"
            },
            Actor = ValidActor(AgentDefinitionCatalog.TestingAgent),
            Purpose = "Run the controlled scripted test plan.",
            Inputs =
            [
                new AgentToolRequestInput
                {
                    InputId = "input-test-plan-1",
                    RefType = "TestPlanRef",
                    RefId = "test-plan-1",
                    Source = "test",
                    Summary = "Sanitised test plan reference.",
                    EvidenceRefs = ["evidence-test-plan-1"],
                    IsSanitised = true
                }
            ],
            Evidence =
            [
                new AgentToolRequestEvidence
                {
                    EvidenceId = "evidence-test-plan-1",
                    RefType = "RunReport",
                    RefId = "run-report-1",
                    Summary = "Evidence supports requesting this test run.",
                    SupportsNeedForTool = true
                }
            ],
            ApprovalRequirement = new AgentToolRequestApprovalRequirement
            {
                RequiresGovernanceGate = true,
                Reason = "Test execution requires gate metadata."
            },
            RequestedAtUtc = RequestedAt
        };

    private static ManualImplementationPatchProposalRequest ValidImplementationRequest()
    {
        var toolRequest = ValidImplementationToolRequest();
        return new ManualImplementationPatchProposalRequest
        {
            ManualProposalId = "manual-implementation-proposal-1",
            ToolRequest = toolRequest,
            GateDecision = AllowedGate(toolRequest),
            RequestedByUserId = "user-1",
            ProposalGoal = "Draft a safe proposal for the implementation issue.",
            Inputs =
            [
                new PatchProposalInputRef
                {
                    InputRefId = "proposal-input-1",
                    RefType = "Issue",
                    RefId = "issue-1",
                    Source = "test",
                    Summary = "Sanitised implementation issue.",
                    EvidenceRefs = ["evidence-issue-1"],
                    IsSanitised = true
                }
            ],
            Parameters = new Dictionary<string, string> { ["scope"] = "single-file" },
            RequestedAtUtc = RequestedAt
        };
    }

    private static AgentToolRequest ValidImplementationToolRequest() =>
        new()
        {
            ToolRequestId = "tool-request-patch-proposal-1",
            Status = AgentToolRequestStatus.PendingGate,
            RequestType = AgentToolRequestType.PatchProposalRequest,
            ToolKind = AgentToolKind.PatchProposal,
            RiskLevel = AgentToolRiskLevel.Medium,
            Scope = new AgentToolRequestScope
            {
                TenantId = "tenant-1",
                ProjectId = "project-1",
                CampaignId = "campaign-1",
                RunId = "run-1",
                AgentRunId = "agent-run-implementation-1",
                CorrelationId = "correlation-1"
            },
            Actor = ValidActor(AgentDefinitionCatalog.ImplementationAgent),
            Purpose = "Request a proposal-only implementation patch.",
            Inputs =
            [
                new AgentToolRequestInput
                {
                    InputId = "input-issue-1",
                    RefType = "Issue",
                    RefId = "issue-1",
                    Source = "test",
                    Summary = "Sanitised implementation issue.",
                    EvidenceRefs = ["evidence-issue-1"],
                    IsSanitised = true
                }
            ],
            Evidence =
            [
                new AgentToolRequestEvidence
                {
                    EvidenceId = "evidence-issue-1",
                    RefType = "RunReport",
                    RefId = "run-report-1",
                    Summary = "Evidence supports requesting this patch proposal.",
                    SupportsNeedForTool = true
                }
            ],
            RequestedAtUtc = RequestedAt
        };

    private static AgentToolRequestActor ValidActor(AgentDefinition agent) =>
        new()
        {
            AgentId = agent.AgentId,
            AgentName = agent.Name,
            AgentKind = agent.Kind,
            ExecutionMode = agent.ExecutionMode,
            DeclaredCapabilities = agent.Capabilities?.ToArray() ?? [],
            ForbiddenCapabilities = agent.ForbiddenCapabilities?.ToArray() ?? []
        };

    private static AgentToolExecutionGateDecision AllowedGate(AgentToolRequest request)
    {
        var gateRequest = new AgentToolExecutionGateRequest
        {
            ToolRequest = request,
            PolicyContext = new AgentToolExecutionGatePolicyContext
            {
                PolicyKnown = true,
                AllowsToolRequest = true,
                AllowsToolExecution = true,
                AllowsTestExecution = request.ToolKind == AgentToolKind.TestRun,
                AllowsPatchProposal = request.ToolKind == AgentToolKind.PatchProposal,
                PolicyRefs = [$"policy:{request.ToolKind}"]
            },
            ApprovalContext = request.ToolKind == AgentToolKind.TestRun
                ? new AgentToolExecutionGateApprovalContext
                {
                    HasGovernanceGateApproval = true,
                    GovernanceGateDecisionId = "governance-gate-1",
                    ApprovalRefs = ["governance-gate-1"]
                }
                : new AgentToolExecutionGateApprovalContext(),
            EvaluatedAtUtc = GateEvaluatedAt
        };

        var result = new AgentToolExecutionGate().Evaluate(gateRequest);
        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(result.Decision);
        Assert.AreEqual(AgentToolExecutionGateDecisionType.Allowed, result.Decision.Decision);
        return result.Decision;
    }

    private static ScriptedTestRunPlanExecutor SuccessExecutor() =>
        new(request => new TestRunPlanExecutionResult
        {
            Succeeded = true,
            ExecutionId = request.ExecutionId,
            ExitCode = 0,
            Summary = "Scripted test-plan executor completed successfully.",
            Outcome = "passed",
            TestsPassed = 9,
            TestsSkipped = 1,
            Duration = TimeSpan.FromSeconds(2),
            EvidenceRefs = ["test-result-1", request.TestPlanRef]
        });

    private static ScriptedPatchProposalGenerator SuccessGenerator() =>
        new(_ => new PatchProposalGenerationResult
        {
            Succeeded = true,
            Summary = "Scripted patch proposal generator produced proposal-only evidence.",
            Proposal = new PatchProposalPackage
            {
                PatchProposalId = "patch-proposal-1",
                Title = "Safe proposal",
                Summary = "Proposal-only implementation package.",
                Rationale = "Evidence supports a human-reviewed proposal.",
                EvidenceRefs = ["evidence-package-1"],
                IsProposalOnly = true,
                RequiresHumanReview = true,
                RequiresValidation = true,
                AppliesCleanlyClaimed = false,
                CreatesAuthority = false,
                CreatesRuntimeAction = false,
                MutatesSource = false,
                AppliesPatch = false,
                FileChanges =
                [
                    new ProposedFileChange
                    {
                        FileChangeId = "file-change-1",
                        Path = "src/example.cs",
                        ChangeKind = "Modify",
                        Summary = "Describe a proposed file change.",
                        EvidenceRefs = ["evidence-file-1"],
                        IsProposalOnly = true,
                        WritesFile = false,
                        DeletesFile = false,
                        AppliesPatch = false,
                        Hunks =
                        [
                            new ProposedPatchHunk
                            {
                                HunkId = "hunk-1",
                                Summary = "Describe a proposed hunk.",
                                BeforeSnippet = "before",
                                AfterSnippet = "after",
                                EvidenceRefs = ["evidence-hunk-1"],
                                ContainsRawPrivateReasoning = false,
                                ContainsSecret = false,
                                ClaimsApplied = false
                            }
                        ]
                    }
                ]
            },
            EvidenceRefs = ["evidence-package-1"]
        });

    private static ToolExecutionAuditRecord RehashPayload(ToolExecutionAuditRecord record) =>
        record with { PayloadSha256 = ToolExecutionAuditRecordFactory.Sha256(record.PayloadJson) };

    private static ToolExecutionAuditRecord RehashEnvelope(ToolExecutionAuditRecord record) =>
        record with { AuditEnvelopeSha256 = ToolExecutionAuditRecordFactory.Sha256(record.AuditEnvelopeJson) };

    private static void AssertNoValidationIssues(ToolExecutionAuditRecord record)
    {
        var issues = new ToolExecutionAuditValidator().Validate(record);
        Assert.IsFalse(issues.Any(), FormatIssues(issues));
    }

    private static void AssertNoUnsafeFlags(ToolExecutionAuditRecord record)
    {
        Assert.IsFalse(record.ContainsRawPrivateReasoning);
        Assert.IsFalse(record.ContainsSecret);
        Assert.IsFalse(record.ClaimsApproval);
        Assert.IsFalse(record.ClaimsPolicyApproval);
        Assert.IsFalse(record.ClaimsHumanApproval);
        Assert.IsFalse(record.ClaimsMemoryPromotion);
        Assert.IsFalse(record.ExecutesTool);
        Assert.IsFalse(record.MutatesSource);
        Assert.IsFalse(record.AppliesPatch);
        Assert.IsFalse(record.WritesFiles);
        Assert.IsFalse(record.DeletesFiles);
        Assert.IsFalse(record.RunsGit);
        Assert.IsFalse(record.CallsExternalSystem);
        Assert.IsFalse(record.SubmitsGitHubReview);
        Assert.IsFalse(record.CreatesPullRequest);
        Assert.IsFalse(record.PromotesMemory);
        Assert.IsFalse(record.CreatesCollectiveMemory);
        Assert.IsFalse(record.WritesWeaviate);
    }

    private async Task DirectInsertAsync(ToolExecutionAuditRecord record)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            INSERT INTO toolaudit.ToolExecutionAuditRecord
            (
                ToolExecutionAuditId, TenantId, ProjectId, CampaignId, RunId, AgentRunId, ManualExecutionId,
                ToolRequestId, GateDecisionId, ToolKind, RequestType, AgentKind, AgentId, AgentName, Status,
                Succeeded, PayloadKind, PayloadJson, PayloadSha256, AuditEnvelopeJson, AuditEnvelopeSha256,
                EvidenceRefsJson, CreatedAtUtc, ContainsRawPrivateReasoning, ContainsSecret, ClaimsApproval,
                ClaimsPolicyApproval, ClaimsHumanApproval, ClaimsMemoryPromotion, ExecutesTool, MutatesSource,
                AppliesPatch, WritesFiles, DeletesFiles, RunsGit, CallsExternalSystem, SubmitsGitHubReview,
                CreatesPullRequest, PromotesMemory, CreatesCollectiveMemory, WritesWeaviate
            )
            VALUES
            (
                @ToolExecutionAuditId, @TenantId, @ProjectId, @CampaignId, @RunId, @AgentRunId, @ManualExecutionId,
                @ToolRequestId, @GateDecisionId, @ToolKind, @RequestType, @AgentKind, @AgentId, @AgentName, @Status,
                @Succeeded, @PayloadKind, @PayloadJson, @PayloadSha256, @AuditEnvelopeJson, @AuditEnvelopeSha256,
                @EvidenceRefsJson, @CreatedAtUtc, @ContainsRawPrivateReasoning, @ContainsSecret, @ClaimsApproval,
                @ClaimsPolicyApproval, @ClaimsHumanApproval, @ClaimsMemoryPromotion, @ExecutesTool, @MutatesSource,
                @AppliesPatch, @WritesFiles, @DeletesFiles, @RunsGit, @CallsExternalSystem, @SubmitsGitHubReview,
                @CreatesPullRequest, @PromotesMemory, @CreatesCollectiveMemory, @WritesWeaviate
            );
            """,
            new
            {
                record.ToolExecutionAuditId,
                record.TenantId,
                record.ProjectId,
                record.CampaignId,
                record.RunId,
                record.AgentRunId,
                record.ManualExecutionId,
                record.ToolRequestId,
                record.GateDecisionId,
                ToolKind = record.ToolKind.ToString(),
                RequestType = record.RequestType.ToString(),
                AgentKind = record.AgentKind.ToString(),
                record.AgentId,
                record.AgentName,
                record.Status,
                record.Succeeded,
                record.PayloadKind,
                record.PayloadJson,
                record.PayloadSha256,
                record.AuditEnvelopeJson,
                record.AuditEnvelopeSha256,
                EvidenceRefsJson = ToolExecutionAuditRecordFactory.Serialize(record.EvidenceRefs),
                record.CreatedAtUtc,
                record.ContainsRawPrivateReasoning,
                record.ContainsSecret,
                record.ClaimsApproval,
                record.ClaimsPolicyApproval,
                record.ClaimsHumanApproval,
                record.ClaimsMemoryPromotion,
                record.ExecutesTool,
                record.MutatesSource,
                record.AppliesPatch,
                record.WritesFiles,
                record.DeletesFiles,
                record.RunsGit,
                record.CallsExternalSystem,
                record.SubmitsGitHubReview,
                record.CreatesPullRequest,
                record.PromotesMemory,
                record.CreatesCollectiveMemory,
                record.WritesWeaviate
            });
    }

    private static async Task ExpectSqlFailsAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (SqlException)
        {
            return;
        }

        Assert.Fail("Expected SQL operation to fail.");
    }

    private static void ExpectArgumentException(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }

        Assert.Fail("Expected ArgumentException.");
    }

    private async Task ApplyToolExecutionAuditMigrationAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        var migration = await File.ReadAllTextAsync(Path.Combine(FindRepositoryRoot(), "Database", "migrate_tool_execution_audit.sql"));
        foreach (var batch in SplitSqlBatches(migration))
            await connection.ExecuteAsync(batch);
    }

    private async Task DropToolExecutionAuditSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID(N'toolaudit.TR_ToolExecutionAuditRecord_BlockUpdateDelete', N'TR') IS NOT NULL
                DROP TRIGGER toolaudit.TR_ToolExecutionAuditRecord_BlockUpdateDelete;
            IF OBJECT_ID(N'toolaudit.AppendToolExecutionAuditRecord', N'P') IS NOT NULL
                DROP PROCEDURE toolaudit.AppendToolExecutionAuditRecord;
            IF OBJECT_ID(N'toolaudit.GetToolExecutionAuditRecord', N'P') IS NOT NULL
                DROP PROCEDURE toolaudit.GetToolExecutionAuditRecord;
            IF OBJECT_ID(N'toolaudit.ListToolExecutionAuditRecordsByRun', N'P') IS NOT NULL
                DROP PROCEDURE toolaudit.ListToolExecutionAuditRecordsByRun;
            IF OBJECT_ID(N'toolaudit.ToolExecutionAuditRecord', N'U') IS NOT NULL
                DROP TABLE toolaudit.ToolExecutionAuditRecord;
            IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'IronDevToolExecutionAuditRuntimeRole' AND type = N'R')
                DROP ROLE IronDevToolExecutionAuditRuntimeRole;
            IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'toolaudit')
                EXEC(N'DROP SCHEMA toolaudit');
            """);
    }

    private static IReadOnlyList<string> SplitSqlBatches(string sql) =>
        sql.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\nGO\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .ToArray();

    private static void AssertHasIssue(IReadOnlyList<ToolExecutionAuditIssue> issues, string code) =>
        Assert.IsTrue(issues.Any(issue => issue.Code == code), $"Expected issue {code}.{Environment.NewLine}{FormatIssues(issues)}");

    private static string FormatIssues(IReadOnlyList<ToolExecutionAuditIssue> issues) =>
        string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static void AssertNoForbiddenTokens(string text, params string[] tokens)
    {
        foreach (var token in tokens)
            Assert.IsFalse(text.Contains(token, StringComparison.Ordinal), $"Unexpected token found: {token}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
