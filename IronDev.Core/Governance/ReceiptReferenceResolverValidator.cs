using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class ReceiptReferenceResolverValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "receipt reference resolver is read-only",
        "receipt reference resolver is reference-only",
        "receipt reference resolver is metadata-only",
        "receipt reference resolver is not operation identity",
        "receipt reference resolver is not operation lookup",
        "receipt reference resolver is not correlation authority",
        "receipt reference resolver is not timeline assembly",
        "receipt reference resolver is not status projection",
        "receipt reference resolver is not missing evidence calculation",
        "receipt reference resolver is not forbidden-action resolution",
        "receipt reference resolver is not raw evidence resolution",
        "receipt reference resolver is not raw receipt resolution",
        "receipt reference resolver is not receipt verification",
        "receipt reference resolver is not executor proof",
        "receipt reference resolver is not validation freshness",
        "receipt reference resolver is not policy satisfaction",
        "receipt reference resolver is not approval",
        "receipt reference resolver is not next-safe-action formatting",
        "receipt reference resolver is not authority-warning formatting",
        "receipt reference resolver is not source apply",
        "receipt reference resolver is not rollback",
        "receipt reference resolver is not retry permission",
        "receipt reference resolver is not commit",
        "receipt reference resolver is not push",
        "receipt reference resolver is not PR creation",
        "receipt reference resolver is not merge readiness",
        "receipt reference resolver is not release readiness",
        "receipt reference resolver is not deployment readiness",
        "receipt reference resolver is not memory promotion",
        "receipt reference resolver is not workflow continuation",
        "receipt found is not authority",
        "receipt missing is not denial",
        "receipt ambiguity is not denial or approval",
        "redacted receipt metadata is not raw payload",
        "receipt kind is not authority kind",
        "complete receipt resolution is not action allowed"
    ];

    public static ReceiptReferenceResolverResult ValidateRequest(ReceiptReferenceResolverRequest? request)
    {
        if (request is null)
        {
            return Invalid(["ReceiptReferenceResolverRequestRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(request.TenantId, "ReceiptReferenceTenantIdRequired", "ReceiptReferenceTenantIdInvalid", issues);
        AddScopeIssues(request.ProjectId, "ReceiptReferenceProjectIdRequired", "ReceiptReferenceProjectIdInvalid", issues);
        AddOperationIdIssues(request.OperationId, issues);

        if (request.RequestedReferences is null)
        {
            issues.Add("ReceiptReferenceRequestedReferencesRequired");
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
                    issues.Add("ReceiptReferenceTenantMismatch");
                }

                if (!Same(request.ProjectId, requested.ProjectId))
                {
                    issues.Add("ReceiptReferenceProjectMismatch");
                }

                if (!Same(request.OperationId, requested.OperationId))
                {
                    issues.Add("ReceiptReferenceOperationMismatch");
                }
            }
        }

        if (request.AvailableReceipts is null)
        {
            issues.Add("ReceiptReferenceAvailableReceiptsRequired");
        }
        else
        {
            foreach (var available in request.AvailableReceipts)
            {
                AddAvailableReceiptIssues(available, issues);
                if (available is null)
                {
                    continue;
                }

                if (!Same(request.TenantId, available.TenantId))
                {
                    issues.Add("AvailableReceiptTenantMismatch");
                }

                if (!Same(request.ProjectId, available.ProjectId))
                {
                    issues.Add("AvailableReceiptProjectMismatch");
                }

                if (!Same(request.OperationId, available.OperationId))
                {
                    issues.Add("AvailableReceiptOperationMismatch");
                }
            }
        }

        return Result(
            issues,
            request.TenantId,
            request.ProjectId,
            request.OperationId,
            ReceiptReferenceResolutionStatus.InvalidRequest);
    }

    private static void AddRequestedReferenceIssues(
        ReceiptReferenceRequestItem? requested,
        ICollection<string> issues)
    {
        if (requested is null)
        {
            issues.Add("ReceiptReferenceRequestItemRequired");
            return;
        }

        AddScopeIssues(requested.TenantId, "ReceiptReferenceItemTenantIdRequired", "ReceiptReferenceItemTenantIdInvalid", issues);
        AddScopeIssues(requested.ProjectId, "ReceiptReferenceItemProjectIdRequired", "ReceiptReferenceItemProjectIdInvalid", issues);
        AddOperationIdIssues(requested.OperationId, issues);
        AddCorrelationIdIssues(requested.CorrelationId, requested.OperationId, "ReceiptReferenceCorrelationId", issues);
        AddIdIssues(requested.ReceiptReferenceId, "ReceiptReferenceIdRequired", "ReceiptReferenceIdInvalid", issues);
        AddReceiptKindIssues(requested.ReceiptKind, "ReceiptReferenceKindRequired", issues);
        AddSourceIssues(requested.Source, "ReceiptReferenceSourceRequired", "ReceiptReferenceSourceInvalid", issues);

        if (requested.RequestedAtUtc == default)
        {
            issues.Add("ReceiptReferenceRequestedAtRequired");
        }

        AddReferencePairIssues(
            requested.ReferenceKind,
            requested.ReferenceId,
            "ReceiptReferenceReferenceKindRequired",
            "ReceiptReferenceReferenceIdRequired",
            "ReceiptReferenceReferenceIdInvalid",
            issues);
    }

    private static void AddAvailableReceiptIssues(
        AvailableReceiptMetadata? available,
        ICollection<string> issues)
    {
        if (available is null)
        {
            issues.Add("AvailableReceiptMetadataRequired");
            return;
        }

        AddScopeIssues(available.TenantId, "AvailableReceiptTenantIdRequired", "AvailableReceiptTenantIdInvalid", issues);
        AddScopeIssues(available.ProjectId, "AvailableReceiptProjectIdRequired", "AvailableReceiptProjectIdInvalid", issues);
        AddOperationIdIssues(available.OperationId, issues);
        AddCorrelationIdIssues(available.CorrelationId, available.OperationId, "AvailableReceiptCorrelationId", issues);
        AddIdIssues(available.ReceiptId, "AvailableReceiptIdRequired", "AvailableReceiptIdInvalid", issues);
        AddReceiptKindIssues(available.ReceiptKind, "AvailableReceiptKindRequired", issues);
        AddSurfaceIssues(available.SurfaceKind, available.SurfaceId, issues);
        AddSourceIssues(available.Source, "AvailableReceiptSourceRequired", "AvailableReceiptSourceInvalid", issues);

        if (available.CreatedAtUtc == default)
        {
            issues.Add("AvailableReceiptCreatedAtRequired");
        }

        AddReferencePairIssues(
            available.ReferenceKind,
            available.ReferenceId,
            "AvailableReceiptReferenceKindRequired",
            "AvailableReceiptReferenceIdRequired",
            "AvailableReceiptReferenceIdInvalid",
            issues);

        if (available.IsRedacted && string.IsNullOrWhiteSpace(available.RedactionReason))
        {
            issues.Add("AvailableReceiptRedactionReasonRequired");
        }

        if (!string.IsNullOrWhiteSpace(available.RedactionReason) &&
            ContainsUnsafeText(available.RedactionReason))
        {
            issues.Add("AvailableReceiptRedactionReasonInvalid");
        }
    }

    private static void AddReceiptKindIssues(
        ReceiptReferenceKind kind,
        string issue,
        ICollection<string> issues)
    {
        if (kind == ReceiptReferenceKind.Unknown || !Enum.IsDefined(kind))
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
            issues.Add("AvailableReceiptSurfaceKindRequired");
        }

        if (string.IsNullOrWhiteSpace(surfaceId))
        {
            issues.Add("AvailableReceiptSurfaceIdRequired");
            return;
        }

        if (ContainsUnsafeText(surfaceId) ||
            surfaceId.Any(char.IsWhiteSpace) ||
            IsUrl(surfaceId))
        {
            issues.Add("AvailableReceiptSurfaceIdInvalid");
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

    private static ReceiptReferenceResolverResult Invalid(IReadOnlyList<string> issues) =>
        Result(
            issues,
            string.Empty,
            string.Empty,
            string.Empty,
            ReceiptReferenceResolutionStatus.InvalidRequest);

    private static ReceiptReferenceResolverResult Result(
        IReadOnlyList<string> issues,
        string tenantId,
        string projectId,
        string operationId,
        ReceiptReferenceResolutionStatus status) =>
        new()
        {
            IsValid = issues.Count == 0,
            ResolutionStatus = issues.Count == 0 ? ReceiptReferenceResolutionStatus.NoReferences : status,
            TenantId = tenantId,
            ProjectId = projectId,
            OperationId = operationId,
            ResolvedReceipts = [],
            UnresolvedReceipts = [],
            AmbiguousReceipts = [],
            Issues = issues.Distinct(StringComparer.Ordinal).OrderBy(static issue => issue, StringComparer.Ordinal).ToArray(),
            Warnings = issues.Count == 0 ? Warnings() : [],
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    private static IReadOnlyList<string> Warnings() =>
    [
        "receipt reference resolution is metadata-only",
        "receipt found is not authority",
        "complete receipt resolution is not action allowed",
        "redacted receipt metadata is not raw payload"
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
