using Dapper;
using IronDev.Core.Agents;
using IronDev.Core.Agents.Audit;
using IronDev.Core.Agents.Concrete;
using IronDev.Data;
using IronDev.Infrastructure.AgentRunAudit;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IronDev.IntegrationTests.Agents;

[TestCategory("Store")]
[TestClass]
public sealed class GovernedManualAgentExecutionStoreTests : IntegrationTestBase
{
    private static readonly DateTimeOffset ExecutedAt = new(2026, 6, 11, 10, 0, 0, TimeSpan.Zero);

    private SqlAgentRunAuditEnvelopeStore _store = null!;
    private SqlAgentRunAuditEnvelopeReadRepository _readRepository = null!;
    private AgentRunAuditQueryService _queryService = null!;

    [TestInitialize]
    public override async Task TestInitialize()
    {
        await base.TestInitialize();
        await DropAgentRunAuditSchemaAsync();
        await ApplyAgentRunAuditMigrationAsync();

        var connectionFactory = ServiceProvider.GetRequiredService<IDbConnectionFactory>();
        _store = new SqlAgentRunAuditEnvelopeStore(connectionFactory);
        _readRepository = new SqlAgentRunAuditEnvelopeReadRepository(connectionFactory);
        _queryService = new AgentRunAuditQueryService(_readRepository);
    }

    [TestCleanup]
    public override async Task TestCleanup()
    {
        try
        {
            await DropAgentRunAuditSchemaAsync();
        }
        catch
        {
            // Cleanup should not hide the real assertion failure.
        }

        await base.TestCleanup();
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_ContractsExist()
    {
        Assert.AreEqual(nameof(StoredManualAgentExecutionStatus), typeof(StoredManualAgentExecutionStatus).Name);
        Assert.AreEqual(nameof(StoredManualAgentExecutionIssue), typeof(StoredManualAgentExecutionIssue).Name);
        Assert.AreEqual(nameof(ManualAgentExecutionSpecialisationSelection), typeof(ManualAgentExecutionSpecialisationSelection).Name);
        Assert.AreEqual(nameof(StoredManualAgentExecutionResult<CriticReviewResult>), typeof(StoredManualAgentExecutionResult<CriticReviewResult>).Name.Split('`')[0]);
        Assert.AreEqual(nameof(ManualAgentExecutionStoreValidator), typeof(ManualAgentExecutionStoreValidator).Name);
        Assert.AreEqual(nameof(IStoredManualIndependentCriticAgentService), typeof(IStoredManualIndependentCriticAgentService).Name);
        Assert.AreEqual(nameof(StoredManualIndependentCriticAgentService), typeof(StoredManualIndependentCriticAgentService).Name);
        Assert.AreEqual(nameof(IStoredManualMemoryImprovementAgentService), typeof(IStoredManualMemoryImprovementAgentService).Name);
        Assert.AreEqual(nameof(StoredManualMemoryImprovementAgentService), typeof(StoredManualMemoryImprovementAgentService).Name);
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_StoresCriticAuditEnvelopeWithSelectedSpecialisation()
    {
        var service = new StoredManualIndependentCriticAgentService(
            new ManualIndependentCriticAgentService(),
            _store);

        var result = service.ExecuteAndStore(
            BuildCriticRequest("review-store-critic"),
            CriticSelection("builtin.critic.code-review"),
            ExecutedAt);

        var read = _readRepository.Get("project-1", result.AgentRunId);

        Assert.AreEqual(StoredManualAgentExecutionStatus.Stored, result.Status, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Output);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.IsNotNull(result.AppendResult);
        Assert.AreEqual(AgentRunAuditEnvelopeAppendStatus.Appended, result.AppendResult.Status);
        Assert.IsNotNull(read);
        Assert.AreEqual(result.AgentRunId, read.Run.AgentRunId);
        Assert.IsTrue(read.Inputs.Any(input =>
            input.RefType == "AgentSpecialisationDefinition" &&
            input.RefId == "builtin.critic.code-review" &&
            !input.IsAuthoritativeForAction &&
            !input.ContainsRawPrivateReasoning));
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_StoresMemoryAuditEnvelopeWithSelectedSpecialisation()
    {
        var service = new StoredManualMemoryImprovementAgentService(
            new ManualMemoryImprovementAgentService(),
            _store);

        var result = service.ExecuteAndStore(
            BuildMemoryRequest("detection-store-memory"),
            MemorySelection("builtin.memory.repeated-failure-mode-detector"),
            ExecutedAt);

        var read = _readRepository.Get("project-1", result.AgentRunId);

        Assert.AreEqual(StoredManualAgentExecutionStatus.Stored, result.Status, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Output);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.IsTrue(result.Output.ProposalDrafts.All(proposal =>
            proposal.IsProposalOnly &&
            !proposal.CreatesCollectiveMemory &&
            !proposal.PromotesMemory &&
            proposal.RequiresHumanReview));
        Assert.IsNotNull(read);
        Assert.IsTrue(read.Inputs.Any(input =>
            input.RefType == "AgentSpecialisationDefinition" &&
            input.RefId == "builtin.memory.repeated-failure-mode-detector" &&
            !input.IsAuthoritativeForAction &&
            !input.ContainsRawPrivateReasoning));
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_ReadQueryCanProjectStoredManualRun()
    {
        var service = new StoredManualIndependentCriticAgentService(
            new ManualIndependentCriticAgentService(),
            _store);

        var result = service.ExecuteAndStore(
            BuildCriticRequest("review-query"),
            CriticSelection("builtin.critic.architecture-review"),
            ExecutedAt);

        var detail = _queryService.GetAgentRun("project-1", result.AgentRunId);

        Assert.AreEqual(StoredManualAgentExecutionStatus.Stored, result.Status, FormatIssues(result.Issues));
        Assert.IsNotNull(detail.Run);
        Assert.AreEqual(result.AgentRunId, detail.Run.Run.AgentRunId);
        Assert.IsTrue(detail.Run.Inputs.Any(input => input.RefType == "AgentSpecialisationDefinition"));
        Assert.IsTrue(detail.Run.Outputs.Any(output => output.RefType == nameof(CriticReviewResult)));
        Assert.IsTrue(detail.Run.ThoughtLedger.Count > 0);
        Assert.IsTrue(detail.Run.CapabilityUses.Count > 0);
        Assert.IsTrue(detail.Run.BoundaryDecisions.Count > 0);
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_DuplicateStoredRunIsIdempotentButChangedEnvelopeConflicts()
    {
        var service = new StoredManualIndependentCriticAgentService(
            new ManualIndependentCriticAgentService(),
            _store);

        var first = service.ExecuteAndStore(
            BuildCriticRequest("review-duplicate"),
            CriticSelection("builtin.critic.security-review"),
            ExecutedAt);
        var second = service.ExecuteAndStore(
            BuildCriticRequest("review-duplicate"),
            CriticSelection("builtin.critic.security-review"),
            ExecutedAt);
        var conflict = service.ExecuteAndStore(
            BuildCriticRequest("review-duplicate", requestSummary: "Review the same evidence with a changed safe summary."),
            CriticSelection("builtin.critic.security-review"),
            ExecutedAt.AddMinutes(2));

        Assert.AreEqual(StoredManualAgentExecutionStatus.Stored, first.Status, FormatIssues(first.Issues));
        Assert.AreEqual(StoredManualAgentExecutionStatus.AlreadyStored, second.Status, FormatIssues(second.Issues));
        Assert.AreEqual(StoredManualAgentExecutionStatus.Conflict, conflict.Status);
        Assert.IsTrue(conflict.Issues.Any(issue => issue.Code == "AGENT_RUN_AUDIT_DUPLICATE_CONFLICT"));
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_InvalidCriticSpecialisationDoesNotExecuteOrAppend()
    {
        var manual = new FakeManualCriticAgentService((request, reviewedAt) =>
            new ManualIndependentCriticAgentService().Review(request, reviewedAt));
        var store = new FakeAuditStore(AgentRunAuditEnvelopeAppendStatus.Appended);
        var service = new StoredManualIndependentCriticAgentService(manual, store);

        var result = service.ExecuteAndStore(
            BuildCriticRequest("review-invalid-specialisation"),
            CriticSelection("builtin.memory.repeated-failure-mode-detector"),
            ExecutedAt);

        Assert.AreEqual(StoredManualAgentExecutionStatus.InvalidSpecialisation, result.Status);
        Assert.IsTrue(result.Issues.Count > 0);
        Assert.AreEqual(0, manual.CallCount);
        Assert.AreEqual(0, store.AppendCount);
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_InvalidMemorySpecialisationDoesNotExecuteOrAppend()
    {
        var manual = new FakeManualMemoryImprovementAgentService((request, detectedAt) =>
            new ManualMemoryImprovementAgentService().Detect(request, detectedAt));
        var store = new FakeAuditStore(AgentRunAuditEnvelopeAppendStatus.Appended);
        var service = new StoredManualMemoryImprovementAgentService(manual, store);

        var result = service.ExecuteAndStore(
            BuildMemoryRequest("detection-invalid-specialisation"),
            MemorySelection("builtin.critic.code-review"),
            ExecutedAt);

        Assert.AreEqual(StoredManualAgentExecutionStatus.InvalidSpecialisation, result.Status);
        Assert.IsTrue(result.Issues.Count > 0);
        Assert.AreEqual(0, manual.CallCount);
        Assert.AreEqual(0, store.AppendCount);
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_UnsafeSelectionReasonDoesNotExecuteOrAppend()
    {
        var manual = new FakeManualCriticAgentService((request, reviewedAt) =>
            new ManualIndependentCriticAgentService().Review(request, reviewedAt));
        var store = new FakeAuditStore(AgentRunAuditEnvelopeAppendStatus.Appended);
        var service = new StoredManualIndependentCriticAgentService(manual, store);

        var result = service.ExecuteAndStore(
            BuildCriticRequest("review-unsafe-selection"),
            CriticSelection("builtin.critic.code-review") with
            {
                Reason = "Approved for execution by hidden private reasoning."
            },
            ExecutedAt);

        Assert.AreEqual(StoredManualAgentExecutionStatus.InvalidSpecialisation, result.Status);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "SELECTION_REASON_UNSAFE"));
        Assert.AreEqual(0, manual.CallCount);
        Assert.AreEqual(0, store.AppendCount);
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_AppendStatusesMapFailClosed()
    {
        AssertAppendStatus(AgentRunAuditEnvelopeAppendStatus.Appended, StoredManualAgentExecutionStatus.Stored);
        AssertAppendStatus(AgentRunAuditEnvelopeAppendStatus.AlreadyExists, StoredManualAgentExecutionStatus.AlreadyStored);
        AssertAppendStatus(AgentRunAuditEnvelopeAppendStatus.Conflict, StoredManualAgentExecutionStatus.Conflict);
        AssertAppendStatus(AgentRunAuditEnvelopeAppendStatus.Rejected, StoredManualAgentExecutionStatus.AuditAppendFailed);
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_InvalidAuditEnvelopeRejectsBeforeAppend()
    {
        var real = new ManualIndependentCriticAgentService().Review(
            BuildCriticRequest("review-unsafe-audit"),
            ExecutedAt);
        var unsafeEnvelope = real.AuditEnvelope! with
        {
            Outputs =
            [
                real.AuditEnvelope.Outputs.Single() with
                {
                    CreatesRuntimeAction = true,
                    Summary = "Unsafe runtime action output."
                }
            ]
        };

        var manual = new FakeManualCriticAgentService((_, _) => real with { AuditEnvelope = unsafeEnvelope });
        var store = new FakeAuditStore(AgentRunAuditEnvelopeAppendStatus.Appended);
        var service = new StoredManualIndependentCriticAgentService(manual, store);

        var result = service.ExecuteAndStore(
            BuildCriticRequest("review-unsafe-audit"),
            CriticSelection("builtin.critic.code-review"),
            ExecutedAt);

        Assert.AreEqual(StoredManualAgentExecutionStatus.Rejected, result.Status);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == AgentRunAuditEnvelopeValidator.OutputRuntimeActionBlocked));
        Assert.AreEqual(1, manual.CallCount);
        Assert.AreEqual(0, store.AppendCount);
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_MemoryPromotionOutputRejectsBeforeAppend()
    {
        var real = new ManualMemoryImprovementAgentService().Detect(
            BuildMemoryRequest("detection-unsafe-output"),
            ExecutedAt);
        var unsafeOutput = real.DetectionResult! with
        {
            ProposalDrafts =
            [
                real.DetectionResult.ProposalDrafts.Single() with
                {
                    PromotesMemory = true
                }
            ]
        };

        var manual = new FakeManualMemoryImprovementAgentService((_, _) => real with { DetectionResult = unsafeOutput });
        var store = new FakeAuditStore(AgentRunAuditEnvelopeAppendStatus.Appended);
        var service = new StoredManualMemoryImprovementAgentService(manual, store);

        var result = service.ExecuteAndStore(
            BuildMemoryRequest("detection-unsafe-output"),
            MemorySelection("builtin.memory.repeated-failure-mode-detector"),
            ExecutedAt);

        Assert.AreEqual(StoredManualAgentExecutionStatus.Rejected, result.Status);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == ManualMemoryImprovementDetectionValidator.PromotesMemoryBlocked ||
            issue.Code == "MEMORY_OUTPUT_PROMOTES_MEMORY"));
        Assert.AreEqual(1, manual.CallCount);
        Assert.AreEqual(0, store.AppendCount);
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_DiRegistrationExistsWithoutWriteEndpoint()
    {
        var repositoryRoot = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "IronDev.Api", "Program.cs"));
        var controllerPath = Path.Combine(repositoryRoot, "IronDev.Api", "Controllers", "AgentRunAuditController.cs");
        var controller = File.Exists(controllerPath) ? File.ReadAllText(controllerPath) : string.Empty;

        StringAssert.Contains(program, "IStoredManualIndependentCriticAgentService");
        StringAssert.Contains(program, "IStoredManualMemoryImprovementAgentService");
        Assert.IsFalse(controller.Contains("[HttpPost", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(controller.Contains("MapPost", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void GovernedManualAgentExecutionStore_DoesNotAddRuntimeOrMutationBoundary()
    {
        var repositoryRoot = FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "IronDev.Core",
            "Agents",
            "Concrete",
            "StoredManualAgentExecutionService.cs"));

        var forbiddenTokens = new[]
        {
            "AgentScheduler",
            "AgentOrchestrator",
            "AgentPromptRunner",
            "AgentToolRouter",
            "ExecuteAgentAsync",
            "RunAgentAsync",
            "IChatCompletion",
            "OpenAI",
            "Anthropic",
            "Gemini",
            "HttpClient",
            "ControllerBase",
            "File.Copy",
            "File.Delete",
            "ProcessStartInfo",
            "Weaviate",
            "INSERT INTO",
            "UPDATE ",
            "DELETE FROM"
        };

        foreach (var token in forbiddenTokens)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
    }

    private static void AssertAppendStatus(
        AgentRunAuditEnvelopeAppendStatus appendStatus,
        StoredManualAgentExecutionStatus expectedStatus)
    {
        var service = new StoredManualIndependentCriticAgentService(
            new ManualIndependentCriticAgentService(),
            new FakeAuditStore(appendStatus));

        var result = service.ExecuteAndStore(
            BuildCriticRequest($"review-append-{appendStatus}"),
            CriticSelection("builtin.critic.code-review"),
            ExecutedAt);

        Assert.AreEqual(expectedStatus, result.Status, FormatIssues(result.Issues));
        Assert.IsNotNull(result.Output);
        Assert.IsNotNull(result.AuditEnvelope);
        Assert.IsNotNull(result.AppendResult);
    }

    private static ManualAgentExecutionSpecialisationSelection CriticSelection(string specialisationId) =>
        new()
        {
            SpecialisationId = specialisationId,
            RequestedByUserId = "user-1",
            Reason = "Store the manual critic review audit envelope for governed review."
        };

    private static ManualAgentExecutionSpecialisationSelection MemorySelection(string specialisationId) =>
        new()
        {
            SpecialisationId = specialisationId,
            RequestedByUserId = "user-1",
            Reason = "Store the manual memory-improvement audit envelope for governed review."
        };

    private static ManualCriticReviewRequest BuildCriticRequest(
        string reviewRequestId,
        string requestSummary = "Review the supplied diff and report evidence.") =>
        new()
        {
            ReviewRequestId = reviewRequestId,
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            SubjectType = CriticReviewSubjectType.PullRequest,
            SubjectId = "pr-1",
            RequestedByUserId = "user-1",
            CorrelationId = $"corr-{reviewRequestId}",
            RequestSummary = requestSummary,
            Inputs =
            [
                new ManualCriticReviewInputRef
                {
                    InputRefId = "input-1",
                    RefType = "PullRequestDiff",
                    RefId = "diff-1",
                    Source = "manual test",
                    Summary = "Diff evidence supplied by caller.",
                    EvidenceRefs = ["evidence-1"]
                }
            ],
            FindingDrafts =
            [
                new ManualCriticFindingDraft
                {
                    Severity = CriticSeverity.High,
                    Title = "Missing boundary evidence",
                    Problem = "The change does not prove the review boundary.",
                    WhyItMatters = "Without evidence, the manual critic output could be mistaken for governance.",
                    RequiredFix = "Add boundary evidence before merge.",
                    EvidenceRefs = ["evidence-1"],
                    BlocksMerge = true,
                    RequiresHumanReview = true
                }
            ],
            RequestedVerdict = CriticReviewVerdict.RequestChanges
        };

    private static ManualMemoryImprovementDetectionRequest BuildMemoryRequest(string detectionRequestId) =>
        new()
        {
            DetectionRequestId = detectionRequestId,
            TenantId = "tenant-1",
            ProjectId = "project-1",
            CampaignId = "campaign-1",
            RunId = "run-1",
            RequestedByUserId = "user-1",
            CorrelationId = $"corr-{detectionRequestId}",
            RequestSummary = "Review supplied memory evidence for repeated patterns.",
            Inputs =
            [
                new ManualMemoryImprovementInputRef
                {
                    InputRefId = "input-1",
                    RefType = "AgentRunAuditEnvelope",
                    RefId = "audit-1",
                    Source = "manual test",
                    Summary = "Audit evidence supplied by caller.",
                    EvidenceRefs = ["evidence-1"]
                }
            ],
            PatternDrafts =
            [
                new ManualMemoryImprovementPatternDraft
                {
                    PatternType = MemoryImprovementPatternType.RepeatedGovernanceBlock,
                    Summary = "Repeated review boundary confusion appears in three reviews.",
                    Confidence = 0.82m,
                    EvidenceRefs = ["evidence-1"],
                    RelatedMemoryIds = ["memory-1"],
                    RelatedProposalIds = ["proposal-1"],
                    RequiresHumanReview = true
                }
            ],
            ProposalDrafts =
            [
                new ManualMemoryImprovementProposalDraftInput
                {
                    Title = "Clarify evidence boundary",
                    Summary = "Draft a proposal reminding agents that evidence is accountability, not authority.",
                    Rationale = "Repeated reviews show the same confusion.",
                    SourcePatternIndex = 0,
                    EvidenceRefs = ["evidence-1"],
                    IsProposalOnly = true,
                    CreatesCollectiveMemory = false,
                    PromotesMemory = false,
                    RequiresHumanReview = true
                }
            ]
        };

    private async Task ApplyAgentRunAuditMigrationAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(),
            "Database",
            "migrate_agent_run_audit_envelope.sql")));
    }

    private async Task DropAgentRunAuditSchemaAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            """
            IF OBJECT_ID('agent.TR_AgentRunAuditEnvelope_BlockUpdateDelete', 'TR') IS NOT NULL
                DROP TRIGGER agent.TR_AgentRunAuditEnvelope_BlockUpdateDelete;
            IF OBJECT_ID('agent.AgentRunAuditEnvelope', 'U') IS NOT NULL
                DROP TABLE agent.AgentRunAuditEnvelope;
            IF SCHEMA_ID('agent') IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID('agent'))
                DROP SCHEMA agent;
            """);
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

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static string FormatIssues(IReadOnlyList<StoredManualAgentExecutionIssue> issues)
    {
        return string.Join(Environment.NewLine, issues.Select(issue => $"{issue.Code}: {issue.Message}"));
    }

    private sealed class FakeAuditStore : IAgentRunAuditEnvelopeStore
    {
        private readonly AgentRunAuditEnvelopeAppendStatus _status;

        public FakeAuditStore(AgentRunAuditEnvelopeAppendStatus status)
        {
            _status = status;
        }

        public int AppendCount { get; private set; }

        public AgentRunAuditEnvelopeAppendResult Append(AgentRunAuditEnvelope envelope, DateTimeOffset appendedAtUtc)
        {
            AppendCount++;

            return new AgentRunAuditEnvelopeAppendResult
            {
                Status = _status,
                AgentRunId = envelope.Run.AgentRunId,
                EnvelopeSha256 = new string('a', 64),
                Issues = _status switch
                {
                    AgentRunAuditEnvelopeAppendStatus.Rejected =>
                    [
                        new AgentRunAuditEnvelopeStoreIssue
                        {
                            Code = "FAKE_APPEND_REJECTED",
                            Severity = AgentDefinitionValidator.SeverityError,
                            Message = "Fake append rejected."
                        }
                    ],
                    AgentRunAuditEnvelopeAppendStatus.Conflict =>
                    [
                        new AgentRunAuditEnvelopeStoreIssue
                        {
                            Code = "FAKE_APPEND_CONFLICT",
                            Severity = AgentDefinitionValidator.SeverityError,
                            Message = "Fake append conflict."
                        }
                    ],
                    _ => Array.Empty<AgentRunAuditEnvelopeStoreIssue>()
                }
            };
        }
    }

    private sealed class FakeManualCriticAgentService : IManualIndependentCriticAgentService
    {
        private readonly Func<ManualCriticReviewRequest, DateTimeOffset, ManualCriticReviewResult> _handler;

        public FakeManualCriticAgentService(Func<ManualCriticReviewRequest, DateTimeOffset, ManualCriticReviewResult> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public ManualCriticReviewResult Review(ManualCriticReviewRequest request, DateTimeOffset reviewedAtUtc)
        {
            CallCount++;
            return _handler(request, reviewedAtUtc);
        }
    }

    private sealed class FakeManualMemoryImprovementAgentService : IManualMemoryImprovementAgentService
    {
        private readonly Func<ManualMemoryImprovementDetectionRequest, DateTimeOffset, ManualMemoryImprovementDetectionResult> _handler;

        public FakeManualMemoryImprovementAgentService(Func<ManualMemoryImprovementDetectionRequest, DateTimeOffset, ManualMemoryImprovementDetectionResult> handler)
        {
            _handler = handler;
        }

        public int CallCount { get; private set; }

        public ManualMemoryImprovementDetectionResult Detect(
            ManualMemoryImprovementDetectionRequest request,
            DateTimeOffset detectedAtUtc)
        {
            CallCount++;
            return _handler(request, detectedAtUtc);
        }
    }
}
