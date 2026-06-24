using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class OperationCorrelationValidator
{
    public static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "correlation id is not operation id",
        "correlation id is not tenant scope",
        "correlation id is not project scope",
        "correlation id is not run id",
        "correlation id is not patch artifact id",
        "correlation id is not source apply id",
        "correlation id is not commit package id",
        "correlation id is not commit sha",
        "correlation id is not push id",
        "correlation id is not pull request id",
        "correlation id is not receipt id",
        "correlation id is not evidence id",
        "correlation id is not operation lookup",
        "correlation group is not timeline projection",
        "correlation group is not status projection",
        "correlation group is not evidence resolution",
        "correlation group is not receipt resolution",
        "correlation group is not blocked-state explanation",
        "correlation group is not next-safe-action formatting",
        "correlation group is not authority",
        "correlation group is not approval",
        "correlation group is not policy satisfaction",
        "correlation group is not validation freshness",
        "correlation group is not source apply",
        "correlation group is not rollback",
        "correlation group is not retry permission",
        "correlation group is not commit",
        "correlation group is not push",
        "correlation group is not pull request creation",
        "correlation group is not merge readiness",
        "correlation group is not release readiness",
        "correlation group is not deployment readiness",
        "correlation group is not memory promotion",
        "correlation group is not workflow continuation"
    ];

    public static OperationCorrelationValidationResult ValidateLink(OperationCorrelationLink? link)
    {
        if (link is null)
        {
            return Invalid(["OperationCorrelationLinkRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(link.TenantId, "OperationCorrelationTenantIdRequired", "OperationCorrelationTenantIdInvalid", issues);
        AddScopeIssues(link.ProjectId, "OperationCorrelationProjectIdRequired", "OperationCorrelationProjectIdInvalid", issues);
        AddOperationIdIssues(link.OperationId, issues);
        AddCorrelationIdIssues(link.CorrelationId, link.OperationId, issues);
        AddSurfaceIssues(link.SurfaceKind, link.SurfaceId, issues);

        if (link.ObservedAtUtc == default)
        {
            issues.Add("OperationCorrelationObservedAtRequired");
        }

        AddSourceIssues(link.Source, issues);

        return Result(issues);
    }

    public static OperationCorrelationValidationResult ValidateGroup(OperationCorrelationGroup? group)
    {
        if (group is null)
        {
            return Invalid(["OperationCorrelationGroupRequired"]);
        }

        var issues = new List<string>();
        AddScopeIssues(group.TenantId, "OperationCorrelationTenantIdRequired", "OperationCorrelationTenantIdInvalid", issues);
        AddScopeIssues(group.ProjectId, "OperationCorrelationProjectIdRequired", "OperationCorrelationProjectIdInvalid", issues);
        AddOperationIdIssues(group.OperationId, issues);
        AddCorrelationIdIssues(group.CorrelationId, group.OperationId, issues);

        var links = group.Links ?? [];
        if (links.Count == 0)
        {
            issues.Add("OperationCorrelationGroupLinksRequired");
        }

        var surfaceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            var linkValidation = ValidateLink(link);
            if (!linkValidation.IsValid)
            {
                issues.Add("OperationCorrelationGroupLinkInvalid");
                issues.AddRange(linkValidation.Issues.Select(static issue => $"OperationCorrelationGroupLink:{issue}"));
            }

            if (!Same(group.TenantId, link.TenantId))
            {
                issues.Add("OperationCorrelationGroupTenantMismatch");
            }

            if (!Same(group.ProjectId, link.ProjectId))
            {
                issues.Add("OperationCorrelationGroupProjectMismatch");
            }

            if (!Same(group.OperationId, link.OperationId))
            {
                issues.Add("OperationCorrelationGroupOperationMismatch");
            }

            if (!Same(group.CorrelationId, link.CorrelationId))
            {
                issues.Add("OperationCorrelationGroupCorrelationMismatch");
            }

            if (link.SurfaceKind != OperationCorrelationSurfaceKind.Unknown &&
                Enum.IsDefined(link.SurfaceKind) &&
                !string.IsNullOrWhiteSpace(link.SurfaceId) &&
                !surfaceIds.Add($"{link.SurfaceKind}:{link.SurfaceId}"))
            {
                issues.Add("OperationCorrelationDuplicateSurfaceId");
            }
        }

        return Result(issues);
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
            issues.Add("OperationCorrelationIdRequired");
            return;
        }

        if (ContainsUnsafeText(correlationId) ||
            correlationId.Any(char.IsWhiteSpace) ||
            correlationId.Contains('/') ||
            correlationId.Contains('\\'))
        {
            issues.Add("OperationCorrelationIdInvalid");
        }

        if (IsUrl(correlationId))
        {
            issues.Add("OperationCorrelationIdUrlNotAllowed");
        }

        if (ContainsAuthorityText(correlationId))
        {
            issues.Add("OperationCorrelationIdAuthorityTextBlocked");
        }

        if (!string.IsNullOrWhiteSpace(operationId) &&
            Same(correlationId, operationId))
        {
            issues.Add("OperationCorrelationIdCannotReplaceOperationId");
        }

        AddReferenceSubstitutionIssues(correlationId, issues);

        if (!CanonicalCorrelationIdPattern().IsMatch(correlationId))
        {
            issues.Add("OperationCorrelationIdMustBeCanonical");
        }
    }

    private static void AddReferenceSubstitutionIssues(string correlationId, ICollection<string> issues)
    {
        if (HasAnyPrefix(correlationId, "op_"))
        {
            issues.Add("OperationCorrelationIdCannotLookLikeOperationId");
        }

        if (HasAnyPrefix(correlationId, "run_", "run:", "run-", "run/"))
        {
            issues.Add("OperationCorrelationIdCannotLookLikeRunId");
        }

        if (HasAnyPrefix(correlationId, "patch-artifact:", "patch-artifact-", "patch_", "patch-", "artifact:"))
        {
            issues.Add("OperationCorrelationIdCannotLookLikePatchArtifactId");
        }

        if (HasAnyPrefix(correlationId, "source-apply:", "source-apply-", "source_apply_", "apply:", "apply_"))
        {
            issues.Add("OperationCorrelationIdCannotLookLikeSourceApplyId");
        }

        if (HasAnyPrefix(correlationId, "commit-package:", "commit-package-", "commit_package_"))
        {
            issues.Add("OperationCorrelationIdCannotLookLikeCommitPackageId");
        }

        if (CommitShaPattern().IsMatch(correlationId))
        {
            issues.Add("OperationCorrelationIdCannotLookLikeCommitSha");
        }

        if (HasAnyPrefix(correlationId, "push:", "push_", "push-"))
        {
            issues.Add("OperationCorrelationIdCannotLookLikePushId");
        }

        if (HasAnyPrefix(correlationId, "pr:", "pr-", "pull-request:", "pull-request-", "pull_request_") ||
            int.TryParse(correlationId, out _))
        {
            issues.Add("OperationCorrelationIdCannotLookLikePullRequestId");
        }

        if (HasAnyPrefix(correlationId, "receipt:", "receipt-", "receipt_", "source-apply-receipt:", "operation-receipt:"))
        {
            issues.Add("OperationCorrelationIdCannotLookLikeReceiptId");
        }

        if (HasAnyPrefix(correlationId, "evidence:", "evidence-", "evidence_"))
        {
            issues.Add("OperationCorrelationIdCannotLookLikeEvidenceId");
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
            issues.Add("OperationCorrelationSurfaceKindRequired");
        }

        if (string.IsNullOrWhiteSpace(surfaceId))
        {
            issues.Add("OperationCorrelationSurfaceIdRequired");
            return;
        }

        if (ContainsUnsafeText(surfaceId) ||
            surfaceId.Any(char.IsWhiteSpace))
        {
            issues.Add("OperationCorrelationSurfaceIdInvalid");
        }
    }

    private static void AddSourceIssues(string? source, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            issues.Add("OperationCorrelationSourceRequired");
            return;
        }

        if (ContainsUnsafeText(source) ||
            source.Any(char.IsWhiteSpace) ||
            ContainsAuthorityText(source))
        {
            issues.Add("OperationCorrelationSourceInvalid");
        }
    }

    private static OperationCorrelationValidationResult Invalid(IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            Issues = issues,
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    private static OperationCorrelationValidationResult Result(IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = issues.Count == 0,
            Issues = issues.Distinct(StringComparer.Ordinal).ToArray(),
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool HasAnyPrefix(string value, params string[] prefixes) =>
        prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

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

    [GeneratedRegex("^corr_([0-9a-f]{16,64}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$")]
    private static partial Regex CanonicalCorrelationIdPattern();

    [GeneratedRegex("^[0-9a-fA-F]{40}$")]
    private static partial Regex CommitShaPattern();
}
