using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class RollbackRecoveryReadModelValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "rollback/recovery read model is read-only",
        "rollback/recovery read model is metadata-only",
        "rollback/recovery read model uses supplied material metadata only",
        "rollback/recovery read model uses supplied diagnostic snapshot metadata only",
        "rollback/recovery read model uses supplied AsOfUtc only",
        "rollback/recovery read model is not operation identity",
        "rollback/recovery read model is not operation lookup",
        "rollback/recovery read model is not correlation authority",
        "rollback/recovery read model is not timeline assembly",
        "rollback/recovery read model is not status projection",
        "rollback/recovery read model is not missing evidence calculation",
        "rollback/recovery read model is not forbidden-action resolution",
        "rollback/recovery read model is not receipt resolution",
        "rollback/recovery read model is not evidence resolution",
        "rollback/recovery read model is not validation staleness resolution",
        "rollback/recovery read model is not patch/base freshness resolution",
        "rollback/recovery read model is not worktree/base/head freshness resolution",
        "rollback/recovery read model is not interrupted-run resolution",
        "rollback/recovery read model is not validation execution",
        "rollback/recovery read model is not raw patch resolution",
        "rollback/recovery read model is not raw diff resolution",
        "rollback/recovery read model is not source inspection",
        "rollback/recovery read model is not Git inspection",
        "rollback/recovery read model is not policy satisfaction",
        "rollback/recovery read model is not approval",
        "rollback/recovery read model is not next-safe-action formatting",
        "rollback/recovery read model is not authority-warning formatting",
        "rollback/recovery read model is not source apply",
        "rollback/recovery read model is not rollback execution",
        "rollback/recovery read model is not recovery execution",
        "rollback/recovery read model is not retry permission",
        "rollback/recovery read model is not resume permission",
        "rollback/recovery read model is not commit",
        "rollback/recovery read model is not push",
        "rollback/recovery read model is not PR creation",
        "rollback/recovery read model is not merge readiness",
        "rollback/recovery read model is not release readiness",
        "rollback/recovery read model is not deployment readiness",
        "rollback/recovery read model is not memory promotion",
        "rollback/recovery read model is not workflow continuation",
        "rollback plan observed is not rollback authority",
        "rollback evidence observed is not rollback execution proof",
        "rollback receipt observed is not rollback permission",
        "recovery plan observed is not recovery authority",
        "recovery evidence observed is not recovery execution proof",
        "recovery receipt observed is not recovery permission",
        "rollback failed is not retry authority",
        "recovery failed is not resume authority",
        "no missing material is not action allowed",
        "complete assessment is not action allowed"
    ];

    public static RollbackRecoveryReadModel ValidateRequest(RollbackRecoveryReadModelRequest? request)
    {
        if (request is null)
        {
            return Invalid(["RollbackRecoveryReadModelRequestRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(request.TenantId, "RollbackRecoveryTenantIdRequired", "RollbackRecoveryTenantIdInvalid", issues);
        AddScopeIssues(request.ProjectId, "RollbackRecoveryProjectIdRequired", "RollbackRecoveryProjectIdInvalid", issues);
        AddOperationIdIssues(request.OperationId, issues);

        if (request.AsOfUtc == default)
        {
            issues.Add("RollbackRecoveryAsOfUtcRequired");
        }

        if (request.Materials is null)
        {
            issues.Add("RollbackRecoveryMaterialsRequired");
        }
        else
        {
            foreach (var material in request.Materials)
            {
                AddMaterialIssues(material, issues);
                if (material is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, material.TenantId))
                {
                    issues.Add("RollbackRecoveryMaterialTenantMismatch");
                }

                if (!Same(request.ProjectId, material.ProjectId))
                {
                    issues.Add("RollbackRecoveryMaterialProjectMismatch");
                }

                if (!Same(request.OperationId, material.OperationId))
                {
                    issues.Add("RollbackRecoveryMaterialOperationMismatch");
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
            RollbackRecoveryReadModelStatus.InvalidRequest);
    }

    private static void AddMaterialIssues(
        RollbackRecoveryMaterialObservation? material,
        ICollection<string> issues)
    {
        if (material is null)
        {
            issues.Add("RollbackRecoveryMaterialRequired");
            return;
        }

        AddScopeIssues(material.TenantId, "RollbackRecoveryMaterialTenantIdRequired", "RollbackRecoveryMaterialTenantIdInvalid", issues);
        AddScopeIssues(material.ProjectId, "RollbackRecoveryMaterialProjectIdRequired", "RollbackRecoveryMaterialProjectIdInvalid", issues);
        AddOperationIdIssues(material.OperationId, issues);
        AddCorrelationIdIssues(material.CorrelationId, material.OperationId, issues);
        AddIdIssues(material.MaterialId, "RollbackRecoveryMaterialIdRequired", "RollbackRecoveryMaterialIdInvalid", issues);
        AddMaterialKindIssues(material.MaterialKind, issues);
        AddSurfaceIssues(material.SurfaceKind, material.SurfaceId, issues);
        AddReferencePairIssues(material.ReferenceKind, material.ReferenceId, issues);
        AddSourceIssues(material.Source, "RollbackRecoveryMaterialSourceRequired", "RollbackRecoveryMaterialSourceInvalid", issues);
        AddRedactionIssues(material.IsRedacted, material.RedactionReason, issues);

        if (material.AppendPosition < 0)
        {
            issues.Add("RollbackRecoveryMaterialAppendPositionInvalid");
        }

        if (material.ObservedAtUtc == default)
        {
            issues.Add("RollbackRecoveryMaterialObservedAtRequired");
        }

        if (material.RecordedAtUtc == default)
        {
            issues.Add("RollbackRecoveryMaterialRecordedAtRequired");
        }

        if (material.ObservedAtUtc != default &&
            material.RecordedAtUtc != default &&
            material.RecordedAtUtc < material.ObservedAtUtc)
        {
            issues.Add("RollbackRecoveryMaterialRecordedBeforeObserved");
        }
    }

    private static void AddDiagnosticSnapshotIssues(
        RollbackRecoveryReadModelRequest request,
        RollbackRecoveryDiagnosticSnapshot snapshot,
        ICollection<string> issues)
    {
        AddScopeIssues(snapshot.TenantId, "RollbackRecoveryDiagnosticTenantIdRequired", "RollbackRecoveryDiagnosticTenantIdInvalid", issues);
        AddScopeIssues(snapshot.ProjectId, "RollbackRecoveryDiagnosticProjectIdRequired", "RollbackRecoveryDiagnosticProjectIdInvalid", issues);
        AddOperationIdIssues(snapshot.OperationId, issues);
        AddSourceIssues(snapshot.Source, "RollbackRecoveryDiagnosticSourceRequired", "RollbackRecoveryDiagnosticSourceInvalid", issues);

        if (!Same(request.TenantId, snapshot.TenantId))
        {
            issues.Add("RollbackRecoveryDiagnosticTenantMismatch");
        }

        if (!Same(request.ProjectId, snapshot.ProjectId))
        {
            issues.Add("RollbackRecoveryDiagnosticProjectMismatch");
        }

        if (!Same(request.OperationId, snapshot.OperationId))
        {
            issues.Add("RollbackRecoveryDiagnosticOperationMismatch");
        }

        if (snapshot.RecordedAtUtc == default)
        {
            issues.Add("RollbackRecoveryDiagnosticRecordedAtRequired");
        }

        if (!Enum.IsDefined(snapshot.InterruptedRunStatus))
        {
            issues.Add("RollbackRecoveryDiagnosticInterruptedRunStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.InterruptedRunState))
        {
            issues.Add("RollbackRecoveryDiagnosticInterruptedRunStateInvalid");
        }

        if (!Enum.IsDefined(snapshot.InterruptedRunGap))
        {
            issues.Add("RollbackRecoveryDiagnosticInterruptedRunGapInvalid");
        }

        if (!Enum.IsDefined(snapshot.ProjectedStatusKind))
        {
            issues.Add("RollbackRecoveryDiagnosticProjectedStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.MissingEvidenceStatus))
        {
            issues.Add("RollbackRecoveryDiagnosticMissingEvidenceStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.ForbiddenActionStatus))
        {
            issues.Add("RollbackRecoveryDiagnosticForbiddenActionStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.ReceiptResolutionStatus))
        {
            issues.Add("RollbackRecoveryDiagnosticReceiptResolutionStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.EvidenceResolutionStatus))
        {
            issues.Add("RollbackRecoveryDiagnosticEvidenceResolutionStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.ValidationStalenessStatus))
        {
            issues.Add("RollbackRecoveryDiagnosticValidationStalenessStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.PatchBaseFreshnessStatus))
        {
            issues.Add("RollbackRecoveryDiagnosticPatchBaseFreshnessStatusInvalid");
        }

        if (!Enum.IsDefined(snapshot.WorktreeBaseHeadFreshnessStatus))
        {
            issues.Add("RollbackRecoveryDiagnosticWorktreeBaseHeadFreshnessStatusInvalid");
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
            issues.Add("RollbackRecoveryMaterialCorrelationIdRequired");
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
            issues.Add("RollbackRecoveryMaterialCorrelationIdInvalid");
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

    private static void AddMaterialKindIssues(
        RollbackRecoveryMaterialKind materialKind,
        ICollection<string> issues)
    {
        if (materialKind == RollbackRecoveryMaterialKind.Unknown ||
            !Enum.IsDefined(materialKind))
        {
            issues.Add("RollbackRecoveryMaterialKindRequired");
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
            issues.Add("RollbackRecoveryMaterialSurfaceKindRequired");
        }

        AddIdIssues(surfaceId, "RollbackRecoveryMaterialSurfaceIdRequired", "RollbackRecoveryMaterialSurfaceIdInvalid", issues);
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
            issues.Add("RollbackRecoveryMaterialReferenceKindInvalid");
        }

        if (hasKind && !hasId)
        {
            issues.Add("RollbackRecoveryMaterialReferenceIdRequired");
        }

        if (!hasKind && hasId)
        {
            issues.Add("RollbackRecoveryMaterialReferenceKindRequired");
        }

        if (hasId)
        {
            AddIdIssues(referenceId, "RollbackRecoveryMaterialReferenceIdRequired", "RollbackRecoveryMaterialReferenceIdInvalid", issues);
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
            issues.Add("RollbackRecoveryMaterialRedactionReasonRequired");
        }

        if (!string.IsNullOrWhiteSpace(redactionReason) &&
            ContainsUnsafeText(redactionReason))
        {
            issues.Add("RollbackRecoveryMaterialRedactionReasonInvalid");
        }
    }

    private static RollbackRecoveryReadModel Invalid(IReadOnlyList<string> issues) =>
        Result(issues, string.Empty, string.Empty, string.Empty, default, RollbackRecoveryReadModelStatus.InvalidRequest);

    private static RollbackRecoveryReadModel Result(
        IReadOnlyList<string> issues,
        string tenantId,
        string projectId,
        string operationId,
        DateTimeOffset asOfUtc,
        RollbackRecoveryReadModelStatus status) =>
        new()
        {
            IsValid = issues.Count == 0,
            ResolutionStatus = issues.Count == 0 ? RollbackRecoveryReadModelStatus.NoMaterial : status,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            AsOfUtc = asOfUtc,
            Assessment = null,
            MaterialIds = [],
            AmbiguousMaterial = [],
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = issues.Count == 0 ? Warnings() : [],
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    internal static IReadOnlyList<string> Warnings() =>
    [
        "rollback/recovery state is metadata-only",
        "rollback/recovery state uses supplied material metadata only",
        "rollback/recovery state uses supplied diagnostic snapshot metadata only",
        "rollback/recovery state uses supplied AsOfUtc only",
        "rollback material is not rollback authority",
        "recovery material is not recovery authority",
        "rollback plan observed is not rollback authority",
        "rollback evidence observed is not rollback execution proof",
        "rollback receipt observed is not rollback permission",
        "recovery plan observed is not recovery authority",
        "recovery evidence observed is not recovery execution proof",
        "recovery receipt observed is not recovery permission",
        "rollback failed is not retry authority",
        "recovery failed is not resume authority",
        "no missing material is not action allowed"
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
            "rollback allowed",
            "recovery allowed",
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
