using System.Text.Json;

namespace IronDev.Core.Governance;

public static class GovernedReleaseGateStatuses
{
    public const string DecisionRecordStored = "DecisionRecordStored";
    public const string Rejected = "Rejected";
    public const string DecisionRecordSaveFailed = "DecisionRecordSaveFailed";
    public const string DecisionRecordReadBackFailed = "DecisionRecordReadBackFailed";
}

public static class GovernedReleaseGateBoundaryText
{
    public const string Boundary = """
        Governed release gate evaluates release-readiness evidence only.
        Governed release gate may emit and store a ReleaseReadinessDecisionRecord.
        Governed release gate is not release approval.
        Governed release gate is not deployment approval.
        Governed release gate is not merge approval.
        Governed release gate is not release execution.
        Governed release gate is not source apply.
        Governed release gate is not rollback execution.
        Governed release gate is not workflow continuation.
        Governed release gate does not mutate workflow state.
        Governed release gate does not run git.
        Governed release gate does not call agents, models, tools, UI, memory, or retrieval.
        Human review remains required for release approval, deployment, and merge.
        """;

    public static IReadOnlyList<string> Warnings { get; } =
    [
        "Governed release gate evaluates release-readiness evidence only.",
        "A stored ReleaseReadinessDecisionRecord is evidence, not release approval.",
        "ReadyEvidenceSatisfied means evidence satisfied only.",
        "Release, deployment, and merge approval remain false.",
        "Release, source apply, rollback, workflow, and git execution remain false.",
        "Human review remains required for release approval, deployment, and merge."
    ];
}

public sealed record GovernedReleaseGateRequest
{
    public required Guid GovernedReleaseGateRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required ReleaseReadinessReport ReleaseReadinessReport { get; init; }
    public required string RequestedBy { get; init; }
    public required DateTimeOffset RequestedAtUtc { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }
    public string Boundary { get; init; } = GovernedReleaseGateBoundaryText.Boundary;
}

public sealed record GovernedReleaseGateResult
{
    public required Guid GovernedReleaseGateRequestId { get; init; }
    public required Guid ProjectId { get; init; }
    public required bool Succeeded { get; init; }
    public required string Status { get; init; }
    public required bool ReleaseReadinessGateRan { get; init; }
    public required bool DecisionRecordStored { get; init; }
    public required ReleaseReadinessDecisionRecord? DecisionRecord { get; init; }
    public required bool ReleaseReadinessEvidenceSatisfied { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool DeploymentApproved { get; init; }
    public required bool MergeApproved { get; init; }
    public required bool ReleaseExecuted { get; init; }
    public required bool SourceApplyExecuted { get; init; }
    public required bool RollbackExecuted { get; init; }
    public required bool WorkflowContinued { get; init; }
    public required bool WorkflowMutated { get; init; }
    public required bool GitOperationExecuted { get; init; }
    public required bool HumanReviewRequiredForReleaseApproval { get; init; }
    public required bool HumanReviewRequiredForDeployment { get; init; }
    public required bool HumanReviewRequiredForMerge { get; init; }
    public required IReadOnlyList<GovernedReleaseGateIssue> Issues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public string Boundary { get; init; } = GovernedReleaseGateBoundaryText.Boundary;
}

public sealed record GovernedReleaseGateIssue(string Code, string Field, string Message);

public interface IGovernedReleaseGateService
{
    Task<GovernedReleaseGateResult> EvaluateAsync(
        GovernedReleaseGateRequest? request,
        CancellationToken cancellationToken = default);
}

public static class GovernedReleaseGateValidation
{
    private static readonly string[] PrivateOrRawMarkers =
    [
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "private reasoning",
        "hidden reasoning",
        "scratchpad",
        "entire patch",
        "entirepatch",
        "patch payload",
        "patchpayload",
        "password",
        "api_key",
        "apikey",
        "secret",
        "private key",
        "bearer"
    ];

    private static readonly string[] AuthorityMarkers =
    [
        "release approved",
        "approved for release",
        "deployment approved",
        "merge approved",
        "safe to deploy",
        "safe to merge",
        "can deploy",
        "can merge",
        "green to ship",
        "release executed",
        "source applied by decision",
        "rollback executed by decision",
        "workflow continued by decision",
        "git committed",
        "git pushed",
        "tag created",
        "pull request created",
        "memory promoted",
        "retrieval activated",
        "agent dispatched",
        "tool executed",
        "model called"
    ];

    public static IReadOnlyList<GovernedReleaseGateIssue> ValidateRequest(GovernedReleaseGateRequest? request)
    {
        var issues = new List<GovernedReleaseGateIssue>();
        if (request is null)
        {
            Add(issues, "RequestRequired", "request", "Governed release gate request is required.");
            return issues;
        }

        RequireGuid(request.GovernedReleaseGateRequestId, nameof(request.GovernedReleaseGateRequestId), issues);
        RequireGuid(request.ProjectId, nameof(request.ProjectId), issues);
        RequireText(request.RequestedBy, nameof(request.RequestedBy), issues);
        if (request.RequestedAtUtc == default)
            Add(issues, "RequestedAtRequired", nameof(request.RequestedAtUtc), "Requested timestamp is required.");
        RequireList(request.EvidenceReferences, nameof(request.EvidenceReferences), issues);
        RequireList(request.BoundaryMaxims, nameof(request.BoundaryMaxims), issues);
        RequireText(request.Boundary, nameof(request.Boundary), issues);
        ScanText(request.RequestedBy, nameof(request.RequestedBy), issues);
        ScanText(request.Boundary, nameof(request.Boundary), issues);
        ScanTexts(request.EvidenceReferences, nameof(request.EvidenceReferences), issues);
        ScanTexts(request.BoundaryMaxims, nameof(request.BoundaryMaxims), issues);

        if (request.ReleaseReadinessReport is null)
        {
            Add(issues, "ReleaseReadinessReportRequired", nameof(request.ReleaseReadinessReport), "Release readiness report is required.");
            return issues;
        }

        if (request.ReleaseReadinessReport.ProjectId != request.ProjectId)
            Add(issues, "ProjectMismatch", nameof(request.ProjectId), "Request project must match release readiness report project.");

        foreach (var issue in ReleaseReadinessReportValidation.Validate(request.ReleaseReadinessReport).Issues)
            Add(issues, $"ReleaseReadinessReport.{issue.Code}", $"ReleaseReadinessReport.{issue.Field}", "Release readiness report validation failed.");

        var expectedReportHash = ReleaseReadinessReportHashing.ComputeReportHash(request.ReleaseReadinessReport);
        if (!string.Equals(expectedReportHash, request.ReleaseReadinessReport.ReleaseReadinessReportHash, StringComparison.Ordinal))
            Add(issues, "ReleaseReadinessReportHashMismatch", nameof(request.ReleaseReadinessReport.ReleaseReadinessReportHash), "Release readiness report hash does not match report content.");

        ScanSerializedRequest(request, issues);
        return issues;
    }

    public static void ScanExternalText(string? value, string field, List<GovernedReleaseGateIssue> issues) =>
        ScanText(value, field, issues);

    private static void ScanSerializedRequest(GovernedReleaseGateRequest request, List<GovernedReleaseGateIssue> issues)
    {
        var serialized = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        ScanText(serialized, "request", issues);
    }

    private static void RequireGuid(Guid value, string field, List<GovernedReleaseGateIssue> issues)
    {
        if (value == Guid.Empty)
            Add(issues, $"{field}Required", field, $"{field} is required.");
    }

    private static void RequireText(string? value, string field, List<GovernedReleaseGateIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            Add(issues, $"{field}Required", field, $"{field} is required.");
        else
            ScanText(value, field, issues);
    }

    private static void RequireList(IReadOnlyList<string>? values, string field, List<GovernedReleaseGateIssue> issues)
    {
        if (values is null || values.Count == 0 || values.Any(string.IsNullOrWhiteSpace))
        {
            Add(issues, $"{field}Required", field, $"{field} is required.");
            return;
        }

        ScanTexts(values, field, issues);
    }

    private static void ScanTexts(IEnumerable<string>? values, string field, List<GovernedReleaseGateIssue> issues)
    {
        if (values is null)
            return;

        foreach (var value in values)
            ScanText(value, field, issues);
    }

    private static void ScanText(string? value, string field, List<GovernedReleaseGateIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (PrivateOrRawMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            Add(issues, "UnsafeRequestMaterialRejected", field, "Governed release gate request contains private, raw, prompt, scratchpad, patch, or secret-like material.");

        if (AuthorityMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            Add(issues, "AuthorityRequestMaterialRejected", field, "Governed release gate request must not claim release, deployment, merge, execution, git, memory, retrieval, agent, model, or tool authority.");
    }

    private static void Add(List<GovernedReleaseGateIssue> issues, string code, string field, string message) =>
        issues.Add(new GovernedReleaseGateIssue(code, field, message));
}
