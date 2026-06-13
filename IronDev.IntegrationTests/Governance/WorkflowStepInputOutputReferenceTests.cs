using IronDev.Core.Workflow;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace IronDev.IntegrationTests.Governance;

[TestClass]
[TestCategory("WorkflowStepInputOutputReference")]
public sealed class WorkflowStepInputOutputReferenceTests
{
    private static readonly Guid ProjectId = Guid.Parse("10000000-dddd-4444-8888-990000000001");
    private static readonly Guid WorkflowRunId = Guid.Parse("20000000-dddd-4444-8888-990000000001");
    private static readonly Guid WorkflowRunStepId = Guid.Parse("30000000-dddd-4444-8888-990000000001");
    private readonly WorkflowStepInputOutputReferenceValidator _validator = new();

    [TestMethod]
    public void WorkflowStepInputReference_ExposesExpectedShape()
    {
        var properties = typeof(WorkflowStepInputReference).GetProperties().Select(property => property.Name).ToArray();

        CollectionAssert.IsSubsetOf(
            new[]
            {
                "WorkflowStepInputReferenceId",
                "WorkflowRunId",
                "WorkflowRunStepId",
                "ProjectId",
                "InputKey",
                "InputType",
                "Status",
                "AllowedUses",
                "MetadataJson",
                "GrantsApproval",
                "GrantsExecution",
                "MutatesSource",
                "PromotesMemory",
                "StartsWorkflow",
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
    public void WorkflowStepOutputReference_ExposesExpectedShape()
    {
        var properties = typeof(WorkflowStepOutputReference).GetProperties().Select(property => property.Name).ToArray();

        CollectionAssert.IsSubsetOf(
            new[]
            {
                "WorkflowStepOutputReferenceId",
                "WorkflowRunId",
                "WorkflowRunStepId",
                "ProjectId",
                "OutputKey",
                "OutputType",
                "Status",
                "AllowedUses",
                "MetadataJson",
                "GrantsApproval",
                "GrantsExecution",
                "MutatesSource",
                "PromotesMemory",
                "StartsWorkflow",
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
    public void WorkflowStepInputOutputReference_UsesBoundedInputTypes()
    {
        var names = Enum.GetNames<WorkflowStepInputReferenceType>();
        CollectionAssert.AreEquivalent(
            new[]
            {
                "WorkflowRunFact",
                "WorkflowStepFact",
                "WorkflowCheckpointFact",
                "EvidenceReference",
                "GroundingReference",
                "ThoughtLedgerReference",
                "HumanNote",
                "ValidationFinding",
                "ReviewFinding",
                "DebugFinding",
                "PolicyEvaluationInput",
                "ApprovalRequirementInput",
                "HandoffContext",
                "SourceFileRangeReference",
                "MemoryCandidateReference"
            },
            names);

        AssertDoesNotContainAny(names, "RawPrompt", "RawCompletion", "RawToolOutput", "HiddenReasoning", "ExecutableCommand", "ToolExecutionInput", "AgentDispatchInput", "WorkflowResumeInput", "LangGraphState");
    }

    [TestMethod]
    public void WorkflowStepInputOutputReference_UsesBoundedOutputTypes()
    {
        var names = Enum.GetNames<WorkflowStepOutputReferenceType>();
        CollectionAssert.AreEquivalent(
            new[]
            {
                "WorkflowStepFact",
                "WorkflowCheckpointFact",
                "EvidenceReference",
                "GroundingReference",
                "ThoughtLedgerReference",
                "ValidationFinding",
                "ReviewFinding",
                "DebugFinding",
                "PolicyEvaluationFinding",
                "ApprovalRequirementFinding",
                "HumanDecisionSupportOutput",
                "HandoffSummary",
                "MemoryCandidateOutput",
                "SourceApplyCandidateOutput",
                "Receipt"
            },
            names);

        AssertDoesNotContainAny(names, "ToolExecutionOutput", "ModelRawOutput", "RawCompletion", "RawToolOutput", "SourceMutation", "SourceApplied", "MemoryPromoted", "AcceptedMemoryCreated", "ReleaseApproved", "WorkflowContinued", "WorkflowResumed");
    }

    [TestMethod]
    public void WorkflowStepInputOutputReference_UsesBoundedStatuses()
    {
        var names = Enum.GetNames<WorkflowStepReferenceStatus>();
        CollectionAssert.AreEquivalent(new[] { "Recorded", "Captured", "ReadyForReview", "Superseded", "Cancelled", "Rejected" }, names);
        AssertDoesNotContainAny(names, "Consumed", "Produced", "Executed", "Dispatched", "Approved", "Authorized", "ExecutionAllowed", "PolicySatisfied", "WorkflowContinued", "WorkflowResumed", "SourceApplied", "MemoryPromoted", "AcceptedMemoryCreated", "ReleaseApproved", "Restorable", "ResumeAllowed");
    }

    [TestMethod]
    public void WorkflowStepInputOutputReference_UsesBoundedAllowedUses()
    {
        var allowedUses = WorkflowStepReferenceAllowedUses.All.ToArray();

        CollectionAssert.AreEquivalent(
            new[]
            {
                "Context",
                "Review",
                "Debugging",
                "Validation",
                "Traceability",
                "HumanDecisionSupport",
                "AuditReference",
                "PolicyInput",
                "RequirementEvaluation",
                "ClaimSupport",
                "CheckpointExplanation",
                "StepExplanation"
            },
            allowedUses);

        AssertDoesNotContainAny(allowedUses, "Approval", "ExecutionPermission", "CanExecute", "WorkflowContinuation", "WorkflowResume", "SourceApplyPermission", "MemoryPromotionPermission", "AcceptedMemory", "ReleaseApproval", "CanShip", "AuthorityTransfer", "ToolExecution", "AgentDispatch");
    }

    [TestMethod]
    public void WorkflowStepInputOutputReference_DoesNotExposeExecutionOrContinuationMethods()
    {
        var methodNames = string.Join(
            "\n",
            typeof(WorkflowStepInputOutputReferenceValidator).GetMethods().Select(method => method.Name));

        AssertNoForbiddenTokens(methodNames, "ConsumeInput", "ProduceOutput", "Execute", "Dispatch", "Continue", "Resume", "Approve", "ApplySource", "PromoteMemory", "ApproveRelease", "SatisfyPolicy", "CreateAcceptedMemory");
    }

    [TestMethod]
    public void WorkflowStepInputOutputReference_AllowsBoundedInputTypes()
    {
        foreach (var inputType in Enum.GetValues<WorkflowStepInputReferenceType>())
            AssertValid(_validator.ValidateInput(ValidInput() with { InputType = inputType }));
    }

    [TestMethod]
    public void WorkflowStepInputOutputReference_AllowsBoundedOutputTypes()
    {
        foreach (var outputType in Enum.GetValues<WorkflowStepOutputReferenceType>())
            AssertValid(_validator.ValidateOutput(ValidOutput() with { OutputType = outputType }));
    }

    [TestMethod]
    public void WorkflowStepInputOutputReference_AllowsBoundedStatusesAndAllowedUses()
    {
        foreach (var status in Enum.GetValues<WorkflowStepReferenceStatus>())
        {
            AssertValid(_validator.ValidateInput(ValidInput() with { Status = status }));
            AssertValid(_validator.ValidateOutput(ValidOutput() with { Status = status }));
        }

        foreach (var allowedUse in WorkflowStepReferenceAllowedUses.All)
        {
            AssertValid(_validator.ValidateInput(ValidInput() with { AllowedUses = [allowedUse] }));
            AssertValid(_validator.ValidateOutput(ValidOutput() with { AllowedUses = [allowedUse] }));
        }
    }

    [TestMethod]
    public void ValidateInput_RejectsRequiredFieldAndVocabularyFailures()
    {
        AssertInvalid(_validator.ValidateInput(ValidInput() with { ProjectId = Guid.Empty }), "WORKFLOW_STEP_INPUT_REFERENCE_PROJECT_ID_REQUIRED");
        AssertInvalid(_validator.ValidateInput(ValidInput() with { WorkflowRunId = Guid.Empty }), "WORKFLOW_STEP_INPUT_REFERENCE_RUN_ID_REQUIRED");
        AssertInvalid(_validator.ValidateInput(ValidInput() with { WorkflowRunStepId = Guid.Empty }), "WORKFLOW_STEP_INPUT_REFERENCE_STEP_ID_REQUIRED");
        AssertInvalid(_validator.ValidateInput(ValidInput() with { InputKey = " " }), "WORKFLOW_STEP_INPUT_REFERENCE_KEY_REQUIRED");
        AssertInvalid(_validator.ValidateInput(ValidInput() with { InputType = (WorkflowStepInputReferenceType)999 }), "WORKFLOW_STEP_INPUT_REFERENCE_TYPE_INVALID");
        AssertInvalid(_validator.ValidateInput(ValidInput() with { Status = (WorkflowStepReferenceStatus)999 }), "WORKFLOW_STEP_INPUT_REFERENCE_STATUS_INVALID");
        AssertInvalid(_validator.ValidateInput(ValidInput() with { AllowedUses = ["Approval"] }), "WORKFLOW_STEP_INPUT_REFERENCE_ALLOWED_USE_INVALID");
    }

    [TestMethod]
    public void ValidateOutput_RejectsRequiredFieldAndVocabularyFailures()
    {
        AssertInvalid(_validator.ValidateOutput(ValidOutput() with { ProjectId = Guid.Empty }), "WORKFLOW_STEP_OUTPUT_REFERENCE_PROJECT_ID_REQUIRED");
        AssertInvalid(_validator.ValidateOutput(ValidOutput() with { WorkflowRunId = Guid.Empty }), "WORKFLOW_STEP_OUTPUT_REFERENCE_RUN_ID_REQUIRED");
        AssertInvalid(_validator.ValidateOutput(ValidOutput() with { WorkflowRunStepId = Guid.Empty }), "WORKFLOW_STEP_OUTPUT_REFERENCE_STEP_ID_REQUIRED");
        AssertInvalid(_validator.ValidateOutput(ValidOutput() with { OutputKey = " " }), "WORKFLOW_STEP_OUTPUT_REFERENCE_KEY_REQUIRED");
        AssertInvalid(_validator.ValidateOutput(ValidOutput() with { OutputType = (WorkflowStepOutputReferenceType)999 }), "WORKFLOW_STEP_OUTPUT_REFERENCE_TYPE_INVALID");
        AssertInvalid(_validator.ValidateOutput(ValidOutput() with { Status = (WorkflowStepReferenceStatus)999 }), "WORKFLOW_STEP_OUTPUT_REFERENCE_STATUS_INVALID");
        AssertInvalid(_validator.ValidateOutput(ValidOutput() with { AllowedUses = ["ExecutionPermission"] }), "WORKFLOW_STEP_OUTPUT_REFERENCE_ALLOWED_USE_INVALID");
    }

    [TestMethod]
    public void WorkflowStepInputOutputReference_RejectsPrivateRawRuntimeAndAuthorityMarkers()
    {
        foreach (var marker in new[] { "hiddenReasoning", "chainOfThought", "rawPrompt", "rawCompletion", "rawToolOutput", "entirePatch" })
        {
            AssertInvalid(_validator.ValidateInput(ValidInput() with { SafeSummary = marker }), "WORKFLOW_STEP_REFERENCE_PRIVATE_REASONING_BLOCKED");
            AssertInvalid(_validator.ValidateOutput(ValidOutput() with { SafeSummary = marker }), "WORKFLOW_STEP_REFERENCE_PRIVATE_REASONING_BLOCKED");
        }

        foreach (var marker in new[] { "approval granted", "execution allowed", "policy satisfied", "source applied", "memory promoted", "accepted memory", "release approved", "workflow continued", "resume workflow", "dispatch agent", "tool executed", "model output", "runtime frame", "LangGraph state" })
        {
            AssertInvalid(_validator.ValidateInput(ValidInput() with { SafeSummary = marker }), "WORKFLOW_STEP_REFERENCE_AUTHORITY_OR_RUNTIME_LANGUAGE_BLOCKED");
            AssertInvalid(_validator.ValidateOutput(ValidOutput() with { SafeSummary = marker }), "WORKFLOW_STEP_REFERENCE_AUTHORITY_OR_RUNTIME_LANGUAGE_BLOCKED");
        }
    }

    [TestMethod]
    public void WorkflowStepInputOutputReference_RejectsUnsafeMetadataAndTrueAuthorityFlags()
    {
        AssertInvalid(_validator.ValidateInput(ValidInput() with { MetadataJson = "{\"rawPrompt\":\"no\"}" }), "WORKFLOW_STEP_REFERENCE_PRIVATE_REASONING_BLOCKED");
        AssertInvalid(_validator.ValidateOutput(ValidOutput() with { MetadataJson = "{\"rawCompletion\":\"no\"}" }), "WORKFLOW_STEP_REFERENCE_PRIVATE_REASONING_BLOCKED");
        AssertInvalid(_validator.ValidateInput(ValidInput() with { MetadataJson = "{\"executionAllowed\":true}" }), "WORKFLOW_STEP_REFERENCE_AUTHORITY_METADATA_BLOCKED");
        AssertInvalid(_validator.ValidateOutput(ValidOutput() with { MetadataJson = "{\"workflowContinued\":true}" }), "WORKFLOW_STEP_REFERENCE_AUTHORITY_METADATA_BLOCKED");
        AssertInvalid(_validator.ValidateInput(ValidInput() with { GrantsExecution = true }), "WORKFLOW_STEP_REFERENCE_AUTHORITY_FLAG_BLOCKED");
        AssertInvalid(_validator.ValidateOutput(ValidOutput() with { PromotesMemory = true }), "WORKFLOW_STEP_REFERENCE_AUTHORITY_FLAG_BLOCKED");
        AssertInvalid(_validator.ValidateOutput(ValidOutput() with { CreatesAcceptedMemory = true }), "WORKFLOW_STEP_REFERENCE_AUTHORITY_FLAG_BLOCKED");
    }

    [TestMethod]
    public void WorkflowStepInputOutputReference_AuthorityFlagsAreAlwaysFalse()
    {
        var input = MaterializedInput();
        var output = MaterializedOutput();

        AssertAllFalse(
            input.GrantsApproval,
            input.GrantsExecution,
            input.MutatesSource,
            input.PromotesMemory,
            input.StartsWorkflow,
            input.ContinuesWorkflow,
            input.ResumesWorkflow,
            input.SatisfiesPolicy,
            input.TransfersAuthority,
            input.ApprovesRelease,
            input.CreatesAcceptedMemory,
            output.GrantsApproval,
            output.GrantsExecution,
            output.MutatesSource,
            output.PromotesMemory,
            output.StartsWorkflow,
            output.ContinuesWorkflow,
            output.ResumesWorkflow,
            output.SatisfiesPolicy,
            output.TransfersAuthority,
            output.ApprovesRelease,
            output.CreatesAcceptedMemory);
    }

    [TestMethod]
    public void WorkflowStepInputOutputReference_DoesNotReferenceRuntimeCapabilityFiles()
    {
        var root = RepositoryRoot();
        var productionText = File.ReadAllText(Path.Combine(root, "IronDev.Core", "Workflow", "WorkflowStepInputOutputReferenceModels.cs")) +
                             File.ReadAllText(Path.Combine(root, "Docs", "BLOCK_J_DURABLE_WORKFLOW_RUN_SUBSTRATE.md"));

        AssertNoForbiddenTokens(
            productionText,
            "IHostedService",
            "BackgroundService",
            "ControllerBase",
            "WebApplication",
            "WorkflowRunner",
            "WorkflowResumeEngine",
            "WorkflowRestoreEngine",
            "LangGraphRuntime",
            "A2aRuntime",
            "MessageBus",
            "Queue",
            "ToolExecutor",
            "ModelClient",
            "ApplySource",
            "PromoteMemory",
            "ApproveRelease");
    }

    private static WorkflowStepInputReferenceCreateRequest ValidInput() =>
        new()
        {
            WorkflowStepInputReferenceId = Guid.NewGuid(),
            WorkflowRunId = WorkflowRunId,
            WorkflowRunStepId = WorkflowRunStepId,
            ProjectId = ProjectId,
            InputKey = "validation-context",
            InputType = WorkflowStepInputReferenceType.ValidationFinding,
            Status = WorkflowStepReferenceStatus.Recorded,
            SourceType = "validation_finding",
            SourceId = "validation-001",
            SafeSummary = "Input reference supports step review only.",
            WorkflowCheckpointId = Guid.NewGuid(),
            WorkflowRunEvidenceReferenceId = Guid.NewGuid(),
            WorkflowRunGroundingReferenceId = Guid.NewGuid(),
            GovernanceEventId = Guid.NewGuid(),
            AllowedUses = [WorkflowStepReferenceAllowedUses.Context, WorkflowStepReferenceAllowedUses.Review],
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "workflow-reference-tests",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"workflow.step.reference.metadata.v1\",\"notes\":\"Input reference for validation review.\",\"grantsApproval\":false,\"grantsExecution\":false,\"mutatesSource\":false,\"promotesMemory\":false,\"startsWorkflow\":false,\"continuesWorkflow\":false,\"resumesWorkflow\":false,\"satisfiesPolicy\":false,\"transfersAuthority\":false,\"approvesRelease\":false,\"createsAcceptedMemory\":false}"
        };

    private static WorkflowStepOutputReferenceCreateRequest ValidOutput() =>
        new()
        {
            WorkflowStepOutputReferenceId = Guid.NewGuid(),
            WorkflowRunId = WorkflowRunId,
            WorkflowRunStepId = WorkflowRunStepId,
            ProjectId = ProjectId,
            OutputKey = "review-finding",
            OutputType = WorkflowStepOutputReferenceType.ReviewFinding,
            Status = WorkflowStepReferenceStatus.Recorded,
            TargetType = "review_finding",
            TargetId = "review-001",
            SafeSummary = "Output reference records review finding evidence only.",
            WorkflowCheckpointId = Guid.NewGuid(),
            WorkflowRunEvidenceReferenceId = Guid.NewGuid(),
            WorkflowRunGroundingReferenceId = Guid.NewGuid(),
            GovernanceEventId = Guid.NewGuid(),
            AllowedUses = [WorkflowStepReferenceAllowedUses.Traceability, WorkflowStepReferenceAllowedUses.AuditReference],
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "workflow-reference-tests",
            MetadataVersion = 1,
            MetadataJson = "{\"schema\":\"workflow.step.reference.metadata.v1\",\"notes\":\"Output reference for review traceability.\",\"grantsApproval\":false,\"grantsExecution\":false,\"mutatesSource\":false,\"promotesMemory\":false,\"startsWorkflow\":false,\"continuesWorkflow\":false,\"resumesWorkflow\":false,\"satisfiesPolicy\":false,\"transfersAuthority\":false,\"approvesRelease\":false,\"createsAcceptedMemory\":false}"
        };

    private static WorkflowStepInputReference MaterializedInput() =>
        new()
        {
            WorkflowStepInputReferenceId = Guid.NewGuid(),
            WorkflowRunId = WorkflowRunId,
            WorkflowRunStepId = WorkflowRunStepId,
            ProjectId = ProjectId,
            InputKey = "validation-context",
            InputType = WorkflowStepInputReferenceType.ValidationFinding,
            Status = WorkflowStepReferenceStatus.Recorded,
            SourceType = "validation_finding",
            SourceId = "validation-001",
            SafeSummary = "Input reference supports step review only.",
            AllowedUses = [WorkflowStepReferenceAllowedUses.Context],
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "workflow-reference-tests",
            MetadataVersion = 1,
            MetadataJson = "{}",
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            ContinuesWorkflow = false,
            ResumesWorkflow = false,
            SatisfiesPolicy = false,
            TransfersAuthority = false,
            ApprovesRelease = false,
            CreatesAcceptedMemory = false,
            CreatedUtc = DateTimeOffset.UtcNow
        };

    private static WorkflowStepOutputReference MaterializedOutput() =>
        new()
        {
            WorkflowStepOutputReferenceId = Guid.NewGuid(),
            WorkflowRunId = WorkflowRunId,
            WorkflowRunStepId = WorkflowRunStepId,
            ProjectId = ProjectId,
            OutputKey = "review-finding",
            OutputType = WorkflowStepOutputReferenceType.ReviewFinding,
            Status = WorkflowStepReferenceStatus.Recorded,
            TargetType = "review_finding",
            TargetId = "review-001",
            SafeSummary = "Output reference records review finding evidence only.",
            AllowedUses = [WorkflowStepReferenceAllowedUses.Traceability],
            CreatedByActorType = "system_test_fixture",
            CreatedByActorId = "workflow-reference-tests",
            MetadataVersion = 1,
            MetadataJson = "{}",
            GrantsApproval = false,
            GrantsExecution = false,
            MutatesSource = false,
            PromotesMemory = false,
            StartsWorkflow = false,
            ContinuesWorkflow = false,
            ResumesWorkflow = false,
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

    private static void AssertDoesNotContainAny(IEnumerable<string> values, params string[] forbidden)
    {
        var text = string.Join("\n", values);
        AssertNoForbiddenTokens(text, forbidden);
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
