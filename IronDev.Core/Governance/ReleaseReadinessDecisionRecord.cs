using System.Security.Cryptography;
using System.Text;

namespace IronDev.Core.Governance;

public static class ReleaseReadinessDecisionStatuses
{
    public const string ReadyEvidenceSatisfied = "ReadyEvidenceSatisfied";
    public const string BlockedByMissingEvidence = "BlockedByMissingEvidence";
    public const string BlockedByFailedEvidence = "BlockedByFailedEvidence";
    public const string BlockedByHumanReviewRequired = "BlockedByHumanReviewRequired";
}

public static class ReleaseReadinessDecisionReasonSeverities
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Blocking = "Blocking";
}

public static class ReleaseReadinessDecisionRecordBoundaryText
{
    public const string Boundary = """
        Release readiness decision record is decision evidence shape only.
        Release readiness decision record contract is not the release readiness gate.
        Release readiness decision record contract does not decide readiness.
        Release readiness decision record is not release approval.
        Release readiness decision record is not deployment approval.
        Release readiness decision record is not merge approval.
        Release readiness decision record is not source apply.
        Release readiness decision record is not rollback execution.
        Release readiness decision record is not workflow continuation.
        Release readiness decision record does not mutate workflow state.
        Release readiness decision record does not execute release.
        Release readiness decision record does not run git.
        Release readiness decision record does not call agents, models, tools, API, CLI, UI, memory, or retrieval.
        Human review remains required for release approval, deployment, and merge.
        """;
}

public sealed record ReleaseReadinessDecisionReason
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public sealed record ReleaseReadinessDecisionRecord
{
    public required Guid ReleaseReadinessDecisionRecordId { get; init; }
    public required Guid ProjectId { get; init; }

    public required Guid ReleaseReadinessReportId { get; init; }
    public required string ReleaseReadinessReportHash { get; init; }

    public required string WorkflowRunId { get; init; }
    public required string WorkflowStepId { get; init; }
    public required string SubjectKind { get; init; }
    public required string SubjectId { get; init; }
    public required string SubjectHash { get; init; }

    public required string DecisionStatus { get; init; }

    public required bool ReleaseReadinessEvidenceSatisfied { get; init; }
    public required bool ReleaseApproved { get; init; }
    public required bool DeploymentApproved { get; init; }
    public required bool MergeApproved { get; init; }

    public required bool SourceApplyExecutedByDecision { get; init; }
    public required bool RollbackExecutedByDecision { get; init; }
    public required bool WorkflowMutatedByDecision { get; init; }
    public required bool GitOperationExecutedByDecision { get; init; }
    public required bool ReleaseExecutedByDecision { get; init; }

    public required bool HumanReviewRequiredForReleaseApproval { get; init; }
    public required bool HumanReviewRequiredForDeployment { get; init; }
    public required bool HumanReviewRequiredForMerge { get; init; }

    public required IReadOnlyList<ReleaseReadinessDecisionReason> Reasons { get; init; }
    public required IReadOnlyList<string> EvidenceReferences { get; init; }
    public required IReadOnlyList<string> BoundaryMaxims { get; init; }

    public required DateTimeOffset DecidedAtUtc { get; init; }
    public required string ReleaseReadinessDecisionRecordHash { get; init; }

    public string Boundary { get; init; } = ReleaseReadinessDecisionRecordBoundaryText.Boundary;
}

public sealed record ReleaseReadinessDecisionRecordValidationIssue
{
    public required string Code { get; init; }
    public required string Field { get; init; }
    public required string Message { get; init; }
}

public sealed record ReleaseReadinessDecisionRecordValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<ReleaseReadinessDecisionRecordValidationIssue> Issues { get; init; }
}

public static class ReleaseReadinessDecisionRecordHashing
{
    public static string ComputeHash(ReleaseReadinessDecisionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        var canonical = string.Join(
            "\n",
            new[]
            {
                $"ReleaseReadinessDecisionRecordId={record.ReleaseReadinessDecisionRecordId:D}",
                $"ProjectId={record.ProjectId:D}",
                $"ReleaseReadinessReportId={record.ReleaseReadinessReportId:D}",
                $"ReleaseReadinessReportHash={Normalize(record.ReleaseReadinessReportHash)}",
                $"WorkflowRunId={Normalize(record.WorkflowRunId)}",
                $"WorkflowStepId={Normalize(record.WorkflowStepId)}",
                $"SubjectKind={Normalize(record.SubjectKind)}",
                $"SubjectId={Normalize(record.SubjectId)}",
                $"SubjectHash={Normalize(record.SubjectHash)}",
                $"DecisionStatus={Normalize(record.DecisionStatus)}",
                $"ReleaseReadinessEvidenceSatisfied={record.ReleaseReadinessEvidenceSatisfied}",
                $"ReleaseApproved={record.ReleaseApproved}",
                $"DeploymentApproved={record.DeploymentApproved}",
                $"MergeApproved={record.MergeApproved}",
                $"SourceApplyExecutedByDecision={record.SourceApplyExecutedByDecision}",
                $"RollbackExecutedByDecision={record.RollbackExecutedByDecision}",
                $"WorkflowMutatedByDecision={record.WorkflowMutatedByDecision}",
                $"GitOperationExecutedByDecision={record.GitOperationExecutedByDecision}",
                $"ReleaseExecutedByDecision={record.ReleaseExecutedByDecision}",
                $"HumanReviewRequiredForReleaseApproval={record.HumanReviewRequiredForReleaseApproval}",
                $"HumanReviewRequiredForDeployment={record.HumanReviewRequiredForDeployment}",
                $"HumanReviewRequiredForMerge={record.HumanReviewRequiredForMerge}",
                $"Reasons={NormalizeReasons(record.Reasons)}",
                $"EvidenceReferences={NormalizeList(record.EvidenceReferences)}",
                $"BoundaryMaxims={NormalizeList(record.BoundaryMaxims)}",
                $"DecidedAtUtc={record.DecidedAtUtc.ToUniversalTime():O}",
                $"Boundary={Normalize(record.Boundary)}",
            });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim();

    private static string NormalizeList(IReadOnlyList<string>? values)
        => values is null
            ? string.Empty
            : string.Join("|", values.Select(Normalize));

    private static string NormalizeReasons(IReadOnlyList<ReleaseReadinessDecisionReason>? reasons)
        => reasons is null
            ? string.Empty
            : string.Join(
                "|",
                reasons.Select(reason =>
                    $"{Normalize(reason.Code)}:{Normalize(reason.Severity)}:{Normalize(reason.Field)}:{Normalize(reason.Message)}"));
}

public static class ReleaseReadinessDecisionRecordValidation
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.Ordinal)
    {
        ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied,
        ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence,
        ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence,
        ReleaseReadinessDecisionStatuses.BlockedByHumanReviewRequired,
    };

    private static readonly HashSet<string> ValidSeverities = new(StringComparer.Ordinal)
    {
        ReleaseReadinessDecisionReasonSeverities.Info,
        ReleaseReadinessDecisionReasonSeverities.Warning,
        ReleaseReadinessDecisionReasonSeverities.Blocking,
    };

    private static readonly string[] PrivateRawMarkers =
    {
        "raw prompt",
        "rawprompt",
        "raw completion",
        "rawcompletion",
        "raw tool output",
        "rawtooloutput",
        "chain-of-thought",
        "chain of thought",
        "chainofthought",
        "scratchpad",
        "private reasoning",
        "hidden reasoning",
        "system prompt",
        "developer prompt",
        "entire patch",
        "entirepatch",
        "patch payload",
        "patchpayload",
        "password",
        "api_key",
        "secret",
        "private key",
        "bearer",
    };

    private static readonly string[] AuthorityMarkers =
    {
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
        "deployed by decision",
        "merged by decision",
        "source applied by decision",
        "rollback executed by decision",
        "workflow continued by decision",
        "git " + "committed",
        "git " + "pushed",
        "tag created",
        "pull request created",
        "memory promoted",
        "retrieval activated",
        "agent dispatched",
        "tool executed",
        "model called",
    };

    private static readonly string[] SafeAuthorityPrefixes =
    {
        "not ",
        "no ",
        "does not ",
        "do not ",
        "must not ",
        "never ",
        "without ",
    };

    public static ReleaseReadinessDecisionRecordValidationResult Validate(ReleaseReadinessDecisionRecord? record)
    {
        var issues = new List<ReleaseReadinessDecisionRecordValidationIssue>();

        if (record is null)
        {
            Add(issues, "ReleaseReadinessDecisionRecordRequired", "record", "Release readiness decision record is required.");
            return new ReleaseReadinessDecisionRecordValidationResult
            {
                IsValid = false,
                Issues = issues,
            };
        }

        RequireNonEmptyGuid(issues, record.ReleaseReadinessDecisionRecordId, nameof(record.ReleaseReadinessDecisionRecordId));
        RequireNonEmptyGuid(issues, record.ProjectId, nameof(record.ProjectId));
        RequireNonEmptyGuid(issues, record.ReleaseReadinessReportId, nameof(record.ReleaseReadinessReportId));

        RequireHash(issues, record.ReleaseReadinessReportHash, nameof(record.ReleaseReadinessReportHash));
        RequireText(issues, record.WorkflowRunId, nameof(record.WorkflowRunId));
        RequireText(issues, record.WorkflowStepId, nameof(record.WorkflowStepId));
        RequireText(issues, record.SubjectKind, nameof(record.SubjectKind));
        RequireText(issues, record.SubjectId, nameof(record.SubjectId));
        RequireHash(issues, record.SubjectHash, nameof(record.SubjectHash));
        RequireText(issues, record.Boundary, nameof(record.Boundary));

        if (!ValidStatuses.Contains(record.DecisionStatus))
        {
            Add(issues, "UnknownDecisionStatus", nameof(record.DecisionStatus), "Decision status is not supported.");
        }

        if (record.Reasons is null || record.Reasons.Count == 0)
        {
            Add(issues, "ReasonsRequired", nameof(record.Reasons), "At least one decision reason is required.");
        }
        else
        {
            for (var index = 0; index < record.Reasons.Count; index++)
            {
                ValidateReason(issues, record.Reasons[index], index);
            }
        }

        ValidateRequiredStringList(issues, record.EvidenceReferences, nameof(record.EvidenceReferences));
        ValidateRequiredStringList(issues, record.BoundaryMaxims, nameof(record.BoundaryMaxims));

        RejectTrue(issues, record.ReleaseApproved, nameof(record.ReleaseApproved), "Release approval cannot be created by a readiness decision record.");
        RejectTrue(issues, record.DeploymentApproved, nameof(record.DeploymentApproved), "Deployment approval cannot be created by a readiness decision record.");
        RejectTrue(issues, record.MergeApproved, nameof(record.MergeApproved), "Merge approval cannot be created by a readiness decision record.");
        RejectTrue(issues, record.SourceApplyExecutedByDecision, nameof(record.SourceApplyExecutedByDecision), "Source apply cannot be executed by a readiness decision record.");
        RejectTrue(issues, record.RollbackExecutedByDecision, nameof(record.RollbackExecutedByDecision), "Rollback cannot be executed by a readiness decision record.");
        RejectTrue(issues, record.WorkflowMutatedByDecision, nameof(record.WorkflowMutatedByDecision), "Workflow state cannot be mutated by a readiness decision record.");
        RejectTrue(issues, record.GitOperationExecutedByDecision, nameof(record.GitOperationExecutedByDecision), "Git operations cannot be executed by a readiness decision record.");
        RejectTrue(issues, record.ReleaseExecutedByDecision, nameof(record.ReleaseExecutedByDecision), "Release execution cannot be performed by a readiness decision record.");

        RequireTrue(issues, record.HumanReviewRequiredForReleaseApproval, nameof(record.HumanReviewRequiredForReleaseApproval), "Human review remains required for release approval.");
        RequireTrue(issues, record.HumanReviewRequiredForDeployment, nameof(record.HumanReviewRequiredForDeployment), "Human review remains required for deployment.");
        RequireTrue(issues, record.HumanReviewRequiredForMerge, nameof(record.HumanReviewRequiredForMerge), "Human review remains required for merge.");

        ValidateStatusConsistency(issues, record);
        ValidateTextSafety(issues, record);
        ValidateHash(issues, record);

        return new ReleaseReadinessDecisionRecordValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues,
        };
    }

    private static void ValidateReason(List<ReleaseReadinessDecisionRecordValidationIssue> issues, ReleaseReadinessDecisionReason? reason, int index)
    {
        var prefix = $"{nameof(ReleaseReadinessDecisionRecord.Reasons)}[{index}]";
        if (reason is null)
        {
            Add(issues, "ReasonRequired", prefix, "Decision reason is required.");
            return;
        }

        RequireText(issues, reason.Code, $"{prefix}.{nameof(reason.Code)}");
        RequireText(issues, reason.Field, $"{prefix}.{nameof(reason.Field)}");
        RequireText(issues, reason.Message, $"{prefix}.{nameof(reason.Message)}");

        if (!ValidSeverities.Contains(reason.Severity))
        {
            Add(issues, "UnknownReasonSeverity", $"{prefix}.{nameof(reason.Severity)}", "Reason severity is not supported.");
        }
    }

    private static void ValidateRequiredStringList(
        List<ReleaseReadinessDecisionRecordValidationIssue> issues,
        IReadOnlyList<string>? values,
        string field)
    {
        if (values is null || values.Count == 0)
        {
            Add(issues, $"{field}Required", field, $"{field} is required.");
            return;
        }

        for (var index = 0; index < values.Count; index++)
        {
            RequireText(issues, values[index], $"{field}[{index}]");
        }
    }

    private static void ValidateStatusConsistency(
        List<ReleaseReadinessDecisionRecordValidationIssue> issues,
        ReleaseReadinessDecisionRecord record)
    {
        var hasBlockingReason = record.Reasons?.Any(reason => reason.Severity == ReleaseReadinessDecisionReasonSeverities.Blocking) == true;
        var hasHumanReviewReason = record.Reasons?.Any(reason =>
            ContainsIgnoreCase(reason.Code, "HumanReview") ||
            ContainsIgnoreCase(reason.Message, "human review")) == true;

        switch (record.DecisionStatus)
        {
            case ReleaseReadinessDecisionStatuses.ReadyEvidenceSatisfied:
                if (!record.ReleaseReadinessEvidenceSatisfied)
                {
                    Add(issues, "ReadyEvidenceSatisfiedRequiresEvidenceSatisfied", nameof(record.ReleaseReadinessEvidenceSatisfied), "ReadyEvidenceSatisfied requires satisfied evidence.");
                }

                if (hasBlockingReason)
                {
                    Add(issues, "ReadyEvidenceSatisfiedRejectsBlockingReasons", nameof(record.Reasons), "ReadyEvidenceSatisfied cannot contain blocking reasons.");
                }

                break;

            case ReleaseReadinessDecisionStatuses.BlockedByMissingEvidence:
            case ReleaseReadinessDecisionStatuses.BlockedByFailedEvidence:
                if (record.ReleaseReadinessEvidenceSatisfied)
                {
                    Add(issues, "BlockedStatusRejectsEvidenceSatisfiedTrue", nameof(record.ReleaseReadinessEvidenceSatisfied), "Blocked statuses cannot mark readiness evidence satisfied.");
                }

                if (!hasBlockingReason)
                {
                    Add(issues, "BlockedStatusRequiresBlockingReason", nameof(record.Reasons), "Blocked statuses require at least one blocking reason.");
                }

                break;

            case ReleaseReadinessDecisionStatuses.BlockedByHumanReviewRequired:
                if (!hasHumanReviewReason)
                {
                    Add(issues, "HumanReviewStatusRequiresHumanReviewReason", nameof(record.Reasons), "Human-review blocked status requires a human-review reason.");
                }

                break;
        }
    }

    private static void ValidateHash(
        List<ReleaseReadinessDecisionRecordValidationIssue> issues,
        ReleaseReadinessDecisionRecord record)
    {
        RequireHash(issues, record.ReleaseReadinessDecisionRecordHash, nameof(record.ReleaseReadinessDecisionRecordHash));
        if (!IsSha256(record.ReleaseReadinessDecisionRecordHash))
        {
            return;
        }

        var expected = ReleaseReadinessDecisionRecordHashing.ComputeHash(record);
        if (!string.Equals(record.ReleaseReadinessDecisionRecordHash, expected, StringComparison.OrdinalIgnoreCase))
        {
            Add(issues, "ReleaseReadinessDecisionRecordHashMismatch", nameof(record.ReleaseReadinessDecisionRecordHash), "Release readiness decision record hash does not match record content.");
        }
    }

    private static void ValidateTextSafety(
        List<ReleaseReadinessDecisionRecordValidationIssue> issues,
        ReleaseReadinessDecisionRecord record)
    {
        foreach (var (field, value) in EnumerateText(record))
        {
            if (ContainsPrivateRawMarker(value))
            {
                Add(issues, "PrivateRawMaterialRejected", field, "Private, raw, secret, or prompt material is not allowed in release readiness decision records.");
            }

            if (ContainsForbiddenAuthorityMarker(value))
            {
                Add(issues, "AuthorityClaimRejected", field, "Release approval, execution, source, rollback, workflow, git, memory, retrieval, agent, model, or tool authority claims are not allowed.");
            }
        }
    }

    private static IEnumerable<(string Field, string? Value)> EnumerateText(ReleaseReadinessDecisionRecord record)
    {
        yield return (nameof(record.ReleaseReadinessReportHash), record.ReleaseReadinessReportHash);
        yield return (nameof(record.WorkflowRunId), record.WorkflowRunId);
        yield return (nameof(record.WorkflowStepId), record.WorkflowStepId);
        yield return (nameof(record.SubjectKind), record.SubjectKind);
        yield return (nameof(record.SubjectId), record.SubjectId);
        yield return (nameof(record.SubjectHash), record.SubjectHash);
        yield return (nameof(record.DecisionStatus), record.DecisionStatus);
        yield return (nameof(record.Boundary), record.Boundary);
        yield return (nameof(record.ReleaseReadinessDecisionRecordHash), record.ReleaseReadinessDecisionRecordHash);

        if (record.Reasons is not null)
        {
            for (var index = 0; index < record.Reasons.Count; index++)
            {
                var reason = record.Reasons[index];
                yield return ($"{nameof(record.Reasons)}[{index}].{nameof(reason.Code)}", reason.Code);
                yield return ($"{nameof(record.Reasons)}[{index}].{nameof(reason.Severity)}", reason.Severity);
                yield return ($"{nameof(record.Reasons)}[{index}].{nameof(reason.Field)}", reason.Field);
                yield return ($"{nameof(record.Reasons)}[{index}].{nameof(reason.Message)}", reason.Message);
            }
        }

        if (record.EvidenceReferences is not null)
        {
            for (var index = 0; index < record.EvidenceReferences.Count; index++)
            {
                yield return ($"{nameof(record.EvidenceReferences)}[{index}]", record.EvidenceReferences[index]);
            }
        }

        if (record.BoundaryMaxims is not null)
        {
            for (var index = 0; index < record.BoundaryMaxims.Count; index++)
            {
                yield return ($"{nameof(record.BoundaryMaxims)}[{index}]", record.BoundaryMaxims[index]);
            }
        }
    }

    private static bool ContainsPrivateRawMarker(string? value)
        => ContainsAnyMarker(value, PrivateRawMarkers);

    private static bool ContainsForbiddenAuthorityMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeForMarkerSearch(value);
        foreach (var marker in AuthorityMarkers.Select(NormalizeForMarkerSearch))
        {
            var index = normalized.IndexOf(marker, StringComparison.Ordinal);
            while (index >= 0)
            {
                var prefix = normalized[..index].TrimEnd();
                if (!SafeAuthorityPrefixes.Any(safePrefix => prefix.EndsWith(safePrefix, StringComparison.Ordinal)))
                {
                    return true;
                }

                index = normalized.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
            }
        }

        return false;
    }

    private static bool ContainsAnyMarker(string? value, IReadOnlyList<string> markers)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = NormalizeForMarkerSearch(value);
        return markers.Select(NormalizeForMarkerSearch).Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
    }

    private static string NormalizeForMarkerSearch(string value)
        => value.Trim().ToLowerInvariant().Replace("_", " ", StringComparison.Ordinal);

    private static bool ContainsIgnoreCase(string? value, string marker)
        => value?.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void RequireNonEmptyGuid(
        List<ReleaseReadinessDecisionRecordValidationIssue> issues,
        Guid value,
        string field)
    {
        if (value == Guid.Empty)
        {
            Add(issues, $"{field}Required", field, $"{field} is required.");
        }
    }

    private static void RequireText(
        List<ReleaseReadinessDecisionRecordValidationIssue> issues,
        string? value,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, $"{field}Required", field, $"{field} is required.");
        }
    }

    private static void RequireHash(
        List<ReleaseReadinessDecisionRecordValidationIssue> issues,
        string? value,
        string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(issues, $"{field}Required", field, $"{field} is required.");
            return;
        }

        if (!IsSha256(value))
        {
            Add(issues, $"{field}Invalid", field, $"{field} must be a SHA-256 hex string.");
        }
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void RejectTrue(
        List<ReleaseReadinessDecisionRecordValidationIssue> issues,
        bool value,
        string field,
        string message)
    {
        if (value)
        {
            Add(issues, $"{field}Rejected", field, message);
        }
    }

    private static void RequireTrue(
        List<ReleaseReadinessDecisionRecordValidationIssue> issues,
        bool value,
        string field,
        string message)
    {
        if (!value)
        {
            Add(issues, $"{field}Required", field, message);
        }
    }

    private static void Add(
        List<ReleaseReadinessDecisionRecordValidationIssue> issues,
        string code,
        string field,
        string message)
        => issues.Add(new ReleaseReadinessDecisionRecordValidationIssue
        {
            Code = code,
            Field = field,
            Message = message,
        });
}
