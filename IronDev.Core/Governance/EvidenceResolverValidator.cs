using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class EvidenceResolverValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "evidence resolver is read-only",
        "evidence resolver is metadata-first",
        "evidence resolver may produce redacted previews only from supplied payloads",
        "evidence resolver never fetches raw payloads",
        "evidence resolver never returns raw payloads",
        "evidence resolver is not operation identity",
        "evidence resolver is not operation lookup",
        "evidence resolver is not correlation authority",
        "evidence resolver is not timeline assembly",
        "evidence resolver is not status projection",
        "evidence resolver is not missing evidence calculation",
        "evidence resolver is not forbidden-action resolution",
        "evidence resolver is not receipt resolution",
        "evidence resolver is not evidence authenticity verification",
        "evidence resolver is not executor proof",
        "evidence resolver is not validation freshness",
        "evidence resolver is not policy satisfaction",
        "evidence resolver is not approval",
        "evidence resolver is not next-safe-action formatting",
        "evidence resolver is not authority-warning formatting",
        "evidence resolver is not source apply",
        "evidence resolver is not rollback",
        "evidence resolver is not retry permission",
        "evidence resolver is not commit",
        "evidence resolver is not push",
        "evidence resolver is not PR creation",
        "evidence resolver is not merge readiness",
        "evidence resolver is not release readiness",
        "evidence resolver is not deployment readiness",
        "evidence resolver is not memory promotion",
        "evidence resolver is not workflow continuation",
        "evidence found is not authority",
        "evidence missing is not denial",
        "evidence ambiguity is not denial or approval",
        "redacted evidence preview is not raw payload",
        "evidence kind is not authority kind",
        "complete evidence resolution is not action allowed"
    ];

    public static EvidenceResolverResult ValidateRequest(EvidenceResolverRequest? request)
    {
        if (request is null)
        {
            return Invalid(["EvidenceResolverRequestRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(request.TenantId, "EvidenceResolverTenantIdRequired", "EvidenceResolverTenantIdInvalid", issues);
        AddScopeIssues(request.ProjectId, "EvidenceResolverProjectIdRequired", "EvidenceResolverProjectIdInvalid", issues);
        AddOperationIdIssues(request.OperationId, issues);

        if (request.RequestedReferences is null)
        {
            issues.Add("EvidenceRequestedReferencesRequired");
        }
        else
        {
            foreach (var requested in request.RequestedReferences)
            {
                AddRequestedReferenceIssues(requested, issues);
                if (requested is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, requested.TenantId))
                {
                    issues.Add("EvidenceReferenceTenantMismatch");
                }

                if (!Same(request.ProjectId, requested.ProjectId))
                {
                    issues.Add("EvidenceReferenceProjectMismatch");
                }

                if (!Same(request.OperationId, requested.OperationId))
                {
                    issues.Add("EvidenceReferenceOperationMismatch");
                }
            }
        }

        if (request.AvailableEvidence is null)
        {
            issues.Add("AvailableEvidenceRequired");
        }
        else
        {
            foreach (var available in request.AvailableEvidence)
            {
                AddAvailableEvidenceIssues(available, issues);
                if (available is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, available.TenantId))
                {
                    issues.Add("AvailableEvidenceTenantMismatch");
                }

                if (!Same(request.ProjectId, available.ProjectId))
                {
                    issues.Add("AvailableEvidenceProjectMismatch");
                }

                if (!Same(request.OperationId, available.OperationId))
                {
                    issues.Add("AvailableEvidenceOperationMismatch");
                }
            }
        }

        if (request.SuppliedPayloadsForRedaction is null)
        {
            issues.Add("SuppliedEvidencePayloadsRequired");
        }
        else
        {
            foreach (var payload in request.SuppliedPayloadsForRedaction)
            {
                AddSuppliedPayloadIssues(payload, issues);
                if (payload is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, payload.TenantId))
                {
                    issues.Add("SuppliedEvidencePayloadTenantMismatch");
                }

                if (!Same(request.ProjectId, payload.ProjectId))
                {
                    issues.Add("SuppliedEvidencePayloadProjectMismatch");
                }

                if (!Same(request.OperationId, payload.OperationId))
                {
                    issues.Add("SuppliedEvidencePayloadOperationMismatch");
                }
            }
        }

        return Result(
            issues,
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            EvidenceResolutionStatus.InvalidRequest);
    }

    private static void AddRequestedReferenceIssues(
        EvidenceReferenceRequestItem? requested,
        ICollection<string> issues)
    {
        if (requested is null)
        {
            issues.Add("EvidenceReferenceRequestItemRequired");
            return;
        }

        AddScopeIssues(requested.TenantId, "EvidenceReferenceItemTenantIdRequired", "EvidenceReferenceItemTenantIdInvalid", issues);
        AddScopeIssues(requested.ProjectId, "EvidenceReferenceItemProjectIdRequired", "EvidenceReferenceItemProjectIdInvalid", issues);
        AddOperationIdIssues(requested.OperationId, issues);
        AddCorrelationIdIssues(requested.CorrelationId, requested.OperationId, "EvidenceReferenceCorrelationId", issues);
        AddIdIssues(requested.EvidenceReferenceId, "EvidenceReferenceIdRequired", "EvidenceReferenceIdInvalid", issues);
        AddEvidenceKindIssues(requested.EvidenceKind, "EvidenceReferenceKindRequired", issues);
        AddSourceIssues(requested.Source, "EvidenceReferenceSourceRequired", "EvidenceReferenceSourceInvalid", issues);

        if (requested.RequestedAtUtc == default)
        {
            issues.Add("EvidenceReferenceRequestedAtRequired");
        }

        AddReferencePairIssues(
            requested.ReferenceKind,
            requested.ReferenceId,
            "EvidenceReferenceReferenceKindRequired",
            "EvidenceReferenceReferenceIdRequired",
            "EvidenceReferenceReferenceIdInvalid",
            issues);
    }

    private static void AddAvailableEvidenceIssues(
        AvailableEvidenceMetadata? available,
        ICollection<string> issues)
    {
        if (available is null)
        {
            issues.Add("AvailableEvidenceMetadataRequired");
            return;
        }

        AddScopeIssues(available.TenantId, "AvailableEvidenceTenantIdRequired", "AvailableEvidenceTenantIdInvalid", issues);
        AddScopeIssues(available.ProjectId, "AvailableEvidenceProjectIdRequired", "AvailableEvidenceProjectIdInvalid", issues);
        AddOperationIdIssues(available.OperationId, issues);
        AddCorrelationIdIssues(available.CorrelationId, available.OperationId, "AvailableEvidenceCorrelationId", issues);
        AddIdIssues(available.EvidenceId, "AvailableEvidenceIdRequired", "AvailableEvidenceIdInvalid", issues);
        AddEvidenceKindIssues(available.EvidenceKind, "AvailableEvidenceKindRequired", issues);
        AddSurfaceIssues(available.SurfaceKind, available.SurfaceId, issues);
        AddSourceIssues(available.Source, "AvailableEvidenceSourceRequired", "AvailableEvidenceSourceInvalid", issues);

        if (available.CreatedAtUtc == default)
        {
            issues.Add("AvailableEvidenceCreatedAtRequired");
        }

        if (available.PayloadState == EvidencePayloadState.Unknown ||
            !Enum.IsDefined(available.PayloadState))
        {
            issues.Add("AvailableEvidencePayloadStateRequired");
        }

        AddReferencePairIssues(
            available.ReferenceKind,
            available.ReferenceId,
            "AvailableEvidenceReferenceKindRequired",
            "AvailableEvidenceReferenceIdRequired",
            "AvailableEvidenceReferenceIdInvalid",
            issues);

        if (available.IsRedacted && string.IsNullOrWhiteSpace(available.RedactionReason))
        {
            issues.Add("AvailableEvidenceRedactionReasonRequired");
        }

        if (!string.IsNullOrWhiteSpace(available.RedactionReason) &&
            ContainsUnsafeText(available.RedactionReason))
        {
            issues.Add("AvailableEvidenceRedactionReasonInvalid");
        }
    }

    private static void AddSuppliedPayloadIssues(
        SuppliedEvidencePayloadForRedaction? payload,
        ICollection<string> issues)
    {
        if (payload is null)
        {
            issues.Add("SuppliedEvidencePayloadRequired");
            return;
        }

        AddScopeIssues(payload.TenantId, "SuppliedEvidencePayloadTenantIdRequired", "SuppliedEvidencePayloadTenantIdInvalid", issues);
        AddScopeIssues(payload.ProjectId, "SuppliedEvidencePayloadProjectIdRequired", "SuppliedEvidencePayloadProjectIdInvalid", issues);
        AddOperationIdIssues(payload.OperationId, issues);
        AddIdIssues(payload.EvidenceId, "SuppliedEvidencePayloadEvidenceIdRequired", "SuppliedEvidencePayloadEvidenceIdInvalid", issues);
        AddPayloadTextIssues(payload.PayloadText, issues);
        AddContentTypeIssues(payload.PayloadContentType, issues);
        AddSourceIssues(payload.Source, "SuppliedEvidencePayloadSourceRequired", "SuppliedEvidencePayloadSourceInvalid", issues);

        if (payload.SuppliedAtUtc == default)
        {
            issues.Add("SuppliedEvidencePayloadSuppliedAtRequired");
        }
    }

    private static void AddEvidenceKindIssues(
        EvidenceReferenceKind kind,
        string issue,
        ICollection<string> issues)
    {
        if (kind == EvidenceReferenceKind.Unknown || !Enum.IsDefined(kind))
        {
            issues.Add(issue);
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
            issues.Add("AvailableEvidenceSurfaceKindRequired");
        }

        if (string.IsNullOrWhiteSpace(surfaceId))
        {
            issues.Add("AvailableEvidenceSurfaceIdRequired");
            return;
        }

        if (ContainsUnsafeText(surfaceId) ||
            surfaceId.Any(char.IsWhiteSpace) ||
            IsUrl(surfaceId))
        {
            issues.Add("AvailableEvidenceSurfaceIdInvalid");
        }
    }

    private static void AddReferencePairIssues(
        OperationReferenceKind referenceKind,
        string? referenceId,
        string missingKindIssue,
        string missingIdIssue,
        string invalidIdIssue,
        ICollection<string> issues)
    {
        var hasKind = referenceKind != OperationReferenceKind.Unknown &&
            Enum.IsDefined(referenceKind);
        var hasId = !string.IsNullOrWhiteSpace(referenceId);

        if (!hasKind && !hasId)
        {
            return;
        }

        if (!hasKind)
        {
            issues.Add(missingKindIssue);
        }

        if (!hasId)
        {
            issues.Add(missingIdIssue);
            return;
        }

        var safeReferenceId = referenceId!;
        if (ContainsUnsafeText(safeReferenceId) ||
            safeReferenceId.Any(char.IsWhiteSpace) ||
            IsUrl(safeReferenceId))
        {
            issues.Add(invalidIdIssue);
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
            value.Any(char.IsWhiteSpace))
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
        string issuePrefix,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            issues.Add($"{issuePrefix}Required");
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
            issues.Add($"{issuePrefix}Invalid");
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

    private static void AddPayloadTextIssues(
        string? value,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add("SuppliedEvidencePayloadTextRequired");
        }
    }

    private static void AddContentTypeIssues(
        string? value,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add("SuppliedEvidencePayloadContentTypeRequired");
            return;
        }

        if (value.Any(char.IsControl) ||
            value.Length > 128 ||
            value.Contains('\\'))
        {
            issues.Add("SuppliedEvidencePayloadContentTypeInvalid");
        }
    }

    private static EvidenceResolverResult Invalid(IReadOnlyList<string> issues) =>
        Result(
            issues,
            string.Empty,
            string.Empty,
            string.Empty,
            EvidenceResolutionStatus.InvalidRequest);

    private static EvidenceResolverResult Result(
        IReadOnlyList<string> issues,
        string tenantId,
        string projectId,
        string operationId,
        EvidenceResolutionStatus status) =>
        new()
        {
            IsValid = issues.Count == 0,
            ResolutionStatus = issues.Count == 0 ? EvidenceResolutionStatus.NoReferences : status,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            ResolvedEvidence = [],
            UnresolvedEvidence = [],
            AmbiguousEvidence = [],
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = issues.Count == 0 ? Warnings() : [],
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    private static IReadOnlyList<string> Warnings() =>
    [
        "evidence resolution is metadata-first",
        "evidence found is not authority",
        "complete evidence resolution is not action allowed",
        "redacted evidence preview is not raw payload"
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
            "ready for review",
            "merge ready",
            "release ready",
            "deploy now",
            "continue workflow",
            "retry authorized",
            "rollback authorized",
            "ship it"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsRawPayloadMarker(string value)
    {
        var markers = new[]
        {
            "raw evidence payload",
            "raw receipt payload",
            "raw validation log",
            "raw request body",
            "raw response body",
            "full patch",
            "full diff",
            "hidden chain-of-thought",
            "private reasoning"
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
