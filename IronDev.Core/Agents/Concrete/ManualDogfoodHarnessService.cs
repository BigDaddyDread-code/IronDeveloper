using IronDev.Core.Agents.Audit;
using Audit = IronDev.Core.Agents.Audit;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.Core.Agents.Concrete;

public enum ManualDogfoodHarnessStatus
{
    Succeeded = 1,
    Partial = 2,
    InvalidRequest = 3,
    Failed = 4
}

public enum ManualDogfoodHarnessScenario
{
    TicketReviewFixProposal = 1,
    TestFailureRepairProposal = 2,
    RealRunMemoryImprovement = 3
}

public sealed record ManualDogfoodHarnessRequest
{
    public required string HarnessRunId { get; init; }
    public required string TenantId { get; init; }
    public required string ProjectId { get; init; }
    public string? CampaignId { get; init; }
    public required string RequestedByUserId { get; init; }
    public bool IncludeTicketReviewLoop { get; init; } = true;
    public bool IncludeTestFailureRepairLoop { get; init; } = true;
    public bool IncludeRealRunMemoryImprovement { get; init; } = true;
    public bool PersistToolExecutionAudit { get; init; }
    public DateTimeOffset RequestedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ManualDogfoodHarnessScenarioResult
{
    public required ManualDogfoodHarnessScenario Scenario { get; init; }
    public required bool Succeeded { get; init; }
    public required string ScenarioRunId { get; init; }
    public object? Result { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsAdvisoryOnly { get; init; }
    public bool RequiresHumanReview { get; init; }
    public bool MutatesSource { get; init; }
    public bool AppliesPatch { get; init; }
    public bool RunsTestsAutomatically { get; init; }
    public bool PromotesMemory { get; init; }
    public bool WritesWeaviate { get; init; }
    public bool CreatesPullRequest { get; init; }
}

public sealed record ManualDogfoodHarnessSummary
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> ScenarioSummaries { get; init; } = [];
    public IReadOnlyList<string> RequiredHumanDecisions { get; init; } = [];
    public IReadOnlyList<string> RequiredValidation { get; init; } = [];
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool IsAdvisoryOnly { get; init; }
    public bool CreatesAuthority { get; init; }
    public bool GrantsApproval { get; init; }
    public bool MutatesSource { get; init; }
    public bool AppliesPatch { get; init; }
    public bool RunsTestsAutomatically { get; init; }
    public bool PromotesMemory { get; init; }
    public bool WritesWeaviate { get; init; }
    public bool CreatesPullRequest { get; init; }
}

public sealed record ManualDogfoodHarnessIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record ManualDogfoodHarnessResult
{
    public required bool Succeeded { get; init; }
    public required ManualDogfoodHarnessStatus Status { get; init; }
    public required string HarnessRunId { get; init; }
    public IReadOnlyList<ManualDogfoodHarnessScenarioResult> Scenarios { get; init; } = [];
    public ManualDogfoodHarnessSummary? Summary { get; init; }
    public Audit.AgentRunAuditEnvelope? AuditEnvelope { get; init; }
    public IReadOnlyList<ManualDogfoodHarnessIssue> Issues { get; init; } = [];
}

public interface IManualDogfoodHarnessService
{
    Task<ManualDogfoodHarnessResult> RunAsync(
        ManualDogfoodHarnessRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ManualDogfoodHarnessValidator
{
    public const string DogfoodHarnessRequestRequired = "DOGFOOD_HARNESS_REQUEST_REQUIRED";
    public const string DogfoodHarnessScopeRequired = "DOGFOOD_HARNESS_SCOPE_REQUIRED";
    public const string DogfoodHarnessUserRequired = "DOGFOOD_HARNESS_USER_REQUIRED";
    public const string DogfoodHarnessNoScenarios = "DOGFOOD_HARNESS_NO_SCENARIOS";
    public const string DogfoodHarnessAuditStoreRequired = "DOGFOOD_HARNESS_AUDIT_STORE_REQUIRED";
    public const string DogfoodHarnessTicketLoopFailed = "DOGFOOD_HARNESS_TICKET_LOOP_FAILED";
    public const string DogfoodHarnessTestFailureLoopFailed = "DOGFOOD_HARNESS_TEST_FAILURE_LOOP_FAILED";
    public const string DogfoodHarnessMemoryImprovementFailed = "DOGFOOD_HARNESS_MEMORY_IMPROVEMENT_FAILED";
    public const string DogfoodHarnessScenarioUnsafe = "DOGFOOD_HARNESS_SCENARIO_UNSAFE";
    public const string DogfoodHarnessAuditInvalid = "DOGFOOD_HARNESS_AUDIT_INVALID";
    public const string DogfoodHarnessThoughtLedgerInvalid = "DOGFOOD_HARNESS_THOUGHT_LEDGER_INVALID";
    public const string DogfoodHarnessSourceMutationForbidden = "DOGFOOD_HARNESS_SOURCE_MUTATION_FORBIDDEN";
    public const string DogfoodHarnessMemoryPromotionForbidden = "DOGFOOD_HARNESS_MEMORY_PROMOTION_FORBIDDEN";
    public const string DogfoodHarnessIndexWriteForbidden = "DOGFOOD_HARNESS_INDEX_WRITE_FORBIDDEN";

    public IReadOnlyList<ManualDogfoodHarnessIssue> Validate(
        ManualDogfoodHarnessRequest? request,
        bool toolAuditStoreAvailable)
    {
        var issues = new List<ManualDogfoodHarnessIssue>();

        if (request is null)
        {
            AddError(issues, DogfoodHarnessRequestRequired, "Manual dogfood harness request is required.", "request");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(request.HarnessRunId))
            AddError(issues, DogfoodHarnessRequestRequired, "HarnessRunId is required.", nameof(request.HarnessRunId));

        if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.ProjectId))
            AddError(issues, DogfoodHarnessScopeRequired, "TenantId and ProjectId are required.", "Scope");

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
            AddError(issues, DogfoodHarnessUserRequired, "RequestedByUserId is required.", nameof(request.RequestedByUserId));

        if (!request.IncludeTicketReviewLoop &&
            !request.IncludeTestFailureRepairLoop &&
            !request.IncludeRealRunMemoryImprovement)
        {
            AddError(issues, DogfoodHarnessNoScenarios, "At least one manual dogfood scenario must be included.", "Scenarios");
        }

        if (request.PersistToolExecutionAudit && !toolAuditStoreAvailable)
            AddError(issues, DogfoodHarnessAuditStoreRequired, "PersistToolExecutionAudit requires an explicit audit-store-capable harness setup.", nameof(request.PersistToolExecutionAudit));

        return issues;
    }

    public IReadOnlyList<ManualDogfoodHarnessIssue> ValidateScenarioResult(ManualDogfoodHarnessScenarioResult result)
    {
        var issues = new List<ManualDogfoodHarnessIssue>();

        if (string.IsNullOrWhiteSpace(result.ScenarioRunId))
            AddError(issues, DogfoodHarnessScenarioUnsafe, "ScenarioRunId is required.", nameof(result.ScenarioRunId));

        if (!result.IsAdvisoryOnly)
            AddError(issues, DogfoodHarnessScenarioUnsafe, "Dogfood harness scenario results must be advisory-only.", nameof(result.IsAdvisoryOnly));

        if (result.MutatesSource)
            AddError(issues, DogfoodHarnessSourceMutationForbidden, "Dogfood harness scenario must not mutate source.", nameof(result.MutatesSource));

        if (result.AppliesPatch)
            AddError(issues, DogfoodHarnessSourceMutationForbidden, "Dogfood harness scenario must not apply patches.", nameof(result.AppliesPatch));

        if (result.RunsTestsAutomatically)
            AddError(issues, DogfoodHarnessScenarioUnsafe, "Dogfood harness scenario must not rerun tests automatically.", nameof(result.RunsTestsAutomatically));

        if (result.PromotesMemory)
            AddError(issues, DogfoodHarnessMemoryPromotionForbidden, "Dogfood harness scenario must not change memory authority.", nameof(result.PromotesMemory));

        if (result.WritesWeaviate)
            AddError(issues, DogfoodHarnessIndexWriteForbidden, "Dogfood harness scenario must not write a vector index.", nameof(result.WritesWeaviate));

        if (result.CreatesPullRequest)
            AddError(issues, DogfoodHarnessScenarioUnsafe, "Dogfood harness scenario must not create pull requests.", nameof(result.CreatesPullRequest));

        return issues;
    }

    public IReadOnlyList<ManualDogfoodHarnessIssue> ValidateSummary(ManualDogfoodHarnessSummary summary)
    {
        var issues = new List<ManualDogfoodHarnessIssue>();

        if (!summary.IsAdvisoryOnly)
            AddError(issues, DogfoodHarnessScenarioUnsafe, "Dogfood harness summary must be advisory-only.", nameof(summary.IsAdvisoryOnly));

        if (summary.CreatesAuthority)
            AddError(issues, DogfoodHarnessScenarioUnsafe, "Dogfood harness summary must not create authority.", nameof(summary.CreatesAuthority));

        if (summary.GrantsApproval)
            AddError(issues, DogfoodHarnessScenarioUnsafe, "Dogfood harness summary must not grant approval.", nameof(summary.GrantsApproval));

        if (summary.MutatesSource)
            AddError(issues, DogfoodHarnessSourceMutationForbidden, "Dogfood harness summary must not mutate source.", nameof(summary.MutatesSource));

        if (summary.AppliesPatch)
            AddError(issues, DogfoodHarnessSourceMutationForbidden, "Dogfood harness summary must not apply patches.", nameof(summary.AppliesPatch));

        if (summary.RunsTestsAutomatically)
            AddError(issues, DogfoodHarnessScenarioUnsafe, "Dogfood harness summary must not rerun tests automatically.", nameof(summary.RunsTestsAutomatically));

        if (summary.PromotesMemory)
            AddError(issues, DogfoodHarnessMemoryPromotionForbidden, "Dogfood harness summary must not change memory authority.", nameof(summary.PromotesMemory));

        if (summary.WritesWeaviate)
            AddError(issues, DogfoodHarnessIndexWriteForbidden, "Dogfood harness summary must not write a vector index.", nameof(summary.WritesWeaviate));

        if (summary.CreatesPullRequest)
            AddError(issues, DogfoodHarnessScenarioUnsafe, "Dogfood harness summary must not create pull requests.", nameof(summary.CreatesPullRequest));

        return issues;
    }

    private static void AddError(List<ManualDogfoodHarnessIssue> issues, string code, string message, string field) =>
        issues.Add(new ManualDogfoodHarnessIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });
}

public sealed class ManualDogfoodHarnessService : IManualDogfoodHarnessService
{
    private readonly IManualTicketReviewFixProposalLoopService _ticketLoop;
    private readonly IManualTestFailureRepairProposalLoopService _testFailureLoop;
    private readonly IManualRealRunMemoryImprovementService _memoryImprovement;
    private readonly ManualDogfoodHarnessValidator _validator;
    private readonly Audit.AgentRunAuditEnvelopeValidator _auditValidator;
    private readonly Audit.ThoughtLedgerSafetyValidator _thoughtLedgerValidator;
    private readonly bool _toolAuditStoreAvailable;

    public ManualDogfoodHarnessService()
        : this(
            new ManualTicketReviewFixProposalLoopService(),
            new ManualTestFailureRepairProposalLoopService(),
            new ManualRealRunMemoryImprovementService(),
            toolAuditStoreAvailable: false)
    {
    }

    public ManualDogfoodHarnessService(IToolExecutionAuditStore toolExecutionAuditStore)
        : this(
            new ManualTicketReviewFixProposalLoopService(
                new ManualIndependentCriticAgentService(),
                new ManualImplementationAgentPatchProposalService(),
                new AgentToolExecutionGate(),
                toolExecutionAuditStore),
            new ManualTestFailureRepairProposalLoopService(
                new ManualIndependentCriticAgentService(),
                new ManualImplementationAgentPatchProposalService(),
                new AgentToolExecutionGate(),
                toolExecutionAuditStore),
            new ManualRealRunMemoryImprovementService(),
            toolAuditStoreAvailable: true)
    {
    }

    public ManualDogfoodHarnessService(
        IManualTicketReviewFixProposalLoopService ticketLoop,
        IManualTestFailureRepairProposalLoopService testFailureLoop,
        IManualRealRunMemoryImprovementService memoryImprovement,
        bool toolAuditStoreAvailable = false,
        ManualDogfoodHarnessValidator? validator = null,
        Audit.AgentRunAuditEnvelopeValidator? auditValidator = null,
        Audit.ThoughtLedgerSafetyValidator? thoughtLedgerValidator = null)
    {
        _ticketLoop = ticketLoop;
        _testFailureLoop = testFailureLoop;
        _memoryImprovement = memoryImprovement;
        _toolAuditStoreAvailable = toolAuditStoreAvailable;
        _validator = validator ?? new ManualDogfoodHarnessValidator();
        _auditValidator = auditValidator ?? new Audit.AgentRunAuditEnvelopeValidator();
        _thoughtLedgerValidator = thoughtLedgerValidator ?? new Audit.ThoughtLedgerSafetyValidator();
    }

    public async Task<ManualDogfoodHarnessResult> RunAsync(
        ManualDogfoodHarnessRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestIssues = _validator.Validate(request, _toolAuditStoreAvailable);
        if (requestIssues.Count > 0)
            return Failed(request?.HarnessRunId ?? string.Empty, ManualDogfoodHarnessStatus.InvalidRequest, requestIssues);

        var scenarios = new List<ManualDogfoodHarnessScenarioResult>();
        var issues = new List<ManualDogfoodHarnessIssue>();

        if (request.IncludeTicketReviewLoop)
        {
            var result = await RunTicketScenario(request, cancellationToken).ConfigureAwait(false);
            AddScenarioResult(result, scenarios, issues, ManualDogfoodHarnessValidator.DogfoodHarnessTicketLoopFailed);
        }

        if (request.IncludeTestFailureRepairLoop)
        {
            var result = await RunTestFailureScenario(request, cancellationToken).ConfigureAwait(false);
            AddScenarioResult(result, scenarios, issues, ManualDogfoodHarnessValidator.DogfoodHarnessTestFailureLoopFailed);
        }

        if (request.IncludeRealRunMemoryImprovement)
        {
            var result = await RunMemoryImprovementScenario(request, scenarios, cancellationToken).ConfigureAwait(false);
            AddScenarioResult(result, scenarios, issues, ManualDogfoodHarnessValidator.DogfoodHarnessMemoryImprovementFailed);
        }

        var summary = BuildSummary(scenarios);
        issues.AddRange(_validator.ValidateSummary(summary));

        var auditEnvelope = BuildAuditEnvelope(request, scenarios, summary);
        var auditIssues = _auditValidator.Validate(auditEnvelope);
        issues.AddRange(auditIssues.Select(issue => ToIssue(ManualDogfoodHarnessValidator.DogfoodHarnessAuditInvalid, issue.Message, nameof(Audit.AgentRunAuditEnvelope))));

        var thoughtIssues = _thoughtLedgerValidator.Validate(auditEnvelope.ThoughtLedger);
        issues.AddRange(thoughtIssues.Select(issue => ToIssue(ManualDogfoodHarnessValidator.DogfoodHarnessThoughtLedgerInvalid, issue.Message, nameof(auditEnvelope.ThoughtLedger))));

        if (issues.Any(issue => issue.Code is ManualDogfoodHarnessValidator.DogfoodHarnessScenarioUnsafe or
            ManualDogfoodHarnessValidator.DogfoodHarnessSourceMutationForbidden or
            ManualDogfoodHarnessValidator.DogfoodHarnessMemoryPromotionForbidden or
            ManualDogfoodHarnessValidator.DogfoodHarnessIndexWriteForbidden or
            ManualDogfoodHarnessValidator.DogfoodHarnessAuditInvalid or
            ManualDogfoodHarnessValidator.DogfoodHarnessThoughtLedgerInvalid))
        {
            return Failed(request.HarnessRunId, ManualDogfoodHarnessStatus.Failed, issues, scenarios, summary, auditEnvelope);
        }

        var succeededCount = scenarios.Count(scenario => scenario.Succeeded);
        var status = succeededCount == scenarios.Count
            ? ManualDogfoodHarnessStatus.Succeeded
            : succeededCount > 0
                ? ManualDogfoodHarnessStatus.Partial
                : ManualDogfoodHarnessStatus.Failed;

        return new ManualDogfoodHarnessResult
        {
            Succeeded = status == ManualDogfoodHarnessStatus.Succeeded,
            Status = status,
            HarnessRunId = request.HarnessRunId,
            Scenarios = scenarios,
            Summary = summary,
            AuditEnvelope = auditEnvelope,
            Issues = issues
        };
    }

    private async Task<ManualDogfoodHarnessScenarioResult> RunTicketScenario(
        ManualDogfoodHarnessRequest request,
        CancellationToken cancellationToken)
    {
        var scenarioRunId = $"{request.HarnessRunId}-ticket-review";
        var result = await _ticketLoop.RunAsync(BuildTicketRequest(request, scenarioRunId), cancellationToken).ConfigureAwait(false);
        var evidenceRefs = CollectEvidenceRefs(result.AuditEnvelope, result.Summary?.EvidenceRefs ?? []);

        return new ManualDogfoodHarnessScenarioResult
        {
            Scenario = ManualDogfoodHarnessScenario.TicketReviewFixProposal,
            Succeeded = result.Succeeded &&
                result.CriticStage?.IsReviewOnly == true &&
                result.ProposalStage?.IsProposalOnly == true &&
                result.Summary?.IsAdvisoryOnly == true &&
                result.ProposalStage.MutatesSource == false &&
                result.ProposalStage.AppliesPatch == false,
            ScenarioRunId = scenarioRunId,
            Result = result,
            EvidenceRefs = evidenceRefs,
            IsAdvisoryOnly = true,
            RequiresHumanReview = true,
            MutatesSource = result.ProposalStage?.MutatesSource == true || result.Summary?.MutatesSource == true,
            AppliesPatch = result.ProposalStage?.AppliesPatch == true || result.Summary?.AppliesPatch == true,
            RunsTestsAutomatically = false,
            PromotesMemory = result.ProposalStage?.PromotesMemory == true || result.Summary?.PromotesMemory == true,
            WritesWeaviate = false,
            CreatesPullRequest = result.Summary?.CreatesTicket == true || result.Summary?.SubmitsGitHubReview == true
        };
    }

    private async Task<ManualDogfoodHarnessScenarioResult> RunTestFailureScenario(
        ManualDogfoodHarnessRequest request,
        CancellationToken cancellationToken)
    {
        var scenarioRunId = $"{request.HarnessRunId}-test-failure";
        var result = await _testFailureLoop.RunAsync(BuildTestFailureRequest(request, scenarioRunId), cancellationToken).ConfigureAwait(false);
        var evidenceRefs = CollectEvidenceRefs(result.AuditEnvelope, result.Summary?.EvidenceRefs ?? []);

        return new ManualDogfoodHarnessScenarioResult
        {
            Scenario = ManualDogfoodHarnessScenario.TestFailureRepairProposal,
            Succeeded = result.Succeeded &&
                result.CriticStage?.IsReviewOnly == true &&
                result.ProposalStage?.IsProposalOnly == true &&
                result.ProposalStage?.RequiresSeparateTestRerun == true &&
                result.ProposalStage.RunsTests == false &&
                result.Summary?.IsAdvisoryOnly == true &&
                result.Summary.RunsTests == false,
            ScenarioRunId = scenarioRunId,
            Result = result,
            EvidenceRefs = evidenceRefs,
            IsAdvisoryOnly = true,
            RequiresHumanReview = true,
            MutatesSource = result.ProposalStage?.MutatesSource == true || result.Summary?.MutatesSource == true,
            AppliesPatch = result.ProposalStage?.AppliesPatch == true || result.Summary?.AppliesPatch == true,
            RunsTestsAutomatically = result.ProposalStage?.RunsTests == true || result.Summary?.RunsTests == true,
            PromotesMemory = result.ProposalStage?.PromotesMemory == true || result.Summary?.PromotesMemory == true,
            WritesWeaviate = false,
            CreatesPullRequest = result.ProposalStage?.CreatesPullRequest == true || result.Summary?.CreatesPullRequest == true
        };
    }

    private async Task<ManualDogfoodHarnessScenarioResult> RunMemoryImprovementScenario(
        ManualDogfoodHarnessRequest request,
        IReadOnlyList<ManualDogfoodHarnessScenarioResult> previousScenarios,
        CancellationToken cancellationToken)
    {
        var scenarioRunId = $"{request.HarnessRunId}-memory-improvement";
        var result = await _memoryImprovement.RunAsync(BuildMemoryImprovementRequest(request, scenarioRunId, previousScenarios), cancellationToken).ConfigureAwait(false);
        var evidenceRefs = CollectEvidenceRefs(result.AuditEnvelope, result.Summary?.EvidenceRefs ?? []);

        return new ManualDogfoodHarnessScenarioResult
        {
            Scenario = ManualDogfoodHarnessScenario.RealRunMemoryImprovement,
            Succeeded = result.Succeeded &&
                result.ImprovementStage?.IsProposalOnly == true &&
                result.ImprovementStage.Candidates.All(candidate => candidate.IsProposalOnly && candidate.RequiresHumanReview && !candidate.PromotesMemory && !candidate.CreatesCollectiveMemory && !candidate.WritesWeaviate) &&
                result.Summary?.IsAdvisoryOnly == true,
            ScenarioRunId = scenarioRunId,
            Result = result,
            EvidenceRefs = evidenceRefs,
            IsAdvisoryOnly = true,
            RequiresHumanReview = result.ImprovementStage?.Candidates.Count > 0,
            MutatesSource = false,
            AppliesPatch = false,
            RunsTestsAutomatically = false,
            PromotesMemory = result.ImprovementStage?.PromotesMemory == true || result.Summary?.PromotesMemory == true,
            WritesWeaviate = result.ImprovementStage?.WritesWeaviate == true || result.Summary?.WritesWeaviate == true,
            CreatesPullRequest = false
        };
    }

    private void AddScenarioResult(
        ManualDogfoodHarnessScenarioResult scenario,
        List<ManualDogfoodHarnessScenarioResult> scenarios,
        List<ManualDogfoodHarnessIssue> issues,
        string failureCode)
    {
        scenarios.Add(scenario);
        issues.AddRange(_validator.ValidateScenarioResult(scenario));

        if (!scenario.Succeeded)
            issues.Add(ToIssue(failureCode, $"{scenario.Scenario} did not complete successfully.", scenario.Scenario.ToString()));
    }

    private static ManualTicketReviewFixProposalLoopRequest BuildTicketRequest(
        ManualDogfoodHarnessRequest request,
        string scenarioRunId) =>
        new()
        {
            LoopRunId = scenarioRunId,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            CampaignId = request.CampaignId,
            RequestedByUserId = request.RequestedByUserId,
            RequestedAtUtc = request.RequestedAtUtc,
            PersistToolExecutionAudit = request.PersistToolExecutionAudit,
            Ticket = new ManualTicketReviewTicketInput
            {
                TicketRef = "DOGFOOD-TICKET-001",
                Title = "Dogfood ticket review harness",
                Description = "Exercise manual ticket review to proposal-only fix package.",
                AcceptanceCriteria =
                [
                    "Critic review is review-only.",
                    "Fix proposal is proposal-only.",
                    "No source mutation occurs."
                ],
                EvidenceRefs = [$"dogfood:{scenarioRunId}:ticket"]
            },
            EvidenceBundle = new ManualTicketReviewEvidenceBundle
            {
                Items =
                [
                    new ManualTicketReviewEvidenceItem
                    {
                        EvidenceId = $"evidence-{scenarioRunId}-ticket",
                        RefType = "ManualTicketInput",
                        RefId = "DOGFOOD-TICKET-001",
                        Source = "manual dogfood harness",
                        Summary = "Ticket evidence for manual dogfood harness.",
                        EvidenceRefs = [$"dogfood:{scenarioRunId}:ticket"],
                        SupportsReview = true
                    }
                ]
            }
        };

    private static ManualTestFailureRepairProposalLoopRequest BuildTestFailureRequest(
        ManualDogfoodHarnessRequest request,
        string scenarioRunId) =>
        new()
        {
            LoopRunId = scenarioRunId,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            CampaignId = request.CampaignId,
            RequestedByUserId = request.RequestedByUserId,
            RequestedAtUtc = request.RequestedAtUtc,
            PersistToolExecutionAudit = request.PersistToolExecutionAudit,
            Failure = new ManualTestFailureInput
            {
                FailureRef = "DOGFOOD-TEST-FAILURE-001",
                TestRunRef = "DOGFOOD-TEST-RUN-001",
                TestName = "DogfoodHarness.Tests.ExampleFailure",
                FailureSummary = "Example failure used to prove repair proposal flow.",
                FailureMessage = "Expected governed proposal-only output.",
                StackTraceSummary = "DogfoodHarness.Tests.ExampleFailure:line 42",
                FailedAssertions = ["Expected governed proposal-only output."],
                RelatedFiles = ["IronDev.IntegrationTests/Agents/ManualDogfoodHarnessTests.cs"],
                EvidenceRefs = [$"dogfood:{scenarioRunId}:test-failure"]
            },
            EvidenceBundle = new ManualTestFailureEvidenceBundle
            {
                Items =
                [
                    new ManualTestFailureEvidenceItem
                    {
                        EvidenceId = $"evidence-{scenarioRunId}-test-failure",
                        RefType = "ManualTestFailureInput",
                        RefId = "DOGFOOD-TEST-FAILURE-001",
                        Source = "manual dogfood harness",
                        Summary = "Test failure evidence for manual dogfood harness.",
                        EvidenceRefs = [$"dogfood:{scenarioRunId}:test-failure"],
                        SupportsFailureReview = true,
                        SupportsRepairProposal = true
                    }
                ]
            }
        };

    private static ManualRealRunMemoryImprovementRequest BuildMemoryImprovementRequest(
        ManualDogfoodHarnessRequest request,
        string scenarioRunId,
        IReadOnlyList<ManualDogfoodHarnessScenarioResult> previousScenarios)
    {
        var ticketRef = previousScenarios.FirstOrDefault(item => item.Scenario == ManualDogfoodHarnessScenario.TicketReviewFixProposal)?.ScenarioRunId ?? "dogfood-ticket-review";
        var testRef = previousScenarios.FirstOrDefault(item => item.Scenario == ManualDogfoodHarnessScenario.TestFailureRepairProposal)?.ScenarioRunId ?? "dogfood-test-failure";

        return new ManualRealRunMemoryImprovementRequest
        {
            MemoryImprovementRunId = scenarioRunId,
            TenantId = request.TenantId,
            ProjectId = request.ProjectId,
            CampaignId = request.CampaignId,
            RequestedByUserId = request.RequestedByUserId,
            RequestedAtUtc = request.RequestedAtUtc,
            EvidenceBundle = new RealRunEvidenceBundle
            {
                Items =
                [
                    new RealRunEvidenceItem
                    {
                        EvidenceId = $"evidence-{scenarioRunId}-ticket-loop",
                        RefType = "AgentRunAuditEnvelope",
                        RefId = ticketRef,
                        Source = "manual dogfood harness",
                        Summary = "Dogfood harness repeatedly required explicit proposal-only boundary.",
                        EvidenceRefs = [$"dogfood:{ticketRef}:audit"],
                        SupportsMemoryImprovement = true,
                        IsFromRealRun = true,
                        IsSanitised = true
                    },
                    new RealRunEvidenceItem
                    {
                        EvidenceId = $"evidence-{scenarioRunId}-test-loop",
                        RefType = "AgentRunAuditEnvelope",
                        RefId = testRef,
                        Source = "manual dogfood harness",
                        Summary = "Dogfood harness repeatedly required explicit proposal-only boundary.",
                        EvidenceRefs = [$"dogfood:{testRef}:audit"],
                        SupportsMemoryImprovement = true,
                        IsFromRealRun = true,
                        IsSanitised = true
                    }
                ]
            }
        };
    }

    private static ManualDogfoodHarnessSummary BuildSummary(IReadOnlyList<ManualDogfoodHarnessScenarioResult> scenarios)
    {
        var evidenceRefs = CollectEvidenceRefs(scenarios);
        return new ManualDogfoodHarnessSummary
        {
            Title = "Manual dogfood harness completed",
            Summary = "Manual dogfood harness ran selected backend loops as advisory/proposal-only evidence; human review remains required, no source mutation occurred, and no memory authority changed.",
            ScenarioSummaries = scenarios
                .Select(scenario => $"{scenario.Scenario}: {(scenario.Succeeded ? "succeeded" : "failed safely")}")
                .ToArray(),
            RequiredHumanDecisions =
            [
                "Review ticket fix proposals before any implementation work.",
                "Review test failure repair proposals and rerun tests separately.",
                "Review memory improvement candidates before any governed memory proposal path."
            ],
            RequiredValidation =
            [
                "Validate any patch proposal separately before apply.",
                "Rerun tests separately; the dogfood harness does not rerun tests automatically.",
                "Review memory candidates separately; the dogfood harness does not change memory authority."
            ],
            EvidenceRefs = evidenceRefs,
            IsAdvisoryOnly = true,
            CreatesAuthority = false,
            GrantsApproval = false,
            MutatesSource = false,
            AppliesPatch = false,
            RunsTestsAutomatically = false,
            PromotesMemory = false,
            WritesWeaviate = false,
            CreatesPullRequest = false
        };
    }

    private static Audit.AgentRunAuditEnvelope BuildAuditEnvelope(
        ManualDogfoodHarnessRequest request,
        IReadOnlyList<ManualDogfoodHarnessScenarioResult> scenarios,
        ManualDogfoodHarnessSummary summary)
    {
        var runId = request.HarnessRunId;
        var evidenceRefs = summary.EvidenceRefs;
        var succeeded = scenarios.All(scenario => scenario.Succeeded);
        var status = succeeded ? Audit.AgentRunStatus.Completed : Audit.AgentRunStatus.CompletedWithWarnings;
        var agent = AgentDefinitionCatalog.ReportingAgent;

        return new Audit.AgentRunAuditEnvelope
        {
            Run = new Audit.AgentRunRecord
            {
                AgentRunId = runId,
                TenantId = request.TenantId,
                ProjectId = request.ProjectId,
                CampaignId = request.CampaignId,
                RunId = runId,
                AgentId = agent.AgentId,
                AgentName = agent.Name,
                RequestedByUserId = request.RequestedByUserId,
                TriggerType = Audit.AgentRunTriggerType.TestHarness,
                Status = status,
                RequestSummary = "Manual end-to-end dogfood harness.",
                Purpose = "Prove manual backend dogfood loops can run together safely without runtime authority.",
                CreatedAtUtc = request.RequestedAtUtc,
                StartedAtUtc = request.RequestedAtUtc,
                CompletedAtUtc = request.RequestedAtUtc
            },
            AgentDefinitionSnapshot = agent,
            Inputs = scenarios.Select(scenario => new Audit.AgentRunInputRef
            {
                InputRefId = $"input-{runId}-{scenario.Scenario}",
                AgentRunId = runId,
                RefType = "ManualDogfoodHarnessScenario",
                RefId = scenario.ScenarioRunId,
                Source = "manual dogfood harness",
                Summary = $"{scenario.Scenario} scenario result was used as harness evidence.",
                IsAuthoritativeForAction = false,
                ContainsRawPrivateReasoning = false
            }).ToArray(),
            Outputs =
            [
                new Audit.AgentRunOutputRef
                {
                    OutputRefId = $"output-{runId}-summary",
                    AgentRunId = runId,
                    RefType = nameof(ManualDogfoodHarnessSummary),
                    RefId = $"summary-{runId}",
                    Summary = summary.Summary,
                    IsReviewOnly = false,
                    IsProposalOnly = true,
                    CreatesAuthority = false,
                    CreatesRuntimeAction = false,
                    ContainsRawPrivateReasoning = false,
                    EvidenceRefs = evidenceRefs
                }
            ],
            Steps = BuildSteps(runId, scenarios, evidenceRefs, request.RequestedAtUtc),
            CapabilityUses = BuildCapabilityUses(runId),
            BoundaryDecisions = BuildBoundaryDecisions(runId, scenarios, evidenceRefs),
            ThoughtLedger = BuildThoughtLedger(runId, scenarios, evidenceRefs, request.RequestedAtUtc)
        };
    }

    private static IReadOnlyList<Audit.AgentRunStep> BuildSteps(
        string runId,
        IReadOnlyList<ManualDogfoodHarnessScenarioResult> scenarios,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset occurredAtUtc)
    {
        var steps = new List<Audit.AgentRunStep>
        {
            Step(runId, 1, Audit.AgentRunStepType.Created, "Manual dogfood harness request was created.", evidenceRefs, occurredAtUtc),
            Step(runId, 2, Audit.AgentRunStepType.InputBound, "Selected manual dogfood scenarios were bound.", evidenceRefs, occurredAtUtc)
        };

        var sequence = 3;
        foreach (var scenario in scenarios)
            steps.Add(Step(runId, sequence++, Audit.AgentRunStepType.OutputRecorded, $"{scenario.Scenario} produced safe advisory evidence.", scenario.EvidenceRefs, occurredAtUtc));

        steps.Add(Step(runId, sequence, Audit.AgentRunStepType.Completed, "Manual dogfood harness completed without runtime authority.", evidenceRefs, occurredAtUtc));
        return steps;
    }

    private static Audit.AgentRunStep Step(
        string runId,
        int sequence,
        Audit.AgentRunStepType stepType,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset occurredAtUtc) =>
        new()
        {
            StepId = $"step-{runId}-{sequence:000}",
            AgentRunId = runId,
            Sequence = sequence,
            StepType = stepType,
            OccurredAtUtc = occurredAtUtc,
            Summary = summary,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<Audit.AgentCapabilityUseRecord> BuildCapabilityUses(string runId) =>
        [
            CapabilityUse(runId, AgentCapability.CreateReport, Audit.AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(runId, AgentCapability.RunTool, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.MutateSource, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.CallExternalSystem, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.PromoteCollectiveMemory, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.RepresentHumanApproval, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.RepresentHumanPromotionDecision, Audit.AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.BlockExecution, Audit.AgentCapabilityUseOutcome.Blocked)
        ];

    private static Audit.AgentCapabilityUseRecord CapabilityUse(
        string runId,
        AgentCapability capability,
        Audit.AgentCapabilityUseOutcome outcome)
    {
        var definition = AgentDefinitionCatalog.ReportingAgent;
        return new Audit.AgentCapabilityUseRecord
        {
            CapabilityUseId = $"capability-{runId}-{capability}",
            AgentRunId = runId,
            Capability = capability,
            Outcome = outcome,
            Summary = $"{capability} was {outcome} for manual dogfood harness.",
            PolicyDecisionId = $"policy-{runId}",
            BoundaryDecisionId = $"boundary-{runId}-{capability}",
            EvidenceRef = $"evidence-{runId}",
            WasDeclaredOnAgent = definition.Capabilities?.Contains(capability) == true,
            WasForbiddenOnAgent = definition.ForbiddenCapabilities?.Contains(capability) == true
        };
    }

    private static IReadOnlyList<Audit.AgentBoundaryDecision> BuildBoundaryDecisions(
        string runId,
        IReadOnlyList<ManualDogfoodHarnessScenarioResult> scenarios,
        IReadOnlyList<string> evidenceRefs)
    {
        var decisions = new List<Audit.AgentBoundaryDecision>
        {
            BoundaryDecision(runId, "dogfood-harness-request-validated", Audit.AgentBoundaryDecisionType.Evidence, "allow", "Manual dogfood harness request was validated.", evidenceRefs),
            BoundaryDecision(runId, "human-review-required", Audit.AgentBoundaryDecisionType.Policy, "block", "Human review remains required for proposals and candidates.", evidenceRefs),
            BoundaryDecision(runId, "source-mutation-blocked", Audit.AgentBoundaryDecisionType.Capability, "block", "Source mutation remains unavailable.", evidenceRefs),
            BoundaryDecision(runId, "patch-apply-blocked", Audit.AgentBoundaryDecisionType.Capability, "block", "Patch apply remains unavailable.", evidenceRefs),
            BoundaryDecision(runId, "test-rerun-blocked", Audit.AgentBoundaryDecisionType.Capability, "block", "Automatic test rerun remains unavailable.", evidenceRefs),
            BoundaryDecision(runId, "memory-promotion-blocked", Audit.AgentBoundaryDecisionType.Capability, "block", "Memory authority changes remain unavailable.", evidenceRefs),
            BoundaryDecision(runId, "index-write-blocked", Audit.AgentBoundaryDecisionType.Capability, "block", "Vector index writes remain unavailable.", evidenceRefs),
            BoundaryDecision(runId, "api-cli-runtime-blocked", Audit.AgentBoundaryDecisionType.Safety, "block", "API, CLI, runtime, scheduler, and orchestrator wiring remain unavailable.", evidenceRefs)
        };

        if (scenarios.Any(scenario => scenario.Scenario == ManualDogfoodHarnessScenario.TicketReviewFixProposal))
            decisions.Add(BoundaryDecision(runId, "ticket-review-loop-proposal-only", Audit.AgentBoundaryDecisionType.OutputValidation, "allow", "Ticket review loop produced review-only critic findings and proposal-only fix package.", evidenceRefs));

        if (scenarios.Any(scenario => scenario.Scenario == ManualDogfoodHarnessScenario.TestFailureRepairProposal))
            decisions.Add(BoundaryDecision(runId, "test-failure-loop-proposal-only", Audit.AgentBoundaryDecisionType.OutputValidation, "allow", "Test failure loop produced review-only critic findings and proposal-only repair package.", evidenceRefs));

        if (scenarios.Any(scenario => scenario.Scenario == ManualDogfoodHarnessScenario.RealRunMemoryImprovement))
            decisions.Add(BoundaryDecision(runId, "real-run-memory-candidates-proposal-only", Audit.AgentBoundaryDecisionType.OutputValidation, "allow", "Real-run evidence produced proposal-only memory improvement candidates.", evidenceRefs));

        return decisions;
    }

    private static Audit.AgentBoundaryDecision BoundaryDecision(
        string runId,
        string suffix,
        Audit.AgentBoundaryDecisionType boundaryType,
        string decision,
        string reason,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            BoundaryDecisionId = $"boundary-{runId}-{suffix}",
            AgentRunId = runId,
            BoundaryType = boundaryType,
            Decision = decision,
            Reason = reason,
            SourceRefId = $"manual-dogfood-harness-{suffix}",
            GrantsAuthority = false,
            GrantsHumanApproval = false,
            GrantsPolicyApproval = false,
            GrantsMemoryPromotion = false,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<AuditThoughtLedgerEntry> BuildThoughtLedger(
        string runId,
        IReadOnlyList<ManualDogfoodHarnessScenarioResult> scenarios,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset recordedAtUtc)
    {
        var entries = new List<AuditThoughtLedgerEntry>
        {
            Thought(runId, "request-validated", Audit.ThoughtLedgerEntryType.EvidenceUsed, "Manual dogfood harness request was validated.", evidenceRefs, recordedAtUtc)
        };

        if (scenarios.Any(scenario => scenario.Scenario == ManualDogfoodHarnessScenario.TicketReviewFixProposal))
            entries.Add(Thought(runId, "ticket-loop", Audit.ThoughtLedgerEntryType.OutputRationale, "Ticket review loop produced review-only critic findings and proposal-only fix package.", evidenceRefs, recordedAtUtc));

        if (scenarios.Any(scenario => scenario.Scenario == ManualDogfoodHarnessScenario.TestFailureRepairProposal))
            entries.Add(Thought(runId, "test-failure-loop", Audit.ThoughtLedgerEntryType.OutputRationale, "Test failure loop produced review-only critic findings and proposal-only repair package.", evidenceRefs, recordedAtUtc));

        if (scenarios.Any(scenario => scenario.Scenario == ManualDogfoodHarnessScenario.RealRunMemoryImprovement))
            entries.Add(Thought(runId, "memory-candidates", Audit.ThoughtLedgerEntryType.OutputRationale, "Real run evidence produced proposal-only memory improvement candidates.", evidenceRefs, recordedAtUtc));

        entries.Add(Thought(runId, "human-review", Audit.ThoughtLedgerEntryType.Assumption, "Human review remains required.", evidenceRefs, recordedAtUtc));
        entries.Add(Thought(runId, "no-mutation", Audit.ThoughtLedgerEntryType.BoundaryDecision, "No patch was applied, no source was mutated, no tests were rerun automatically, no memory authority changed, and no vector index write occurred.", evidenceRefs, recordedAtUtc));
        entries.Add(Thought(runId, "no-runtime", Audit.ThoughtLedgerEntryType.RejectedAlternative, "No API, CLI, runtime, scheduler, or orchestrator was added.", evidenceRefs, recordedAtUtc));

        return entries;
    }

    private static AuditThoughtLedgerEntry Thought(
        string runId,
        string suffix,
        Audit.ThoughtLedgerEntryType entryType,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset recordedAtUtc) =>
        new()
        {
            ThoughtLedgerEntryId = $"thought-{runId}-{suffix}",
            AgentRunId = runId,
            EntryType = entryType,
            Summary = summary,
            EvidenceRefs = evidenceRefs,
            RecordedAtUtc = recordedAtUtc,
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false
        };

    private static IReadOnlyList<string> CollectEvidenceRefs(Audit.AgentRunAuditEnvelope? envelope, IReadOnlyList<string> extraRefs)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);
        if (envelope is not null)
        {
            refs.Add(envelope.Run.AgentRunId);

            foreach (var input in envelope.Inputs)
                refs.Add(input.RefId);

            foreach (var output in envelope.Outputs)
            {
                refs.Add(output.RefId);
                foreach (var evidenceRef in output.EvidenceRefs)
                    refs.Add(evidenceRef);
            }
        }

        foreach (var extraRef in extraRefs)
            refs.Add(extraRef);

        return refs.Where(value => !string.IsNullOrWhiteSpace(value)).Order(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<string> CollectEvidenceRefs(IReadOnlyList<ManualDogfoodHarnessScenarioResult> scenarios)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scenario in scenarios)
        {
            refs.Add(scenario.ScenarioRunId);
            foreach (var evidenceRef in scenario.EvidenceRefs)
                refs.Add(evidenceRef);
        }

        return refs.Where(value => !string.IsNullOrWhiteSpace(value)).Order(StringComparer.Ordinal).ToArray();
    }

    private static ManualDogfoodHarnessIssue ToIssue(string code, string message, string field) =>
        new()
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        };

    private static ManualDogfoodHarnessResult Failed(
        string runId,
        ManualDogfoodHarnessStatus status,
        IReadOnlyList<ManualDogfoodHarnessIssue> issues,
        IReadOnlyList<ManualDogfoodHarnessScenarioResult>? scenarios = null,
        ManualDogfoodHarnessSummary? summary = null,
        Audit.AgentRunAuditEnvelope? auditEnvelope = null) =>
        new()
        {
            Succeeded = false,
            Status = status,
            HarnessRunId = runId,
            Scenarios = scenarios ?? [],
            Summary = summary,
            AuditEnvelope = auditEnvelope,
            Issues = issues
        };
}
