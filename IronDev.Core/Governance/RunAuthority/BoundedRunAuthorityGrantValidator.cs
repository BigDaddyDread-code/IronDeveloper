namespace IronDev.Core.Governance;

public static class BoundedRunAuthorityGrantValidator
{
    private static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;
    private static readonly IReadOnlyCollection<string> ForbiddenPrincipalKinds =
    [
        "Memory",
        "Model",
        "Agent",
        "UiState",
        "HistoricalReceipt",
        "Inferred",
        "Unknown"
    ];

    public static BoundedRunAuthorityGrantValidationResult Validate(
        BoundedRunAuthorityGrant? grant,
        DateTimeOffset observedAtUtc)
    {
        var issues = new List<string>();

        if (grant is null)
        {
            issues.Add("BoundedRunAuthorityGrantRequired");
            return Result(issues);
        }

        RequireText(grant.GrantId, "BoundedRunGrantIdRequired", issues);
        ValidateSingleScope(grant.Repository, "Repository", issues);
        ValidateSingleScope(grant.Branch, "Branch", issues);
        ValidateSingleScope(grant.RunId, "RunId", issues);
        ValidateAllowedOperations(grant.AllowedOperationKinds, issues);
        ValidateFileGlobs(grant.AllowedFileGlobs, "AllowedFileGlobs", requireNonEmpty: true, issues);
        ValidateFileGlobs(grant.ForbiddenFileGlobs, "ForbiddenFileGlobs", requireNonEmpty: false, issues);
        ValidateExpiry(grant.ExpiresAtUtc, observedAtUtc, issues);
        if (grant.MaxMutations < 0)
            issues.Add("BoundedRunMaxMutationsCannotBeNegative");
        ValidateRequiredValidation(grant.RequiredValidation, issues);
        ValidateStopBeforeOperations(grant.StopBeforeOperationKinds, issues);
        ValidateGrantedBy(grant.GrantedBy, issues);
        RequireText(grant.HumanReadableIntent, "BoundedRunHumanReadableIntentRequired", issues);

        return Result(issues);
    }

    private static void ValidateAllowedOperations(
        IReadOnlyCollection<RunAuthorityOperationKind>? operations,
        ICollection<string> issues)
    {
        if (operations is null || operations.Count == 0)
        {
            issues.Add("BoundedRunAllowedOperationKindsRequired");
            return;
        }

        foreach (var operation in operations)
        {
            ValidateOperation(operation, "BoundedRunAllowedOperationKindKnownRequired", issues);
            if (RunAuthorityProfileValidator.ProposalOnlyForbiddenOperations.Contains(operation))
                issues.Add($"BoundedRunAllowedOperationCannotCrossBoundary:{operation}");
        }
    }

    private static void ValidateStopBeforeOperations(
        IReadOnlyCollection<RunAuthorityOperationKind>? operations,
        ICollection<string> issues)
    {
        if (operations is null)
        {
            issues.Add("BoundedRunStopBeforeOperationKindsRequired");
            return;
        }

        foreach (var operation in operations)
            ValidateOperation(operation, "BoundedRunStopBeforeOperationKindKnownRequired", issues);
    }

    private static void ValidateOperation(
        RunAuthorityOperationKind operation,
        string issue,
        ICollection<string> issues)
    {
        if (!Enum.IsDefined(operation) || operation == RunAuthorityOperationKind.Unknown)
            issues.Add(issue);
    }

    private static void ValidateFileGlobs(
        IReadOnlyCollection<string>? globs,
        string label,
        bool requireNonEmpty,
        ICollection<string> issues)
    {
        if (globs is null)
        {
            issues.Add($"BoundedRun{label}Required");
            return;
        }

        if (requireNonEmpty && globs.Count == 0)
            issues.Add($"BoundedRun{label}Required");

        foreach (var glob in globs)
        {
            if (!BoundedRunAuthorityGrantFileScope.IsSafeRelativeGlob(glob))
                issues.Add($"BoundedRun{label}Unsafe:{glob}");
        }
    }

    private static void ValidateRequiredValidation(
        IReadOnlyCollection<BoundedRunAuthorityRequiredValidation>? validations,
        ICollection<string> issues)
    {
        if (validations is null || validations.Count == 0)
        {
            issues.Add("BoundedRunRequiredValidationRequired");
            return;
        }

        foreach (var validation in validations)
        {
            if (string.IsNullOrWhiteSpace(validation.ValidationKind))
                issues.Add("BoundedRunRequiredValidationKindRequired");
            if (validation.EvidenceRefPrefixes is null || validation.EvidenceRefPrefixes.Count == 0)
            {
                issues.Add("BoundedRunRequiredValidationEvidenceRefPrefixesRequired");
                continue;
            }

            foreach (var prefix in validation.EvidenceRefPrefixes)
            {
                if (string.IsNullOrWhiteSpace(prefix))
                    issues.Add("BoundedRunRequiredValidationEvidenceRefPrefixRequired");
            }
        }
    }

    private static void ValidateGrantedBy(BoundedRunAuthorityGrantedBy? grantedBy, ICollection<string> issues)
    {
        if (grantedBy is null)
        {
            issues.Add("BoundedRunGrantedByRequired");
            return;
        }

        RequireText(grantedBy.PrincipalId, "BoundedRunGrantedByPrincipalIdRequired", issues);
        RequireText(grantedBy.EvidenceRef, "BoundedRunGrantedByEvidenceRefRequired", issues);

        if (string.IsNullOrWhiteSpace(grantedBy.PrincipalKind))
        {
            issues.Add("BoundedRunGrantedByPrincipalKindRequired");
            return;
        }

        if (ForbiddenPrincipalKinds.Contains(grantedBy.PrincipalKind, IgnoreCase))
        {
            issues.Add($"BoundedRunGrantedByPrincipalKindForbidden:{grantedBy.PrincipalKind}");
            return;
        }

        if (!string.Equals(grantedBy.PrincipalKind, "Human", StringComparison.OrdinalIgnoreCase))
            issues.Add($"BoundedRunGrantedByPrincipalKindUnsupported:{grantedBy.PrincipalKind}");
    }

    private static void ValidateExpiry(
        DateTimeOffset expiresAtUtc,
        DateTimeOffset observedAtUtc,
        ICollection<string> issues)
    {
        if (expiresAtUtc == default)
        {
            issues.Add("BoundedRunExpiresAtUtcRequired");
            return;
        }

        if (expiresAtUtc.Offset != TimeSpan.Zero)
            issues.Add("BoundedRunExpiresAtUtcMustBeUtc");
        if (expiresAtUtc <= observedAtUtc)
            issues.Add("BoundedRunGrantExpired");
    }

    private static void ValidateSingleScope(string value, string label, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"BoundedRun{label}Required");
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('*', StringComparison.Ordinal) ||
            trimmed.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("any", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"BoundedRun{label}MustBeSingleExplicitScope");
        }
    }

    private static void RequireText(string value, string issue, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(issue);
    }

    private static BoundedRunAuthorityGrantValidationResult Result(IEnumerable<string> issues)
    {
        var issueList = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new BoundedRunAuthorityGrantValidationResult
        {
            IsValid = issueList.Length == 0,
            Issues = issueList
        };
    }
}
