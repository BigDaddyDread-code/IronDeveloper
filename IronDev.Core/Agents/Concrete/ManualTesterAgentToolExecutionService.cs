using IronDev.Core.Agents.Audit;
using AuditAgentRunStatus = IronDev.Core.Agents.Audit.AgentRunStatus;
using AuditThoughtLedgerEntry = IronDev.Core.Agents.Audit.ThoughtLedgerEntry;

namespace IronDev.Core.Agents.Concrete;

public enum ManualTesterAgentToolExecutionStatus
{
    Succeeded = 1,
    Failed = 2,
    Blocked = 3,
    InvalidRequest = 4
}

public sealed record ManualTesterAgentToolExecutionRequest
{
    public required string ManualExecutionId { get; init; }
    public required AgentToolRequest ToolRequest { get; init; }
    public required AgentToolExecutionGateDecision GateDecision { get; init; }
    public required string RequestedByUserId { get; init; }
    public required string TestPlanRef { get; init; }
    public string? TestPlanPath { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset RequestedAtUtc { get; init; }
}

public sealed record ManualTesterAgentToolExecutionOutput
{
    public required string OutputId { get; init; }
    public required string Summary { get; init; }
    public required int ExitCode { get; init; }
    public required string Outcome { get; init; }
    public int TestsPassed { get; init; }
    public int TestsFailed { get; init; }
    public int TestsSkipped { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public bool ContainsRawPrivateReasoning { get; init; }
    public bool MutatesSource { get; init; }
    public bool CallsExternalSystem { get; init; }
    public bool SubmitsGitHubReview { get; init; }
    public bool PromotesMemory { get; init; }
    public bool CreatesCollectiveMemory { get; init; }
    public bool WritesWeaviate { get; init; }
}

public sealed record ManualTesterAgentToolExecutionIssue
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Message { get; init; }
    public string Field { get; init; } = string.Empty;
}

public sealed record ManualTesterAgentToolExecutionResult
{
    public required bool Succeeded { get; init; }
    public required ManualTesterAgentToolExecutionStatus Status { get; init; }
    public required string ManualExecutionId { get; init; }
    public string? ToolRequestId { get; init; }
    public string? GateDecisionId { get; init; }
    public ManualTesterAgentToolExecutionOutput? Output { get; init; }
    public AgentRunAuditEnvelope? AuditEnvelope { get; init; }
    public IReadOnlyList<ManualTesterAgentToolExecutionIssue> Issues { get; init; } = [];
}

public sealed record TestRunPlanExecutionRequest
{
    public required string ExecutionId { get; init; }
    public required string TestPlanRef { get; init; }
    public string? TestPlanPath { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset StartedAtUtc { get; init; }
}

public sealed record TestRunPlanExecutionResult
{
    public required bool Succeeded { get; init; }
    public required string ExecutionId { get; init; }
    public required int ExitCode { get; init; }
    public required string Summary { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public int TestsPassed { get; init; }
    public int TestsFailed { get; init; }
    public int TestsSkipped { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<string> EvidenceRefs { get; init; } = [];
    public IReadOnlyList<ManualTesterAgentToolExecutionIssue> Issues { get; init; } = [];
}

public interface ITestRunPlanExecutor
{
    TestRunPlanExecutionResult Execute(TestRunPlanExecutionRequest request);
}

public sealed class ScriptedTestRunPlanExecutor : ITestRunPlanExecutor
{
    private readonly Func<TestRunPlanExecutionRequest, TestRunPlanExecutionResult> _script;

    public int CallCount { get; private set; }
    public TestRunPlanExecutionRequest? LastRequest { get; private set; }

    public ScriptedTestRunPlanExecutor(Func<TestRunPlanExecutionRequest, TestRunPlanExecutionResult>? script = null)
    {
        _script = script ?? DefaultScript;
    }

    public TestRunPlanExecutionResult Execute(TestRunPlanExecutionRequest request)
    {
        CallCount++;
        LastRequest = request;
        return _script(request);
    }

    private static TestRunPlanExecutionResult DefaultScript(TestRunPlanExecutionRequest request) =>
        new()
        {
            Succeeded = true,
            ExecutionId = request.ExecutionId,
            ExitCode = 0,
            Summary = "Scripted test-plan executor completed successfully.",
            Outcome = "passed",
            TestsPassed = 1,
            Duration = TimeSpan.FromMilliseconds(1),
            EvidenceRefs = [$"test-plan:{request.TestPlanRef}"]
        };
}

public interface IManualTesterAgentToolExecutionService
{
    ManualTesterAgentToolExecutionResult Execute(ManualTesterAgentToolExecutionRequest request);
}

public sealed class ManualTesterAgentToolExecutionValidator
{
    public const string ExecutionIdRequired = "TESTER_EXECUTION_ID_REQUIRED";
    public const string RequestRequired = "TESTER_EXECUTION_REQUEST_REQUIRED";
    public const string GateRequired = "TESTER_EXECUTION_GATE_REQUIRED";
    public const string UserRequired = "TESTER_EXECUTION_USER_REQUIRED";
    public const string TestPlanRequired = "TESTER_EXECUTION_TEST_PLAN_REQUIRED";
    public const string RequestGateMismatch = "TESTER_EXECUTION_REQUEST_GATE_MISMATCH";
    public const string GateNotAllowed = "TESTER_EXECUTION_GATE_NOT_ALLOWED";
    public const string GateActionFlagsUnsafe = "TESTER_EXECUTION_GATE_ACTION_FLAGS_UNSAFE";
    public const string ToolKindInvalid = "TESTER_EXECUTION_TOOL_KIND_INVALID";
    public const string RequestTypeInvalid = "TESTER_EXECUTION_REQUEST_TYPE_INVALID";
    public const string AgentInvalid = "TESTER_EXECUTION_AGENT_INVALID";
    public const string RequestClaimsApproval = "TESTER_EXECUTION_REQUEST_CLAIMS_APPROVAL";
    public const string RequestClaimsPermission = "TESTER_EXECUTION_REQUEST_CLAIMS_PERMISSION";
    public const string RequestContainsResult = "TESTER_EXECUTION_REQUEST_CONTAINS_RESULT";
    public const string RequestExecutableWithoutGate = "TESTER_EXECUTION_REQUEST_EXECUTABLE_WITHOUT_GATE";
    public const string TestPlanPathUnsafe = "TESTER_EXECUTION_TEST_PLAN_PATH_UNSAFE";
    public const string WorkingDirectoryUnsafe = "TESTER_EXECUTION_WORKING_DIRECTORY_UNSAFE";
    public const string ParameterUnsafe = "TESTER_EXECUTION_PARAMETER_UNSAFE";
    public const string OutputUnsafe = "TESTER_EXECUTION_OUTPUT_UNSAFE";
    public const string AuditInvalid = "TESTER_EXECUTION_AUDIT_INVALID";
    public const string ThoughtLedgerInvalid = "TESTER_EXECUTION_THOUGHT_LEDGER_INVALID";

    private static readonly IReadOnlyList<string> UnsafeTextMarkers =
    [
        "raw" + " prompt",
        "raw" + " completion",
        "chain" + "-of-" + "thought",
        "scratch" + "pad",
        "scratch" + " pad",
        "private" + " reasoning",
        "hidden" + " deliberation",
        "system" + " prompt",
        "developer" + " prompt",
        "approval granted",
        "approved for execution",
        "policy cleared",
        "promote memory",
        "accepted memory"
    ];

    private static readonly IReadOnlyList<string> UnsafeParameterMarkers =
    [
        "secret",
        "password",
        "api" + "key",
        "token",
        "&&",
        "||",
        ";",
        "`",
        ">",
        "<",
        "|",
        "Power" + "Shell",
        "cmd" + ".exe",
        "ba" + "sh"
    ];

    public IReadOnlyList<ManualTesterAgentToolExecutionIssue> Validate(ManualTesterAgentToolExecutionRequest request)
    {
        var issues = new List<ManualTesterAgentToolExecutionIssue>();

        if (request is null)
        {
            AddError(issues, RequestRequired, "Execution request is required.", "Request");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(request.ManualExecutionId))
            AddError(issues, ExecutionIdRequired, "ManualExecutionId is required.", nameof(request.ManualExecutionId));

        if (request.ToolRequest is null)
            AddError(issues, RequestRequired, "ToolRequest is required.", nameof(request.ToolRequest));

        if (request.GateDecision is null)
            AddError(issues, GateRequired, "GateDecision is required.", nameof(request.GateDecision));

        if (string.IsNullOrWhiteSpace(request.RequestedByUserId))
            AddError(issues, UserRequired, "RequestedByUserId is required.", nameof(request.RequestedByUserId));

        if (string.IsNullOrWhiteSpace(request.TestPlanRef))
            AddError(issues, TestPlanRequired, "TestPlanRef is required.", nameof(request.TestPlanRef));

        if (request.ToolRequest is not null)
            ValidateToolRequest(request.ToolRequest, issues);

        if (request.ToolRequest is not null && request.GateDecision is not null)
            ValidateGateDecision(request.ToolRequest, request.GateDecision, issues);

        ValidateSafePath(request.TestPlanPath, TestPlanPathUnsafe, nameof(request.TestPlanPath), issues);
        ValidateSafePath(request.WorkingDirectory, WorkingDirectoryUnsafe, nameof(request.WorkingDirectory), issues);
        ValidateParameters(request.Parameters, issues);

        return issues;
    }

    public IReadOnlyList<ManualTesterAgentToolExecutionIssue> ValidateOutput(ManualTesterAgentToolExecutionOutput output)
    {
        var issues = new List<ManualTesterAgentToolExecutionIssue>();

        if (output is null)
        {
            AddError(issues, OutputUnsafe, "Execution output is required.", "Output");
            return issues;
        }

        if (string.IsNullOrWhiteSpace(output.OutputId) ||
            string.IsNullOrWhiteSpace(output.Summary) ||
            string.IsNullOrWhiteSpace(output.Outcome))
        {
            AddError(issues, OutputUnsafe, "OutputId, Summary, and Outcome are required.", "Output");
        }

        if (output.EvidenceRefs.Count == 0 || output.EvidenceRefs.Any(string.IsNullOrWhiteSpace))
            AddError(issues, OutputUnsafe, "Execution output requires evidence references.", nameof(output.EvidenceRefs));

        if (output.ContainsRawPrivateReasoning ||
            ContainsUnsafeText([output.OutputId, output.Summary, output.Outcome, .. output.EvidenceRefs]))
        {
            AddError(issues, OutputUnsafe, "Execution output cannot contain raw private reasoning or authority claims.", "Output");
        }

        if (output.MutatesSource ||
            output.CallsExternalSystem ||
            output.SubmitsGitHubReview ||
            output.PromotesMemory ||
            output.CreatesCollectiveMemory ||
            output.WritesWeaviate)
        {
            AddError(issues, OutputUnsafe, "Execution output cannot claim source, external, GitHub, memory, or index side effects.", "Output");
        }

        return issues;
    }

    private static void ValidateToolRequest(
        AgentToolRequest toolRequest,
        List<ManualTesterAgentToolExecutionIssue> issues)
    {
        if (toolRequest.ToolKind != AgentToolKind.TestRun)
            AddError(issues, ToolKindInvalid, "Manual TesterAgent execution only supports AgentToolKind.TestRun.", nameof(toolRequest.ToolKind));

        if (toolRequest.RequestType != AgentToolRequestType.TestExecutionRequest)
            AddError(issues, RequestTypeInvalid, "Manual TesterAgent execution only supports TestExecutionRequest.", nameof(toolRequest.RequestType));

        if (!IsTestingAgent(toolRequest.Actor))
            AddError(issues, AgentInvalid, "Manual TesterAgent execution requires the built-in TestingAgent actor.", nameof(toolRequest.Actor));

        if (toolRequest.ClaimsApproval)
            AddError(issues, RequestClaimsApproval, "ToolRequest cannot claim approval.", nameof(toolRequest.ClaimsApproval));

        if (toolRequest.ClaimsExecutionPermission)
            AddError(issues, RequestClaimsPermission, "ToolRequest cannot claim execution permission.", nameof(toolRequest.ClaimsExecutionPermission));

        if (toolRequest.ContainsExecutionResult)
            AddError(issues, RequestContainsResult, "ToolRequest cannot contain execution results.", nameof(toolRequest.ContainsExecutionResult));

        if (toolRequest.IsExecutableWithoutGate)
            AddError(issues, RequestExecutableWithoutGate, "ToolRequest cannot be executable without the gate.", nameof(toolRequest.IsExecutableWithoutGate));
    }

    private static void ValidateGateDecision(
        AgentToolRequest toolRequest,
        AgentToolExecutionGateDecision gateDecision,
        List<ManualTesterAgentToolExecutionIssue> issues)
    {
        if (!string.Equals(toolRequest.ToolRequestId, gateDecision.ToolRequestId, StringComparison.Ordinal))
        {
            AddError(issues, RequestGateMismatch, "ToolRequestId must match GateDecision.ToolRequestId.", nameof(gateDecision.ToolRequestId));
        }

        if (gateDecision.Decision != AgentToolExecutionGateDecisionType.Allowed ||
            !gateDecision.GrantsExecution ||
            !gateDecision.RequiresExecutor)
        {
            AddError(issues, GateNotAllowed, "GateDecision must be Allowed, grant future execution, and require a future executor.", nameof(gateDecision.Decision));
        }

        if (gateDecision.ExecutesTool ||
            gateDecision.MutatesSource ||
            gateDecision.CallsExternalSystem ||
            gateDecision.SubmitsGitHubReview ||
            gateDecision.PersistsResult ||
            gateDecision.PromotesMemory ||
            gateDecision.CreatesCollectiveMemory ||
            gateDecision.WritesWeaviate)
        {
            AddError(issues, GateActionFlagsUnsafe, "GateDecision must not claim execution, mutation, external, persistence, memory, or index side effects.", nameof(AgentToolExecutionGateDecision));
        }
    }

    private static bool IsTestingAgent(AgentToolRequestActor actor)
    {
        if (actor is null)
            return false;

        var definition = AgentDefinitionCatalog.TestingAgent;
        return string.Equals(actor.AgentId, definition.AgentId, StringComparison.Ordinal) &&
               string.Equals(actor.AgentName, definition.Name, StringComparison.Ordinal) &&
               actor.AgentKind == AgentKind.TestingAgent &&
               actor.ExecutionMode == AgentExecutionMode.ToolExecution &&
               actor.DeclaredCapabilities.Contains(AgentCapability.RunTool) &&
               !actor.ForbiddenCapabilities.Contains(AgentCapability.RunTool);
    }

    private static void ValidateSafePath(
        string? path,
        string code,
        string field,
        List<ManualTesterAgentToolExecutionIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(path) ||
            path.Contains(':', StringComparison.Ordinal) ||
            segments.Length == 0 ||
            segments.Any(segment => segment == "." || segment == ".." || string.IsNullOrWhiteSpace(segment)) ||
            ContainsUnsafeText([path]) ||
            segments.Any(segment => segment.Contains('*', StringComparison.Ordinal) || segment.Contains('?', StringComparison.Ordinal)))
        {
            AddError(issues, code, "Path must be a safe relative path.", field);
        }
    }

    private static void ValidateParameters(
        IReadOnlyDictionary<string, string> parameters,
        List<ManualTesterAgentToolExecutionIssue> issues)
    {
        foreach (var parameter in parameters)
        {
            if (string.IsNullOrWhiteSpace(parameter.Key) ||
                ContainsUnsafeParameterText(parameter.Key) ||
                ContainsUnsafeParameterText(parameter.Value))
            {
                AddError(issues, ParameterUnsafe, "Parameters are data only and cannot contain secrets or command fragments.", nameof(parameters));
            }
        }
    }

    private static bool ContainsUnsafeText(IEnumerable<string?> values) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value) &&
                            UnsafeTextMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)));

    private static bool ContainsUnsafeParameterText(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        UnsafeParameterMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static void AddError(
        List<ManualTesterAgentToolExecutionIssue> issues,
        string code,
        string message,
        string field) =>
        issues.Add(new ManualTesterAgentToolExecutionIssue
        {
            Code = code,
            Severity = AgentDefinitionValidator.SeverityError,
            Message = message,
            Field = field
        });
}

public sealed class ManualTesterAgentToolExecutionService : IManualTesterAgentToolExecutionService
{
    private readonly ITestRunPlanExecutor _executor;
    private readonly ManualTesterAgentToolExecutionValidator _validator;
    private readonly AgentRunAuditEnvelopeValidator _auditValidator;
    private readonly ThoughtLedgerSafetyValidator _thoughtLedgerValidator;

    public ManualTesterAgentToolExecutionService()
        : this(
            new ScriptedTestRunPlanExecutor(),
            new ManualTesterAgentToolExecutionValidator(),
            new AgentRunAuditEnvelopeValidator(),
            new ThoughtLedgerSafetyValidator())
    {
    }

    public ManualTesterAgentToolExecutionService(
        ITestRunPlanExecutor executor,
        ManualTesterAgentToolExecutionValidator? validator = null,
        AgentRunAuditEnvelopeValidator? auditValidator = null,
        ThoughtLedgerSafetyValidator? thoughtLedgerValidator = null)
    {
        _executor = executor;
        _validator = validator ?? new ManualTesterAgentToolExecutionValidator();
        _auditValidator = auditValidator ?? new AgentRunAuditEnvelopeValidator();
        _thoughtLedgerValidator = thoughtLedgerValidator ?? new ThoughtLedgerSafetyValidator();
    }

    public ManualTesterAgentToolExecutionResult Execute(ManualTesterAgentToolExecutionRequest request)
    {
        var issues = _validator.Validate(request);
        if (issues.Count > 0)
            return Rejected(request, DetermineRejectedStatus(issues), issues);

        var executionResult = _executor.Execute(new TestRunPlanExecutionRequest
        {
            ExecutionId = request.ManualExecutionId,
            TestPlanRef = request.TestPlanRef,
            TestPlanPath = request.TestPlanPath,
            WorkingDirectory = request.WorkingDirectory,
            Parameters = request.Parameters,
            StartedAtUtc = request.RequestedAtUtc
        });

        var output = BuildOutput(request.ManualExecutionId, executionResult);
        issues = _validator.ValidateOutput(output).Concat(executionResult.Issues).ToArray();
        if (issues.Any(issue => string.Equals(issue.Severity, AgentDefinitionValidator.SeverityError, StringComparison.OrdinalIgnoreCase)))
            return Rejected(request, ManualTesterAgentToolExecutionStatus.InvalidRequest, issues);

        var auditEnvelope = BuildAuditEnvelope(request, output, executionResult);
        issues = ToManualIssues(_auditValidator.Validate(auditEnvelope), ManualTesterAgentToolExecutionValidator.AuditInvalid)
            .Concat(ToManualIssues(_thoughtLedgerValidator.Validate(auditEnvelope.ThoughtLedger), ManualTesterAgentToolExecutionValidator.ThoughtLedgerInvalid))
            .ToArray();

        if (issues.Count > 0)
            return Rejected(request, ManualTesterAgentToolExecutionStatus.InvalidRequest, issues);

        var status = executionResult.Succeeded
            ? ManualTesterAgentToolExecutionStatus.Succeeded
            : ManualTesterAgentToolExecutionStatus.Failed;

        return new ManualTesterAgentToolExecutionResult
        {
            Succeeded = executionResult.Succeeded,
            Status = status,
            ManualExecutionId = request.ManualExecutionId,
            ToolRequestId = request.ToolRequest.ToolRequestId,
            GateDecisionId = request.GateDecision.GateDecisionId,
            Output = output,
            AuditEnvelope = auditEnvelope,
            Issues = []
        };
    }

    private static ManualTesterAgentToolExecutionOutput BuildOutput(
        string manualExecutionId,
        TestRunPlanExecutionResult executionResult) =>
        new()
        {
            OutputId = $"manual-tester-output-{manualExecutionId}",
            Summary = executionResult.Summary,
            ExitCode = executionResult.ExitCode,
            Outcome = string.IsNullOrWhiteSpace(executionResult.Outcome)
                ? executionResult.Succeeded ? "passed" : "failed"
                : executionResult.Outcome,
            TestsPassed = executionResult.TestsPassed,
            TestsFailed = executionResult.TestsFailed,
            TestsSkipped = executionResult.TestsSkipped,
            Duration = executionResult.Duration,
            EvidenceRefs = executionResult.EvidenceRefs,
            ContainsRawPrivateReasoning = false,
            MutatesSource = false,
            CallsExternalSystem = false,
            SubmitsGitHubReview = false,
            PromotesMemory = false,
            CreatesCollectiveMemory = false,
            WritesWeaviate = false
        };

    private static AgentRunAuditEnvelope BuildAuditEnvelope(
        ManualTesterAgentToolExecutionRequest request,
        ManualTesterAgentToolExecutionOutput output,
        TestRunPlanExecutionResult executionResult)
    {
        var runId = request.ManualExecutionId;
        var evidenceRefs = BuildEvidenceRefs(request, output);
        var completedAt = request.RequestedAtUtc + executionResult.Duration;

        return new AgentRunAuditEnvelope
        {
            Run = new AgentRunRecord
            {
                AgentRunId = runId,
                TenantId = request.ToolRequest.Scope.TenantId,
                ProjectId = request.ToolRequest.Scope.ProjectId,
                CampaignId = request.ToolRequest.Scope.CampaignId ?? "campaign-unspecified",
                RunId = request.ToolRequest.Scope.RunId ?? runId,
                AgentId = AgentDefinitionCatalog.TestingAgent.AgentId,
                AgentName = AgentDefinitionCatalog.TestingAgent.Name,
                RequestedByUserId = request.RequestedByUserId,
                TriggerType = AgentRunTriggerType.ManualGovernedRequest,
                Status = executionResult.Succeeded ? AuditAgentRunStatus.Completed : AuditAgentRunStatus.Failed,
                RequestSummary = "Manual TesterAgent execution of a gated TestRun request.",
                Purpose = "Execute a controlled test-plan request after AgentToolExecutionGate allowed a future executor.",
                CreatedAtUtc = request.RequestedAtUtc,
                StartedAtUtc = request.RequestedAtUtc,
                CompletedAtUtc = completedAt
            },
            AgentDefinitionSnapshot = AgentDefinitionCatalog.TestingAgent,
            Inputs = BuildInputs(runId, request),
            Outputs =
            [
                new AgentRunOutputRef
                {
                    OutputRefId = $"output-{output.OutputId}",
                    AgentRunId = runId,
                    RefType = "ManualTesterAgentToolExecutionOutput",
                    RefId = output.OutputId,
                    Summary = "Controlled TesterAgent test output was recorded as evidence.",
                    IsReviewOnly = false,
                    IsProposalOnly = false,
                    CreatesAuthority = false,
                    CreatesRuntimeAction = false,
                    ContainsRawPrivateReasoning = false,
                    EvidenceRefs = evidenceRefs
                }
            ],
            Steps = BuildSteps(runId, evidenceRefs, request.RequestedAtUtc, completedAt),
            CapabilityUses = BuildCapabilityUses(runId),
            BoundaryDecisions = BuildBoundaryDecisions(runId, evidenceRefs),
            ThoughtLedger = BuildThoughtLedger(runId, evidenceRefs, completedAt)
        };
    }

    private static IReadOnlyList<AgentRunInputRef> BuildInputs(
        string runId,
        ManualTesterAgentToolExecutionRequest request) =>
        [
            new AgentRunInputRef
            {
                InputRefId = $"input-tool-request-{request.ToolRequest.ToolRequestId}",
                AgentRunId = runId,
                RefType = "AgentToolRequest",
                RefId = request.ToolRequest.ToolRequestId,
                Source = "manual-user-request",
                Summary = "Typed TestRun tool request supplied for manual TesterAgent execution.",
                IsAuthoritativeForAction = false,
                ContainsRawPrivateReasoning = false
            },
            new AgentRunInputRef
            {
                InputRefId = $"input-gate-decision-{request.GateDecision.GateDecisionId}",
                AgentRunId = runId,
                RefType = "AgentToolExecutionGateDecision",
                RefId = request.GateDecision.GateDecisionId,
                Source = "agent-tool-execution-gate",
                Summary = "Gate decision allowed future executor use without executing the tool.",
                IsAuthoritativeForAction = false,
                ContainsRawPrivateReasoning = false
            },
            new AgentRunInputRef
            {
                InputRefId = $"input-test-plan-{request.TestPlanRef}",
                AgentRunId = runId,
                RefType = "TestPlanRef",
                RefId = request.TestPlanRef,
                Source = "manual-user-request",
                Summary = "Controlled test plan reference supplied as data.",
                IsAuthoritativeForAction = false,
                ContainsRawPrivateReasoning = false
            }
        ];

    private static IReadOnlyList<AgentRunStep> BuildSteps(
        string runId,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt) =>
        [
            Step(runId, 1, AgentRunStepType.Created, "Manual TesterAgent execution request was created.", evidenceRefs, startedAt),
            Step(runId, 2, AgentRunStepType.InputBound, "Tool request, gate decision, and test plan references were bound.", evidenceRefs, startedAt),
            Step(runId, 3, AgentRunStepType.CapabilityEvaluated, "RunTool was checked for TestingAgent and dangerous capabilities stayed blocked.", evidenceRefs, startedAt),
            Step(runId, 4, AgentRunStepType.BoundaryDecision, "Gate decision was allowed and had not executed the tool.", evidenceRefs, startedAt),
            Step(runId, 5, AgentRunStepType.OutputRecorded, "Controlled test output was recorded as evidence.", evidenceRefs, completedAt),
            Step(runId, 6, AgentRunStepType.Completed, "Manual TesterAgent execution completed with safe audit evidence.", evidenceRefs, completedAt)
        ];

    private static AgentRunStep Step(
        string runId,
        int sequence,
        AgentRunStepType stepType,
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
            EvidenceRefs = evidenceRefs,
            ContainsRawPrivateReasoning = false
        };

    private static IReadOnlyList<AgentCapabilityUseRecord> BuildCapabilityUses(string runId) =>
        [
            CapabilityUse(runId, AgentCapability.RunTool, AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(runId, AgentCapability.CreateReport, AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(runId, AgentCapability.CreateTestReport, AgentCapabilityUseOutcome.Allowed),
            CapabilityUse(runId, AgentCapability.MutateSource, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.CallExternalSystem, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.PromoteCollectiveMemory, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.RepresentHumanApproval, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.RepresentHumanPromotionDecision, AgentCapabilityUseOutcome.Blocked),
            CapabilityUse(runId, AgentCapability.BlockExecution, AgentCapabilityUseOutcome.Blocked)
        ];

    private static AgentCapabilityUseRecord CapabilityUse(
        string runId,
        AgentCapability capability,
        AgentCapabilityUseOutcome outcome)
    {
        var definition = AgentDefinitionCatalog.TestingAgent;
        var declared = definition.Capabilities?.Contains(capability) == true;
        var forbidden = definition.ForbiddenCapabilities?.Contains(capability) == true;

        return new AgentCapabilityUseRecord
        {
            CapabilityUseId = $"capability-{runId}-{capability}",
            AgentRunId = runId,
            Capability = capability,
            Outcome = outcome,
            Summary = $"{capability} was {outcome} for controlled manual TesterAgent execution.",
            PolicyDecisionId = $"policy-{runId}",
            BoundaryDecisionId = $"boundary-{runId}-{capability}",
            EvidenceRef = $"evidence-{runId}",
            WasDeclaredOnAgent = declared,
            WasForbiddenOnAgent = forbidden
        };
    }

    private static IReadOnlyList<AgentBoundaryDecision> BuildBoundaryDecisions(
        string runId,
        IReadOnlyList<string> evidenceRefs) =>
        [
            Boundary(runId, "tool-request-validated", AgentBoundaryDecisionType.Evidence, "allow", "Tool request was validated as TestingAgent TestRun evidence.", evidenceRefs),
            Boundary(runId, "gate-decision-allowed", AgentBoundaryDecisionType.GovernanceDecision, "allow", "Gate decision allowed future executor use and did not execute the tool.", evidenceRefs),
            Boundary(runId, "future-executor-invoked-manually", AgentBoundaryDecisionType.Capability, "allow", "Controlled test-plan executor was invoked manually for TestingAgent only.", evidenceRefs),
            Boundary(runId, "test-output-recorded", AgentBoundaryDecisionType.OutputValidation, "allow", "Test output was recorded as safe evidence.", evidenceRefs),
            Boundary(runId, "source-mutation-blocked", AgentBoundaryDecisionType.Capability, "block", "Source mutation remained blocked.", evidenceRefs),
            Boundary(runId, "external-effect-blocked", AgentBoundaryDecisionType.Capability, "block", "External effects remained blocked.", evidenceRefs),
            Boundary(runId, "github-submission-blocked", AgentBoundaryDecisionType.Capability, "block", "GitHub submission remained blocked.", evidenceRefs),
            Boundary(runId, "memory-promotion-blocked", AgentBoundaryDecisionType.Capability, "block", "Memory promotion remained blocked.", evidenceRefs),
            Boundary(runId, "weaviate-write-blocked", AgentBoundaryDecisionType.Capability, "block", "Index writing remained blocked.", evidenceRefs),
            Boundary(runId, "thought-ledger-safety", AgentBoundaryDecisionType.ThoughtLedgerSafety, "allow", "ThoughtLedger entries contain safe rationale only.", evidenceRefs)
        ];

    private static AgentBoundaryDecision Boundary(
        string runId,
        string suffix,
        AgentBoundaryDecisionType type,
        string decision,
        string reason,
        IReadOnlyList<string> evidenceRefs) =>
        new()
        {
            BoundaryDecisionId = $"boundary-{runId}-{suffix}",
            AgentRunId = runId,
            BoundaryType = type,
            Decision = decision,
            Reason = reason,
            SourceRefId = $"manual-tester-{suffix}",
            GrantsAuthority = false,
            GrantsHumanApproval = false,
            GrantsPolicyApproval = false,
            GrantsMemoryPromotion = false,
            EvidenceRefs = evidenceRefs
        };

    private static IReadOnlyList<AuditThoughtLedgerEntry> BuildThoughtLedger(
        string runId,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset recordedAtUtc) =>
        [
            Thought(runId, "request-validated", ThoughtLedgerEntryType.DecisionRationale, "TesterAgent manual tool execution request was validated.", evidenceRefs, recordedAtUtc),
            Thought(runId, "gate-allowed", ThoughtLedgerEntryType.BoundaryDecision, "AgentToolExecutionGate decision allowed future executor use.", evidenceRefs, recordedAtUtc),
            Thought(runId, "executor-invoked", ThoughtLedgerEntryType.EvidenceUsed, "Controlled test-plan executor was invoked manually.", evidenceRefs, recordedAtUtc),
            Thought(runId, "output-recorded", ThoughtLedgerEntryType.OutputRationale, "Test output was recorded as evidence.", evidenceRefs, recordedAtUtc),
            Thought(runId, "dangerous-blocked", ThoughtLedgerEntryType.BoundaryDecision, "Dangerous capabilities remained blocked.", evidenceRefs, recordedAtUtc),
            Thought(runId, "no-dangerous-effects", ThoughtLedgerEntryType.FollowUp, "No source writes, external effects, GitHub submission, memory promotion, or index write occurred.", evidenceRefs, recordedAtUtc)
        ];

    private static AuditThoughtLedgerEntry Thought(
        string runId,
        string suffix,
        ThoughtLedgerEntryType type,
        string summary,
        IReadOnlyList<string> evidenceRefs,
        DateTimeOffset recordedAtUtc) =>
        new()
        {
            ThoughtLedgerEntryId = $"thought-{runId}-{suffix}",
            AgentRunId = runId,
            EntryType = type,
            Summary = summary,
            EvidenceRefs = evidenceRefs,
            ContainsRawPrivateReasoning = false,
            GrantsAuthority = false,
            GrantsApproval = false,
            GrantsMemoryPromotion = false,
            RecordedAtUtc = recordedAtUtc
        };

    private static IReadOnlyList<string> BuildEvidenceRefs(
        ManualTesterAgentToolExecutionRequest request,
        ManualTesterAgentToolExecutionOutput output)
    {
        var refs = new HashSet<string>(StringComparer.Ordinal)
        {
            request.ToolRequest.ToolRequestId,
            request.GateDecision.GateDecisionId,
            request.TestPlanRef,
            output.OutputId
        };

        foreach (var evidence in request.ToolRequest.Evidence)
            refs.Add(evidence.EvidenceId);

        foreach (var evidenceRef in output.EvidenceRefs)
            refs.Add(evidenceRef);

        return refs.Where(value => !string.IsNullOrWhiteSpace(value)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private static ManualTesterAgentToolExecutionStatus DetermineRejectedStatus(
        IReadOnlyList<ManualTesterAgentToolExecutionIssue> issues) =>
        issues.Any(issue => issue.Code is
            ManualTesterAgentToolExecutionValidator.GateNotAllowed or
            ManualTesterAgentToolExecutionValidator.GateActionFlagsUnsafe or
            ManualTesterAgentToolExecutionValidator.RequestGateMismatch)
            ? ManualTesterAgentToolExecutionStatus.Blocked
            : ManualTesterAgentToolExecutionStatus.InvalidRequest;

    private static ManualTesterAgentToolExecutionResult Rejected(
        ManualTesterAgentToolExecutionRequest? request,
        ManualTesterAgentToolExecutionStatus status,
        IReadOnlyList<ManualTesterAgentToolExecutionIssue> issues) =>
        new()
        {
            Succeeded = false,
            Status = status,
            ManualExecutionId = string.IsNullOrWhiteSpace(request?.ManualExecutionId) ? "missing-manual-execution-id" : request.ManualExecutionId,
            ToolRequestId = request?.ToolRequest?.ToolRequestId,
            GateDecisionId = request?.GateDecision?.GateDecisionId,
            Issues = issues
        };

    private static IReadOnlyList<ManualTesterAgentToolExecutionIssue> ToManualIssues(
        IReadOnlyList<AgentDefinitionValidationIssue> issues,
        string code) =>
        issues.Select(issue => new ManualTesterAgentToolExecutionIssue
        {
            Code = code,
            Severity = issue.Severity,
            Message = $"{issue.Code}: {issue.Message}",
            Field = code
        }).ToArray();
}
