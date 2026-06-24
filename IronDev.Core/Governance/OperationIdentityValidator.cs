using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class OperationIdentityValidator
{
    private static readonly IReadOnlyList<string> ForbiddenAuthorityImplications =
    [
        "operation id is not authority",
        "operation id is not approval",
        "operation id is not policy satisfaction",
        "operation id is not validation success",
        "operation id is not source apply",
        "operation id is not rollback",
        "operation id is not rollback execution",
        "operation id is not retry permission",
        "operation id is not commit",
        "operation id is not push",
        "operation id is not pull request creation",
        "operation id is not merge readiness",
        "operation id is not release readiness",
        "operation id is not deployment readiness",
        "operation id is not memory promotion",
        "operation id is not workflow continuation",
        "linked operation reference is not authority"
    ];

    public static OperationIdentityValidationResult ValidateRecord(OperationIdentityRecord? record)
    {
        if (record is null)
        {
            return Invalid(["OperationIdentityRecordRequired"]);
        }

        var issues = new List<string>();
        AddOperationIdIssues(record.OperationId, issues);

        AddScopeIdIssues(record.TenantId, "OperationIdentityTenantIdRequired", "OperationIdentityTenantIdInvalid", issues);
        AddScopeIdIssues(record.ProjectId, "OperationIdentityProjectIdRequired", "OperationIdentityProjectIdInvalid", issues);

        if (record.CreatedAtUtc == default)
        {
            issues.Add("OperationIdentityCreatedAtRequired");
        }

        if (string.IsNullOrWhiteSpace(record.CreatedBy))
        {
            issues.Add("OperationIdentityCreatedByRequired");
        }
        else if (ContainsUnsafeText(record.CreatedBy))
        {
            issues.Add("OperationIdentityCreatedByInvalid");
        }

        if (record.LifecycleState == OperationIdentityLifecycleState.Unknown ||
            !Enum.IsDefined(record.LifecycleState))
        {
            issues.Add("OperationIdentityLifecycleStateRequired");
        }

        var references = record.References ?? [];
        AddReferenceIssues(record.OperationId, references, issues);

        if (!string.IsNullOrWhiteSpace(record.CorrelationId) &&
            Same(record.CorrelationId, record.OperationId))
        {
            issues.Add("CorrelationIdCannotReplaceOperationId");
        }

        return new OperationIdentityValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues,
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications,
            References = NormalizeReferences(references)
        };
    }

    public static OperationIdentityValidationResult ValidateOperationId(string? operationId)
    {
        var issues = new List<string>();
        AddOperationIdIssues(operationId, issues);

        return new OperationIdentityValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues,
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications,
            References = []
        };
    }

    public static OperationIdentityValidationResult ValidateOperationIdPreserved(
        string? assignedOperationId,
        string? proposedOperationId)
    {
        var issues = new List<string>();
        AddOperationIdIssues(assignedOperationId, issues);
        AddOperationIdIssues(proposedOperationId, issues);

        if (!string.IsNullOrWhiteSpace(assignedOperationId) &&
            !string.IsNullOrWhiteSpace(proposedOperationId) &&
            !Same(assignedOperationId, proposedOperationId))
        {
            issues.Add("OperationIdImmutableOnceAssigned");
        }

        return new OperationIdentityValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues.Distinct(StringComparer.Ordinal).ToArray(),
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications,
            References = []
        };
    }

    public static OperationIdentityLifecycleValidationResult ValidateTransition(
        OperationIdentityLifecycleState from,
        OperationIdentityLifecycleState to)
    {
        var issues = new List<string>();

        if (!Enum.IsDefined(from))
        {
            issues.Add("OperationIdentityTransitionFromStateRequired");
        }

        if (to == OperationIdentityLifecycleState.Unknown ||
            !Enum.IsDefined(to))
        {
            issues.Add("OperationIdentityTransitionToStateRequired");
        }

        if (issues.Count == 0 && !IsAllowedTransition(from, to))
        {
            issues.Add("OperationIdentityTransitionNotAllowed");
        }

        return new OperationIdentityLifecycleValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues,
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications
        };
    }

    private static void AddOperationIdIssues(string? operationId, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(operationId))
        {
            issues.Add("OperationIdRequired");
            return;
        }

        if (ContainsUnsafeText(operationId))
        {
            issues.Add("OperationIdInvalidCharacters");
        }

        if (operationId.Any(char.IsWhiteSpace))
        {
            issues.Add("OperationIdCannotContainWhitespace");
        }

        if (!CanonicalOperationIdPattern().IsMatch(operationId))
        {
            issues.Add("OperationIdMustBeBackendMintedCanonicalId");
        }

        AddReferenceSubstitutionIssues(operationId, issues);
    }

    private static void AddScopeIdIssues(
        string? scopeId,
        string requiredIssue,
        string invalidIssue,
        ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(scopeId))
        {
            issues.Add(requiredIssue);
            return;
        }

        if (ContainsUnsafeText(scopeId) ||
            scopeId.Any(char.IsWhiteSpace) ||
            scopeId.Contains("approve", StringComparison.OrdinalIgnoreCase) ||
            scopeId.Contains("policy", StringComparison.OrdinalIgnoreCase) ||
            scopeId.Contains("authority", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(invalidIssue);
        }
    }

    private static void AddReferenceSubstitutionIssues(string operationId, ICollection<string> issues)
    {
        if (HasAnyPrefix(operationId, "run_", "run:", "run-", "run/"))
        {
            issues.Add("RunIdCannotReplaceOperationId");
        }

        if (HasAnyPrefix(operationId, "patch-artifact:", "patch_", "patch-", "artifact:"))
        {
            issues.Add("PatchArtifactIdCannotReplaceOperationId");
        }

        if (HasAnyPrefix(operationId, "source-apply:", "source_apply_", "apply:", "apply_"))
        {
            issues.Add("SourceApplyIdCannotReplaceOperationId");
        }

        if (HasAnyPrefix(operationId, "commit-package:", "commit_package_", "commit-package-"))
        {
            issues.Add("CommitPackageIdCannotReplaceOperationId");
        }

        if (CommitShaPattern().IsMatch(operationId))
        {
            issues.Add("CommitShaCannotReplaceOperationId");
        }

        if (HasAnyPrefix(operationId, "push:", "push_", "push-"))
        {
            issues.Add("PushIdCannotReplaceOperationId");
        }

        if (HasAnyPrefix(operationId, "pr:", "pull-request:", "pull_request_", "https://github.com/", "http://github.com/") ||
            int.TryParse(operationId, out _))
        {
            issues.Add("PullRequestIdCannotReplaceOperationId");
        }

        if (HasAnyPrefix(operationId, "receipt:", "receipt_", "source-apply-receipt:", "operation-receipt:"))
        {
            issues.Add("ReceiptIdCannotReplaceOperationId");
        }

        if (HasAnyPrefix(operationId, "evidence:", "evidence_", "evidence-"))
        {
            issues.Add("EvidenceIdCannotReplaceOperationId");
        }

        if (HasAnyPrefix(operationId, "correlation:", "correlation_", "corr_"))
        {
            issues.Add("CorrelationIdCannotReplaceOperationId");
        }
    }

    private static void AddReferenceIssues(
        string operationId,
        IReadOnlyList<OperationIdentityReference> references,
        ICollection<string> issues)
    {
        var exactReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referenceKinds = new Dictionary<OperationReferenceKind, List<OperationIdentityReference>>();

        foreach (var reference in references)
        {
            if (reference.ReferenceKind == OperationReferenceKind.Unknown ||
                !Enum.IsDefined(reference.ReferenceKind))
            {
                issues.Add("OperationReferenceKindRequired");
            }

            if (string.IsNullOrWhiteSpace(reference.ReferenceId))
            {
                issues.Add("OperationReferenceIdRequired");
            }
            else
            {
                if (ContainsUnsafeText(reference.ReferenceId) || reference.ReferenceId.Any(char.IsWhiteSpace))
                {
                    issues.Add("OperationReferenceIdInvalid");
                }

                if (Same(reference.ReferenceId, operationId))
                {
                    issues.Add("OperationReferenceCannotReplaceOperationId");
                }
            }

            if (reference.ObservedAtUtc == default)
            {
                issues.Add("OperationReferenceObservedAtRequired");
            }

            if (string.IsNullOrWhiteSpace(reference.Source) || ContainsUnsafeText(reference.Source))
            {
                issues.Add("OperationReferenceSourceRequired");
            }

            var exactKey = $"{reference.ReferenceKind}:{reference.ReferenceId}";
            if (!exactReferences.Add(exactKey))
            {
                issues.Add("DuplicateOperationReference");
            }

            if (!referenceKinds.TryGetValue(reference.ReferenceKind, out var byKind))
            {
                byKind = [];
                referenceKinds[reference.ReferenceKind] = byKind;
            }

            byKind.Add(reference);
        }

        foreach (var referencesForKind in referenceKinds.Values.Where(static item => item.Count > 1))
        {
            var distinctIds = referencesForKind
                .Select(static item => item.ReferenceId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            if (distinctIds > 1 &&
                referencesForKind.Any(static item => item.ObservedAtUtc == default))
            {
                issues.Add("DuplicateReferenceKindRequiresOrderableReferences");
            }
        }
    }

    private static OperationIdentityValidationResult Invalid(IReadOnlyList<string> issues) =>
        new()
        {
            IsValid = false,
            Issues = issues,
            ForbiddenAuthorityImplications = ForbiddenAuthorityImplications,
            References = []
        };

    private static IReadOnlyList<OperationIdentityReference> NormalizeReferences(
        IReadOnlyList<OperationIdentityReference> references) =>
        references
            .OrderBy(static item => item.ObservedAtUtc)
            .ThenBy(static item => item.ReferenceKind)
            .ThenBy(static item => item.ReferenceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Source, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsAllowedTransition(
        OperationIdentityLifecycleState from,
        OperationIdentityLifecycleState to)
    {
        if (to is OperationIdentityLifecycleState.Failed or
            OperationIdentityLifecycleState.Interrupted or
            OperationIdentityLifecycleState.RolledBack or
            OperationIdentityLifecycleState.Completed)
        {
            return IsActiveState(from);
        }

        return (from, to) switch
        {
            (OperationIdentityLifecycleState.Unknown, OperationIdentityLifecycleState.Minted) => true,
            (OperationIdentityLifecycleState.Minted, OperationIdentityLifecycleState.LinkedToRun) => true,
            (OperationIdentityLifecycleState.LinkedToRun, OperationIdentityLifecycleState.LinkedToPatch) => true,
            (OperationIdentityLifecycleState.LinkedToPatch, OperationIdentityLifecycleState.LinkedToApply) => true,
            (OperationIdentityLifecycleState.LinkedToApply, OperationIdentityLifecycleState.LinkedToCommit) => true,
            (OperationIdentityLifecycleState.LinkedToCommit, OperationIdentityLifecycleState.LinkedToPush) => true,
            (OperationIdentityLifecycleState.LinkedToPush, OperationIdentityLifecycleState.LinkedToPullRequest) => true,
            _ => false
        };
    }

    private static bool IsActiveState(OperationIdentityLifecycleState state) =>
        state is OperationIdentityLifecycleState.Minted or
            OperationIdentityLifecycleState.LinkedToRun or
            OperationIdentityLifecycleState.LinkedToPatch or
            OperationIdentityLifecycleState.LinkedToApply or
            OperationIdentityLifecycleState.LinkedToCommit or
            OperationIdentityLifecycleState.LinkedToPush or
            OperationIdentityLifecycleState.LinkedToPullRequest;

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool HasAnyPrefix(string value, params string[] prefixes) =>
        prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsUnsafeText(string value) =>
        value.Any(char.IsControl) || value.Length > 256;

    [GeneratedRegex("^op_([0-9a-f]{8,64}|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$")]
    private static partial Regex CanonicalOperationIdPattern();

    [GeneratedRegex("^[0-9a-fA-F]{40}$")]
    private static partial Regex CommitShaPattern();
}
