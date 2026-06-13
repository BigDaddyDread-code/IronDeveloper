using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowFailureRetryState")]
public sealed class WorkflowFailureRetryStateTests
{
    private static readonly Guid ProjectId = Guid.Parse("10000000-eeee-4444-8888-990000000001");
    private static readonly Guid WorkflowRunId = Guid.Parse("20000000-eeee-4444-8888-990000000001");
    private static readonly Guid WorkflowRunStepId = Guid.Parse("30000000-eeee-4444-8888-990000000001");
    private readonly WorkflowFailureRetryStateValidator _validator = new();

    [TestMethod]
    public void WorkflowFailureState_ExposesExpectedShape()
    {
        var properties = typeof(WorkflowFailureState).GetProperties().Select(property => property.Name).ToArray();

        CollectionAssert.IsSubsetOf(
            new[]
            {
                "WorkflowFailureStateId",
                "WorkflowRunId",
                "WorkflowRunStepId",
                "WorkflowCheckpointId",
                "ProjectId",
                "FailureKey",
                "FailureType",
                "Severity",
                "Status",
                "EvidenceReferences",
                "GroundingReferences",
                "RetriesWorkflow",
                "GrantsApproval",
                "GrantsExecution",
                "MutatesSource",
                "PromotesMemory",
                "ContinuesWorkflow",
                "ResumesWorkflow",
                "SatisfiesPolicy",
                "TransfersAuthority",
                "ApprovesRelease",
                "CreatesAcceptedMemory"
            },
            properties);
    }

    [TestMethod]
    public void WorkflowRetryState_ExposesExpectedShape()
    {
        var properties = typeof(WorkflowRetryState).GetProperties().Select(property => property.Name).ToArray();

        CollectionAssert.IsSubsetOf(
            new[]
            {
                "WorkflowRetryStateId",
                "WorkflowRunId",
                "WorkflowRunStepId",
                "WorkflowCheckpointId",
                "WorkflowFailureStateId",
                "ProjectId",
                "RetryKey",
                "Status",
                "Disposition",
                "Recommendation",
                "AttemptNumber",
                "MaxAttempts",
                "EvidenceReferences",
                "GroundingReferences",
                "RetriesWorkflow",
                "GrantsApproval",
                "GrantsExecution",
                "MutatesSource",
                "PromotesMemory",
                "ContinuesWorkflow",
                "ResumesWorkflow",
                "SatisfiesPolicy",
                "TransfersAuthority",
                "ApprovesRelease",
                "CreatesAcceptedMemory"
            },
            properties);
    }

    [TestMethod]
    public void WorkflowFailureRetryState_UsesBoundedFailureTypes()
    {
        var names = Enum.GetNames<WorkflowFailureType>();
        CollectionAssert.AreEquivalent(
            new[]
            {
                "ValidationFailure",
                "PolicyBoundaryFailure",
                "MissingEvidence",
                "InvalidEvidence",
                "ToolGateBlocked",
                "ApprovalRequired",
                "HumanReviewRequired",
                "TimeoutObserved",
                "ExternalDependencyUnavailable",
                "DataShapeMismatch",
                "InvariantViolation",
                "ConfigurationMissing",
                "StorageFailure",
                "BuildFailure",
                "TestFailure",
                "ReviewFinding",
                "UserCancelled",
                "UnknownSafeFailure"
            },
            names);

        AssertNoForbiddenTokens(string.Join("\n", names), "ToolExecuted", "AgentDispatched", "WorkflowContinued", "WorkflowResumed", "SourceApplied", "MemoryPromoted", "AcceptedMemoryCreated", "ReleaseApproved", "ApprovalSatisfied", "PolicyActivated", "AuthorityTransferred", "RuntimeFrameCaptured", "LangGraphStateCaptured", "RawModelFailure", "RawToolOutputFailure");
    }

    [TestMethod]
    public void WorkflowFailureRetryState_UsesBoundedSeveritiesStatusesDispositionsAndRecommendations()
    {
        CollectionAssert.AreEquivalent(new[] { "Info", "Warning", "Blocked", "Failed", "Critical" }, Enum.GetNames<WorkflowFailureSeverity>());
        CollectionAssert.AreEquivalent(new[] { "Recorded", "ReadyForReview", "Blocked", "Superseded", "Cancelled", "Rejected" }, Enum.GetNames<WorkflowFailureStatus>());
        CollectionAssert.AreEquivalent(new[] { "Recorded", "ReadyForReview", "Blocked", "Superseded", "Cancelled", "Rejected" }, Enum.GetNames<WorkflowRetryStatus>());
        CollectionAssert.AreEquivalent(
            new[]
            {
                "NoRetryRecommended",
                "RetryMayBeReviewed",
                "RetryRequiresHumanReview",
                "RetryRequiresPolicyEvaluation",
                "RetryRequiresMoreEvidence",
                "RetryBlockedByPolicy",
                "RetryBlockedByMissingApproval",
                "RetryBlockedByMissingEvidence",
                "ManualInvestigationRequired"
            },
            Enum.GetNames<WorkflowRetryDisposition>());
        CollectionAssert.AreEquivalent(
            new[] { "DoNotRetry", "ReviewBeforeRetry", "CollectMoreEvidence", "RequestHumanDecision", "EvaluatePolicy", "CreateFollowUpTicket", "MarkBlockedForReview" },
            Enum.GetNames<WorkflowRetryRecommendation>());

        var combined = string.Join("\n", Enum.GetNames<WorkflowFailureSeverity>()
            .Concat(Enum.GetNames<WorkflowFailureStatus>())
            .Concat(Enum.GetNames<WorkflowRetryStatus>())
            .Concat(Enum.GetNames<WorkflowRetryDisposition>())
            .Concat(Enum.GetNames<WorkflowRetryRecommendation>()));
        AssertNoForbiddenTokens(combined, "Approved", "Authorized", "Executable", "RetryAllowed", "RetryNow", "AutoRetry", "RetryScheduled", "RetryQueued", "RetryExecuted", "ResumeAllowed", "WorkflowResumed", "WorkflowContinued", "ReleaseReady", "PolicySatisfied", "SourceApplied", "MemoryPromoted", "ReleaseApproved");
    }

    [TestMethod]
    public void WorkflowFailureRetryState_AllowsBoundedFailureAndRetryVocabularies()
    {
        foreach (var failureType in Enum.GetValues<WorkflowFailureType>())
            AssertValid(_validator.ValidateFailure(ValidFailure() with { FailureType = failureType }));
        foreach (var severity in Enum.GetValues<WorkflowFailureSeverity>())
            AssertValid(_validator.ValidateFailure(ValidFailure() with { Severity = severity }));
        foreach (var status in Enum.GetValues<WorkflowFailureStatus>())
            AssertValid(_validator.ValidateFailure(ValidFailure() with { Status = status }));
        foreach (var status in Enum.GetValues<WorkflowRetryStatus>())
            AssertValid(_validator.ValidateRetry(ValidRetry() with { Status = status }));
        foreach (var disposition in Enum.GetValues<WorkflowRetryDisposition>())
            AssertValid(_validator.ValidateRetry(ValidRetry() with { Disposition = disposition }));
        foreach (var recommendation in Enum.GetValues<WorkflowRetryRecommendation>())
            AssertValid(_validator.ValidateRetry(ValidRetry() with { Recommendation = recommendation }));
    }

    [TestMethod]
    public void ValidateFailure_RejectsRequiredAndVocabularyFailures()
    {
        AssertInvalid(_validator.ValidateFailure(ValidFailure() with { ProjectId = Guid.Empty }), "WORKFLOW_FAILURE_STATE_PROJECT_ID_REQUIRED");
        AssertInvalid(_validator.ValidateFailure(ValidFailure() with { WorkflowRunId = Guid.Empty }), "WORKFLOW_FAILURE_STATE_RUN_ID_REQUIRED");
        AssertInvalid(_validator.ValidateFailure(ValidFailure() with { FailureKey = " " }), "WORKFLOW_FAILURE_STATE_KEY_REQUIRED");
        AssertInvalid(_validator.ValidateFailure(ValidFailure() with { FailureType = (WorkflowFailureType)999 }), "WORKFLOW_FAILURE_TYPE_INVALID");
        AssertInvalid(_validator.ValidateFailure(ValidFailure() with { Severity = (WorkflowFailureSeverity)999 }), "WORKFLOW_FAILURE_SEVERITY_INVALID");
        AssertInvalid(_validator.ValidateFailure(ValidFailure() with { Status = (WorkflowFailureStatus)999 }), "WORKFLOW_FAILURE_STATUS_INVALID");
        AssertInvalid(_validator.ValidateFailure(ValidFailure() with { EvidenceReferences = [ValidEvidence() with { AllowedUse = "Approval" }] }), "WORKFLOW_FAILURE_EVIDENCE_ALLOWED_USE_INVALID");
    }

    [TestMethod]
    public void ValidateRetry_RejectsRequiredVocabularyAndAttemptFailures()
    {
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { ProjectId = Guid.Empty }), "WORKFLOW_RETRY_STATE_PROJECT_ID_REQUIRED");
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { WorkflowRunId = Guid.Empty }), "WORKFLOW_RETRY_STATE_RUN_ID_REQUIRED");
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { RetryKey = " " }), "WORKFLOW_RETRY_STATE_KEY_REQUIRED");
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { Status = (WorkflowRetryStatus)999 }), "WORKFLOW_RETRY_STATUS_INVALID");
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { Disposition = (WorkflowRetryDisposition)999 }), "WORKFLOW_RETRY_DISPOSITION_INVALID");
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { Recommendation = (WorkflowRetryRecommendation)999 }), "WORKFLOW_RETRY_RECOMMENDATION_INVALID");
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { AttemptNumber = -1 }), "WORKFLOW_RETRY_ATTEMPT_INVALID");
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { AttemptNumber = 3, MaxAttempts = 2 }), "WORKFLOW_RETRY_MAX_ATTEMPTS_INVALID");
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { EarliestRetryUtc = DateTimeOffset.UtcNow.AddHours(2), RetryAfterUtc = DateTimeOffset.UtcNow.AddHours(1) }), "WORKFLOW_RETRY_TIMESTAMPS_INVALID");
    }

    [TestMethod]
    public void WorkflowFailureRetryState_RejectsPrivateRawRetryRuntimeAndAuthorityMarkers()
    {
        foreach (var marker in new[] { "hiddenReasoning", "chainOfThought", "rawPrompt", "rawCompletion", "rawToolOutput", "entirePatch" })
        {
            AssertInvalid(_validator.ValidateFailure(ValidFailure() with { SafeSummary = marker }), "WORKFLOW_FAILURE_RETRY_PRIVATE_REASONING_BLOCKED");
            AssertInvalid(_validator.ValidateRetry(ValidRetry() with { SafeSummary = marker }), "WORKFLOW_FAILURE_RETRY_PRIVATE_REASONING_BLOCKED");
        }

        foreach (var marker in new[] { "approval granted", "execution allowed", "policy satisfied", "source applied", "memory promoted", "accepted memory", "release approved", "workflow continued", "resume workflow", "retry now", "auto retry", "retry scheduled", "retry executed", "dispatch agent", "tool executed", "model output", "runtime frame", "LangGraph state" })
        {
            AssertInvalid(_validator.ValidateFailure(ValidFailure() with { SafeSummary = marker }), "WORKFLOW_FAILURE_RETRY_AUTHORITY_OR_RUNTIME_LANGUAGE_BLOCKED");
            AssertInvalid(_validator.ValidateRetry(ValidRetry() with { SafeSummary = marker }), "WORKFLOW_FAILURE_RETRY_AUTHORITY_OR_RUNTIME_LANGUAGE_BLOCKED");
        }
    }

    [TestMethod]
    public void WorkflowFailureRetryState_RejectsUnsafeMetadataAndTrueAuthorityFlags()
    {
        AssertInvalid(_validator.ValidateFailure(ValidFailure() with { MetadataJson = "{\"rawPrompt\":\"no\"}" }), "WORKFLOW_FAILURE_RETRY_PRIVATE_REASONING_BLOCKED");
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { MetadataJson = "{\"rawCompletion\":\"no\"}" }), "WORKFLOW_FAILURE_RETRY_PRIVATE_REASONING_BLOCKED");
        AssertInvalid(_validator.ValidateFailure(ValidFailure() with { MetadataJson = "{\"retryNow\":true}" }), "WORKFLOW_FAILURE_RETRY_AUTHORITY_METADATA_BLOCKED");
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { MetadataJson = "{\"workflowResumed\":true}" }), "WORKFLOW_FAILURE_RETRY_AUTHORITY_METADATA_BLOCKED");
        AssertInvalid(_validator.ValidateFailure(ValidFailure() with { GrantsExecution = true }), "WORKFLOW_FAILURE_RETRY_AUTHORITY_FLAG_BLOCKED");
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { RetriesWorkflow = true }), "WORKFLOW_FAILURE_RETRY_AUTHORITY_FLAG_BLOCKED");
        AssertInvalid(_validator.ValidateRetry(ValidRetry() with { CreatesAcceptedMemory = true }), "WORKFLOW_FAILURE_RETRY_AUTHORITY_FLAG_BLOCKED");
    }

    [TestMethod]
    public void WorkflowFailureRetryState_AuthorityFlagsAreAlwaysFalse()
    {
        var failure = MaterializedFailure();
        var retry = MaterializedRetry();

        AssertAllFalse(
            failure.GrantsApproval,
            failure.GrantsExecution,
            failure.MutatesSource,
            failure.PromotesMemory,
            failure.StartsWorkflow,
            failure.ContinuesWorkflow,
            failure.ResumesWorkflow,
            failure.RetriesWorkflow,
            failure.SatisfiesPolicy,
            failure.TransfersAuthority,
            failure.ApprovesRelease,
            failure.CreatesAcceptedMemory,
            retry.GrantsApproval,
            retry.GrantsExecution,
            retry.MutatesSource,
            retry.PromotesMemory,
            retry.StartsWorkflow,
            retry.ContinuesWorkflow,
            retry.ResumesWorkflow,
            retry.RetriesWorkflow,
            retry.SatisfiesPolicy,
            retry.TransfersAuthority,
            retry.ApprovesRelease,
            retry.CreatesAcceptedMemory);
    }

    [TestMethod]
    public void WorkflowFailureRetryState_DoesNotExposeRetryExecuteContinueResumeMethods()
    {
        var methods = string.Join("\n", typeof(WorkflowFailureRetryStateValidator).GetMethods().Select(method => method.Name));

        AssertNoForbiddenTokens(methods, "Execute", "Dispatch", "Continue", "Resume", "RetryNow", "RetryWorkflow", "ScheduleRetry", "QueueRetry", "Approve", "ApplySource", "PromoteMemory", "ApproveRelease", "SatisfyPolicy");
    }

    [TestMethod]
    public void WorkflowFailureRetryState_DoesNotAddRuntimeStorageOrApiSurface()
    {
        var root = RepositoryRoot();
        var productionText = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Workflow", "WorkflowFailureRetryStateModels.cs")) +
                             File.ReadAllText(Path.Combine(root, "Docs", "BLOCK_J_DURABLE_WORKFLOW_RUN_SUBSTRATE.md"));

        AssertNoForbiddenTokens(
            productionText,
            "CREATE TABLE",
            "CREATE PROCEDURE",
            "SqlWorkflowFailure",
            "IWorkflowFailureRetryStateStore",
            "IHostedService",
            "BackgroundService",
            "ControllerBase",
            "WebApplication",
            "WorkflowRunner",
            "RetryRunner",
            "RetryScheduler",
            "WorkflowResumeEngine",
            "WorkflowRestoreEngine",
            "LangGraphRuntime",
            "A2aRuntime",
            "MessageBus",
            "MessageQueue",
            "QueueClient",
            "ToolExecutor",
            "ModelClient",
            "ApplySource",
            "PromoteMemory",
            "ApproveRelease");
    }

    private static WorkflowFailureStateCreateRequest ValidFailure() =>
        new()
        {
            WorkflowFailureStateId = Guid.NewGuid(),
            WorkflowRunId = WorkflowRunId,
            WorkflowRunStepId = WorkflowRunStepId,
            WorkflowCheckpointId = Guid.NewGuid(),
            ProjectId = ProjectId,
            FailureKey = "validation-failure",
            FailureType = WorkflowFailureType.ValidationFailure,
            Severity = WorkflowFailureSeverity.Blocked,
            Status = WorkflowFailureStatus.Recorded,
            SubjectType = "workflow_step",
            SubjectId = "step-001",
            SafeSummary = "Failure state records validation evidence for review only.",
            EvidenceReferences = [ValidEvidence()],
            GroundingReferences = [ValidGrounding()],
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "workflow-failure-retry-tests",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"workflow.failure.retry.metadata.v1\",\"notes\":\"Failure recorded for human review.\",\"grantsApproval\":false,\"grantsExecution\":false,\"mutatesSource\":false,\"promotesMemory\":false,\"startsWorkflow\":false,\"continuesWorkflow\":false,\"resumesWorkflow\":false,\"retriesWorkflow\":false,\"satisfiesPolicy\":false,\"transfersAuthority\":false,\"approvesRelease\":false,\"createsAcceptedMemory\":false}"
        };

    private static WorkflowRetryStateCreateRequest ValidRetry() =>
        new()
        {
            WorkflowRetryStateId = Guid.NewGuid(),
            WorkflowRunId = WorkflowRunId,
            WorkflowRunStepId = WorkflowRunStepId,
            WorkflowCheckpointId = Guid.NewGuid(),
            WorkflowFailureStateId = Guid.NewGuid(),
            ProjectId = ProjectId,
            RetryKey = "retry-review",
            Status = WorkflowRetryStatus.Recorded,
            Disposition = WorkflowRetryDisposition.RetryMayBeReviewed,
            Recommendation = WorkflowRetryRecommendation.ReviewBeforeRetry,
            AttemptNumber = 1,
            MaxAttempts = 3,
            EarliestRetryUtc = DateTimeOffset.UtcNow.AddHours(1),
            RetryAfterUtc = DateTimeOffset.UtcNow.AddHours(2),
            SubjectType = "workflow_step",
            SubjectId = "step-001",
            SafeSummary = "Retry state records review recommendation only.",
            EvidenceReferences = [ValidEvidence() with { AllowedUse = WorkflowFailureRetryAllowedUses.RetryExplanation }],
            GroundingReferences = [ValidGrounding()],
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "workflow-failure-retry-tests",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"workflow.failure.retry.metadata.v1\",\"notes\":\"Retry state is review-only.\",\"grantsApproval\":false,\"grantsExecution\":false,\"mutatesSource\":false,\"promotesMemory\":false,\"startsWorkflow\":false,\"continuesWorkflow\":false,\"resumesWorkflow\":false,\"retriesWorkflow\":false,\"satisfiesPolicy\":false,\"transfersAuthority\":false,\"approvesRelease\":false,\"createsAcceptedMemory\":false}"
        };

    private static WorkflowFailureEvidenceReference ValidEvidence() =>
        new()
        {
            EvidenceType = "ValidationOutput",
            EvidenceId = "validation-output-001",
            EvidenceLabel = "Validation output",
            SafeSummary = "Evidence supports failure review only.",
            AllowedUse = WorkflowFailureRetryAllowedUses.FailureExplanation,
            GovernanceEventId = Guid.NewGuid(),
            WorkflowRunEvidenceReferenceId = Guid.NewGuid(),
            WorkflowRunStepId = WorkflowRunStepId,
            WorkflowCheckpointId = Guid.NewGuid()
        };

    private static WorkflowFailureGroundingReference ValidGrounding() =>
        new()
        {
            GroundingEvidenceReferenceId = Guid.NewGuid(),
            ClaimType = "EvidenceSupport",
            ClaimId = "claim-001",
            SafeSummary = "Grounding supports failure and retry review only."
        };

    private static WorkflowFailureState MaterializedFailure() =>
        new()
        {
            WorkflowFailureStateId = Guid.NewGuid(),
            WorkflowRunId = WorkflowRunId,
            WorkflowRunStepId = WorkflowRunStepId,
            WorkflowCheckpointId = Guid.NewGuid(),
            ProjectId = ProjectId,
            FailureKey = "validation-failure",
            FailureType = WorkflowFailureType.ValidationFailure,
            Severity = WorkflowFailureSeverity.Blocked,
            Status = WorkflowFailureStatus.Recorded,
            SafeSummary = "Failure state records validation evidence for review only.",
            EvidenceReferences = [ValidEvidence()],
            GroundingReferences = [ValidGrounding()],
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "workflow-failure-retry-tests",
            MetadataVersion = 1,
            MetadataJson = "{}",
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            ContinuesWorkflow = false,
            ResumesWorkflow = false,
            RetriesWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            ApprovesRelease = false,
            CreatesAcceptedMemory = false,
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static WorkflowRetryState MaterializedRetry() =>
        new()
        {
            WorkflowRetryStateId = Guid.NewGuid(),
            WorkflowRunId = WorkflowRunId,
            WorkflowRunStepId = WorkflowRunStepId,
            WorkflowCheckpointId = Guid.NewGuid(),
            WorkflowFailureStateId = Guid.NewGuid(),
            ProjectId = ProjectId,
            RetryKey = "retry-review",
            Status = WorkflowRetryStatus.Recorded,
            Disposition = WorkflowRetryDisposition.RetryMayBeReviewed,
            Recommendation = WorkflowRetryRecommendation.ReviewBeforeRetry,
            AttemptNumber = 1,
            MaxAttempts = 3,
            SafeSummary = "Retry state records review recommendation only.",
            EvidenceReferences = [ValidEvidence()],
            GroundingReferences = [ValidGrounding()],
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "workflow-failure-retry-tests",
            MetadataVersion = 1,
            MetadataJson = "{}",
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            ContinuesWorkflow = false,
            ResumesWorkflow = false,
            RetriesWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            ApprovesRelease = false,
            CreatesAcceptedMemory = false,
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static void AssertValid(WorkflowRunValidationResult result) =>
        Assert.IsTrue(result.IsValid, string.Join("; ", result.Issues.Select(issue => issue.Code + ":" + issue.Message)));

    private static void AssertInvalid(WorkflowRunValidationResult result, string expectedCode)
    {
        Assert.IsFalse(result.IsValid);
        Assert.IsTrue(result.Issues.Any(issue => string.Equals(issue.Code, expectedCode, StringComparison.Ordinal)), $"Expected issue code {expectedCode}. Actual: {string.Join(", ", result.Issues.Select(issue => issue.Code))}");
    }

    private static void AssertNoForbiddenTokens(string text, params string[] forbidden)
    {
        foreach (var token in forbidden)
            Assert.IsFalse(text.Contains(token, StringComparison.OrdinalIgnoreCase), $"Forbidden token found: {token}");
    }

    private static void AssertAllFalse(params bool[] values)
    {
        foreach (var value in values)
            Assert.IsFalse(value);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IronDev.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
