using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class InterruptedRunReadModelValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "interrupted-run read model is read-only",
        "interrupted-run read model is metadata-only",
        "interrupted-run read model uses supplied checkpoint metadata only",
        "interrupted-run read model uses supplied diagnostic snapshot metadata only",
        "interrupted-run read model uses supplied AsOfUtc only",
        "interrupted-run read model is not operation identity",
        "interrupted-run read model is not operation lookup",
        "interrupted-run read model is not correlation authority",
        "interrupted-run read model is not timeline assembly",
        "interrupted-run read model is not status projection",
        "interrupted-run read model is not missing evidence calculation",
        "interrupted-run read model is not forbidden-action resolution",
        "interrupted-run read model is not receipt resolution",
        "interrupted-run read model is not evidence resolution",
        "interrupted-run read model is not validation staleness resolution",
        "interrupted-run read model is not patch/base freshness resolution",
        "interrupted-run read model is not worktree/base/head freshness resolution",
        "interrupted-run read model is not validation execution",
        "interrupted-run read model is not raw patch resolution",
        "interrupted-run read model is not raw diff resolution",
        "interrupted-run read model is not source inspection",
        "interrupted-run read model is not Git inspection",
        "interrupted-run read model is not policy satisfaction",
        "interrupted-run read model is not approval",
        "interrupted-run read model is not next-safe-action formatting",
        "interrupted-run read model is not authority-warning formatting",
        "interrupted-run read model is not source apply",
        "interrupted-run read model is not rollback",
        "interrupted-run read model is not retry permission",
        "interrupted-run read model is not resume permission",
        "interrupted-run read model is not recovery permission",
        "interrupted-run read model is not commit",
        "interrupted-run read model is not push",
        "interrupted-run read model is not PR creation",
        "interrupted-run read model is not merge readiness",
        "interrupted-run read model is not release readiness",
        "interrupted-run read model is not deployment readiness",
        "interrupted-run read model is not memory promotion",
        "interrupted-run read model is not workflow continuation",
        "interrupted state is not retry permission",
        "failed state is not recovery permission",
        "cancelled state is not resume permission",
        "apply-started-not-completed is not rollback authority",
        "commit-created-no-push is not push authority",
        "push-completed-no-PR is not PR creation authority",
        "no interruption observed is not action allowed",
        "complete assessment is not action allowed"
    ];

    public static InterruptedRunReadModel ValidateRequest(InterruptedRunReadModelRequest? request)
    {
        if (request is null)
        {
            return Invalid(["InterruptedRunReadModelRequestRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(request.TenantId, "InterruptedRunTenantIdRequired", "InterruptedRunTenantIdInvalid", issues);
        AddScopeIssues(request.ProjectId, "InterruptedRunProjectIdRequired", "InterruptedRunProjectIdInvalid", issues);
        AddOperationIdIssues(request.OperationId, issues);

        if (request.AsOfUtc == default)
        {
            issues.Add("InterruptedRunAsOfUtcRequired");
        }

        if (request.Checkpoints is null)
        {
            issues.Add("InterruptedRunCheckpointsRequired");
        }
        else
        {
            foreach (var checkpoint in request.Checkpoints)
            {
                AddCheckpointIssues(checkpoint, issues);
                if (checkpoint is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, checkpoint.TenantId))
                {
                    issues.Add("InterruptedRunCheckpointTenantMismatch");
                }

                if (!Same(request.ProjectId, checkpoint.ProjectId))
                {
                    issues.Add("InterruptedRunCheckpointProjectMismatch");
                }

                if (!Same(request.OperationId, checkpoint.OperationId))
                {
                    issues.Add("InterruptedRunCheckpointOperationMismatch");
                }
            }
        }

        if (request.DiagnosticSnapshot is not null)
        {
            AddDiagnosticSnapshotIssues(request, request.DiagnosticSnapshot, issues);
        }

        return Result(
            issues,
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            request.AsOfUtc,
            InterruptedRunReadModelStatus.InvalidRequest);
    }

    private static void AddCheckpointIssues(
        InterruptedRunCheckpointObservation? checkpoint,
        ICollection<string> issues)
    {
        if (checkpoint is null)
        {
            issues.Add("InterruptedRunCheckpointRequired");
            return;
        }

        AddScopeIssues(checkpoint.TenantId, "InterruptedRunCheckpointTenantIdRequired", "InterruptedRunCheckpointTenantIdInvalid", issues);
        AddScopeIssues(checkpoint.ProjectId, "InterruptedRunCheckpointProjectIdRequired", "InterruptedRunCheckpointProjectIdInvalid", issues);
        AddOperationIdIssues(checkpoint.OperationId, issues);
        AddCorrelationIdIssues(checkpoint.CorrelationId, checkpoint.OperationId, issues);
        AddIdIssues(checkpoint.CheckpointId, "InterruptedRunCheckpointIdRequired", "InterruptedRunCheckpointIdInvalid", issues);
        AddCheckpointKindIssues(checkpoint.CheckpointKind, issues);
        AddSurfaceIssues(checkpoint.SurfaceKind, checkpoint.SurfaceId, issues);
        AddReferencePairIssues(checkpoint.ReferenceKind, checkpoint.ReferenceId, issues);
        AddSourceIssues(checkpoint.Source, "InterruptedRunCheckpointSourceRequired", "InterruptedRunCheckpointSourceInvalid", issues);
        AddRedactionIssues(checkpoint.IsRedacted, checkpoint.RedactionReason, issues);

        if (checkpoint.AppendPosition < 0)
        {
            issues.Add("InterruptedRunCheckpointAppendPositionInvalid");
        }

        if (checkpoint.ObservedAtUtc == default)
        {
            issues.Add("InterruptedRunCheckpointObservedAtRequired");
        }

        if (checkpoint.RecordedAtUtc == default)
        {
            issues.Add("InterruptedRunCheckpointRecordedAtRequired");
        }

        if (checkpoint.ObservedAtUtc != default &&
            checkpoint.RecordedAtUtc != default &&
            checkpoint.RecordedAtUtc < checkpoint.ObservedAtUtc)
        {
            issues.Add("InterruptedRunCheckpointRecordedBeforeObserved");
        }
    }

    private static void AddDiagnosticSnapshotIssues(
        InterruptedRunReadModelRequest request,
        InterruptedRunDiagnosticSnapshot snapshot,
        ICollection<string> issues)
    {
        AddScopeIssues(snapshot.TenantId, "InterruptedRunDiagnosticTenantIdRequired", "InterruptedRunDiagnosticTenantIdInvalid", issues);
        AddScopeIssues(snapshot.ProjectId, "InterruptedRunDiagnosticProjectIdRequired", "InterruptedRunDiagnosticProjectIdInvalid", issues);
        AddOperationIdIssues(snapshot.OperationId, issues);
        AddSourceIssues(snapshot.Source, "InterruptedRunDiagnosticSourceRequired", "InterruptedRunDiagnosticSourceInvalid", issues);

        if (!Same(request.TenantId, snapshot.TenantId))
        {
            issues.Add("InterruptedRunDiagnosticTenantMismatch");
        }

        if (!Same(request.ProjectId, snapshot.ProjectId))
        {
            issues.Add("InterruptedRunDiagnosticProjectMismatch");
        }

        if (!Same(request.OperationId, snapshot.OperationId))
        {
            issues.Add("InterruptedRunDiagnosticOperationMismatch");
        }

        if (snapshot.RecordedAtUtc == default)
        {
            issues.Add("InterruptedRunDiagnosticRecordedAtRequired");
        }

        if (!Enum.IsDefined(snapshot.ProjectedStatusKind))
        {
            issues.Add("InterruptedRunDiagnosticProjectedStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.MissingEvidenceStatus))
        {
            issues.Add("InterruptedRunDiagnosticMissingEvidenceStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.ForbiddenActionStatus))
        {
            issues.Add("InterruptedRunDiagnosticForbiddenActionStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.ReceiptResolutionStatus))
        {
            issues.Add("InterruptedRunDiagnosticReceiptResolutionStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.EvidenceResolutionStatus))
        {
            issues.Add("InterruptedRunDiagnosticEvidenceResolutionStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.ValidationStalenessStatus))
        {
            issues.Add("InterruptedRunDiagnosticValidationStalenessStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.PatchBaseFreshnessStatus))
        {
            issues.Add("InterruptedRunDiagnosticPatchBaseFreshnessStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.WorktreeBaseHeadFreshnessStatus))
        {
            issues.Add("InterruptedRunDiagnosticWorktreeBaseHeadFreshnessStatusInvalid");
        }
    }

    private static void AddScopeIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(value) ||
            value.Any(char.IsWhiteSpace) ||
            value.Contains('*') ||
            value.Contains('?') ||
            value.Contains('/') ||
            value.Contains('\\') ||
            IsUrl(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddOperationIdIssues(string? operationId, ICollection<string> issues)
    {
        var result = OperationIdentityValidator.ValidateOperationId(operationId);
        foreach (var issue in result.Issues)
        {
            issues.Add(issue);
        }
    }

    private static void AddCorrelationIdIssues(
        string? correlationId,
        string? operationId,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            issues.Add("InterruptedRunCheckpointCorrelationIdRequired");
            return;
        }

        if (ContainsUnsafeText(correlationId) ||
            correlationId.Any(char.IsWhiteSpace) ||
            correlationId.Contains('/') ||
            correlationId.Contains('\\') ||
            IsUrl(correlationId) ||
            !string.IsNullOrWhiteSpace(operationId) &&
            Same(correlationId, operationId) ||
            !CanonicalCorrelationIdPattern().IsMatch(correlationId))
        {
            issues.Add("InterruptedRunCheckpointCorrelationIdInvalid");
        }
    }

    private static void AddIdIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(value) ||
            value.Any(char.IsWhiteSpace) ||
            IsUrl(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddCheckpointKindIssues(
        InterruptedRunCheckpointKind checkpointKind,
        ICollection<string> issues)
    {
        if (checkpointKind == InterruptedRunCheckpointKind.Unknown ||
            !Enum.IsDefined(checkpointKind))
        {
            issues.Add("InterruptedRunCheckpointKindRequired");
        }
    }

    private static void AddSurfaceIssues(
        OperationCorrelationSurfaceKind surfaceKind,
        string? surfaceId,
        ICollection<string> issues)
    {
        if (surfaceKind == OperationCorrelationSurfaceKind.Unknown ||
            !Enum.IsDefined(surfaceKind))
        {
            issues.Add("InterruptedRunCheckpointSurfaceKindRequired");
        }

        AddIdIssues(surfaceId, "InterruptedRunCheckpointSurfaceIdRequired", "InterruptedRunCheckpointSurfaceIdInvalid", issues);
    }

    private static void AddReferencePairIssues(
        OperationReferenceKind referenceKind,
        string? referenceId,
        ICollection<string> issues)
    {
        var hasKind = referenceKind != OperationReferenceKind.Unknown;
        var hasId = !string.IsNullOrWhiteSpace(referenceId);

        if (hasKind && !Enum.IsDefined(referenceKind))
        {
            issues.Add("InterruptedRunCheckpointReferenceKindInvalid");
        }

        if (hasKind && !hasId)
        {
            issues.Add("InterruptedRunCheckpointReferenceIdRequired");
        }

        if (!hasKind && hasId)
        {
            issues.Add("InterruptedRunCheckpointReferenceKindRequired");
        }

        if (hasId)
        {
            AddIdIssues(referenceId, "InterruptedRunCheckpointReferenceIdRequired", "InterruptedRunCheckpointReferenceIdInvalid", issues);
        }
    }

    private static void AddSourceIssues(
        string? value,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(value) ||
            value.Any(char.IsWhiteSpace))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddRedactionIssues(
        bool isRedacted,
        string? redactionReason,
        ICollection<string> issues)
    {
        if (isRedacted && string.IsNullOrWhiteSpace(redactionReason))
        {
            issues.Add("InterruptedRunCheckpointRedactionReasonRequired");
        }

        if (!string.IsNullOrWhiteSpace(redactionReason) &&
            ContainsUnsafeText(redactionReason))
        {
            issues.Add("InterruptedRunCheckpointRedactionReasonInvalid");
        }
    }

    private static InterruptedRunReadModel Invalid(IReadOnlyList<string> issues) =>
        Result(issues, string.Empty, string.Empty, string.Empty, default, InterruptedRunReadModelStatus.InvalidRequest);

    private static InterruptedRunReadModel Result(
        IReadOnlyList<string> issues,
        string tenantId,
        string projectId,
        string operationId,
        DateTimeOffset asOfUtc,
        InterruptedRunReadModelStatus status) =>
        new()
        {
            IsValid = issues.Count == 0,
            ResolutionStatus = issues.Count == 0 ? InterruptedRunReadModelStatus.NoCheckpoints : status,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = asOfUtc,
            Assessment = null,
            CheckpointIds = [],
            AmbiguousCheckpoints = [],
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = issues.Count == 0 ? Warnings() : [],
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    internal static IReadOnlyList<string> Warnings() =>
    [
        "interrupted-run state is metadata-only",
        "interrupted-run state uses supplied checkpoint metadata only",
        "interrupted-run state uses supplied diagnostic snapshot metadata only",
        "interrupted-run state uses supplied AsOfUtc only",
        "interrupted state is not retry permission",
        "interrupted state is not resume permission",
        "interrupted state is not recovery permission",
        "failed state is not recovery permission",
        "cancelled state is not resume permission",
        "apply-started-not-completed is not rollback authority",
        "commit-created-no-push is not push authority",
        "push-completed-no-PR is not PR creation authority",
        "no interruption observed is not action allowed"
    ];

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsUnsafeText(string value) =>
        value.Any(char.IsControl) ||
        value.Length > 512 ||
        ContainsAuthorityText(value) ||
        ContainsSecretMarker(value) ||
        ContainsRawPayloadMarker(value);

    private static bool ContainsAuthorityText(string value)
    {
        var markers = new[]
        {
            "approval granted",
            "approved for",
            "policy satisfied",
            "authority granted",
            "action allowed",
            "allowed to",
            "ready to apply",
            "ready to commit",
            "ready to push",
            "ready for review",
            "merge ready",
            "release ready",
            "deploy now",
            "continue workflow",
            "retry authorized",
            "resume authorized",
            "recovery authorized",
            "rollback authorized",
            "ship it"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsRawPayloadMarker(string value)
    {
        var markers = new[]
        {
            "raw source",
            "source content",
            "raw patch",
            "raw diff",
            "raw validation log",
            "raw evidence payload",
            "raw receipt payload",
            "raw request body",
            "raw response body",
            "full patch",
            "full diff",
            "hidden chain-of-thought",
            "private reasoning",
            "prompt text"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSecretMarker(string value)
    {
        var markers = new[]
        {
            "authorization:",
            "bearer ",
            "api key",
            "apikey",
            "password",
            "secret",
            "token=",
            "connection string",
            "private key"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUrl(string value) =>
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^corr_([0-9a-f]{16,64}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$")]
    private static partial Regex CanonicalCorrelationIdPattern();
}
