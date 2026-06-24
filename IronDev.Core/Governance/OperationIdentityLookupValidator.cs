namespace IronDev.Core.Governance;

public static class OperationIdentityLookupValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "lookup is read-only",
        "lookup is not operation creation",
        "lookup is not operation id minting",
        "lookup is not status projection",
        "lookup is not timeline projection",
        "lookup is not evidence resolution",
        "lookup is not receipt resolution",
        "lookup is not approval",
        "lookup is not policy satisfaction",
        "lookup is not validation freshness",
        "lookup is not source apply",
        "lookup is not rollback",
        "lookup is not retry permission",
        "lookup is not commit",
        "lookup is not push",
        "lookup is not pull request creation",
        "lookup is not merge readiness",
        "lookup is not release readiness",
        "lookup is not deployment readiness",
        "lookup is not memory promotion",
        "lookup is not workflow continuation",
        "external reference id is not operation id"
    ];

    public static OperationIdentityLookupValidationResult ValidateRequest(OperationIdentityLookupRequest? request)
    {
        if (request is null)
        {
            return Invalid(["OperationIdentityLookupRequestRequired"]);
        }

        var issues = new List<string>();

        AddScopeIssues(request.TenantId, "OperationIdentityLookupTenantIdRequired", "OperationIdentityLookupTenantIdInvalid", issues);
        AddScopeIssues(request.ProjectId, "OperationIdentityLookupProjectIdRequired", "OperationIdentityLookupProjectIdInvalid", issues);

        if (request.ReferenceKind == OperationReferenceKind.Unknown ||
            !Enum.IsDefined(request.ReferenceKind))
        {
            issues.Add("OperationIdentityLookupReferenceKindRequired");
        }

        AddReferenceIdIssues(request.ReferenceKind, request.ReferenceId, issues);

        return new OperationIdentityLookupValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues,
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };
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
            ContainsAuthorityText(value))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddReferenceIdIssues(
        OperationReferenceKind referenceKind,
        string? referenceId,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(referenceId))
        {
            issues.Add("OperationIdentityLookupReferenceIdRequired");
            return;
        }

        if (ContainsUnsafeText(referenceId) ||
            referenceId.Any(char.IsWhiteSpace))
        {
            issues.Add("OperationIdentityLookupReferenceIdInvalid");
        }

        if (ContainsAuthorityText(referenceId))
        {
            issues.Add("OperationIdentityLookupReferenceIdAuthorityTextBlocked");
        }

        if (IsUrl(referenceId) &&
            referenceKind != OperationReferenceKind.PullRequestId)
        {
            issues.Add("OperationIdentityLookupReferenceUrlNotAllowedForReferenceKind");
        }
    }

    private static OperationIdentityLookupValidationResult Invalid(IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            Issues = issues,
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    private static bool ContainsUnsafeText(string value) =>
        value.Any(char.IsControl) || value.Length > 256;

    private static bool ContainsAuthorityText(string value)
    {
        var markers = new[]
        {
            "approve",
            "approved",
            "policy satisfied",
            "policy",
            "authority",
            "ready for review",
            "merge",
            "release",
            "deploy",
            "continue",
            "retry",
            "rollback",
            "ship it"
        };

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUrl(string value) =>
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
}
