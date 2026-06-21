namespace IronDev.Core.Governance;

public static class GovernedOperationStatusValidator
{
    private static readonly string[] AuthorityLeakMarkers =
    [
        "approved because tests passed",
        "tests passed so approved",
        "apply allowed because patch exists",
        "memory says this was approved",
        "receipt exists so workflow can continue",
        "ui marked this as approved",
        "release receipt authorizes deployment",
        "deployment receipt authorizes workflow continuation",
        "evidence authorizes",
        "receipt authorizes",
        "memory authorizes",
        "ui authorizes",
        "policy satisfied by status",
        "approved by status",
        "execution authorized by status"
    ];

    private static readonly string[] DirectMutationActions =
    [
        "apply patch",
        "source apply now",
        "source-apply now",
        "commit",
        "push",
        "merge",
        "release",
        "deploy",
        "rollback",
        "continue workflow",
        "promote memory",
        "publish package",
        "dispatch pipeline"
    ];

    private static readonly string[] SafeActionPrefixes =
    [
        "ask ",
        "collect ",
        "create ",
        "inspect ",
        "observe ",
        "open ",
        "package ",
        "prepare ",
        "request ",
        "review "
    ];

    public static GovernedOperationStatusValidationResult Validate(GovernedOperationStatus status)
    {
        var issues = new List<string>();
        var redFlags = new List<string>();
        var amberFlags = new List<string>();

        RequireText(status.OperationId, "OperationIdRequired", issues);
        RequireText(status.OperationKind, "OperationKindRequired", issues);
        RequireText(status.Subject, "SubjectRequired", issues);

        if (!Enum.IsDefined(status.State))
            issues.Add("StateRequired");

        if (status.ObservedAtUtc == default)
            issues.Add("ObservedAtUtcRequired");

        ValidateStateShape(status, issues);
        ValidateForbiddenActions(status, issues);
        ValidateAuthorityLanguage(status, issues, redFlags);
        ValidateNextSafeActions(status, issues, redFlags, amberFlags);

        return new GovernedOperationStatusValidationResult
        {
            IsValid = issues.Count == 0 && redFlags.Count == 0,
            Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RedFlags = redFlags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            AmberFlags = amberFlags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Boundary = GovernedOperationStatusBoundary.Status
        };
    }

    private static void ValidateStateShape(GovernedOperationStatus status, ICollection<string> issues)
    {
        if (status.State == GovernedOperationState.Blocked)
        {
            if (IsEmpty(status.BlockedReasons))
                issues.Add("BlockedReasonRequired");
            if (IsEmpty(status.MissingEvidence) && IsEmpty(status.NextSafeActions))
                issues.Add("BlockedStatusNeedsEvidenceOrNextSafeAction");
        }

        if (status.State == GovernedOperationState.Eligible && !IsEmpty(status.BlockedReasons))
            issues.Add("EligibleStatusCannotCarryBlockedReasons");

        if (status.State == GovernedOperationState.Completed && IsEmpty(status.ReceiptRefs))
            issues.Add("CompletedStatusRequiresReceiptReference");

        if (status.State == GovernedOperationState.Expired &&
            status.ExpiresAtUtc is null &&
            !ContainsText(status.BlockedReasons, "expir"))
        {
            issues.Add("ExpiredStatusRequiresExpiryEvidence");
        }
    }

    private static void ValidateForbiddenActions(GovernedOperationStatus status, ICollection<string> issues)
    {
        if (!IsEmpty(status.ForbiddenActions) || IsReadOnlyOperation(status.OperationKind))
            return;

        issues.Add("ForbiddenActionsRequiredForAuthorityBearingOperation");
    }

    private static void ValidateAuthorityLanguage(
        GovernedOperationStatus status,
        ICollection<string> issues,
        ICollection<string> redFlags)
    {
        foreach (var marker in AuthorityLeakMarkers)
        {
            if (!ContainsText(AllTextFields(status), marker))
                continue;

            issues.Add("StatusImpliesAuthority");
            redFlags.Add(FlagForMarker(marker));
        }
    }

    private static void ValidateNextSafeActions(
        GovernedOperationStatus status,
        ICollection<string> issues,
        ICollection<string> redFlags,
        ICollection<string> amberFlags)
    {
        foreach (var action in ValuesOrEmpty(status.NextSafeActions).Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            if (IsGuidanceAction(action))
                continue;

            var normalized = action.Trim().ToLowerInvariant();
            if (DirectMutationActions.Any(marker => normalized.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add("NextSafeActionImpliesDirectMutation");
                redFlags.Add("UnsafeNextSafeActionWouldMutate");
                continue;
            }

            amberFlags.Add("UnclassifiedNextSafeAction");
        }
    }

    private static string FlagForMarker(string marker)
    {
        if (marker.Contains("memory", StringComparison.OrdinalIgnoreCase))
            return "MemoryReferenceCannotSatisfyAuthority";
        if (marker.Contains("ui", StringComparison.OrdinalIgnoreCase))
            return "UiStateCannotSatisfyAuthority";
        if (marker.Contains("receipt", StringComparison.OrdinalIgnoreCase))
            return "ReceiptReferenceCannotSatisfyAuthority";
        if (marker.Contains("evidence", StringComparison.OrdinalIgnoreCase) ||
            marker.Contains("tests passed", StringComparison.OrdinalIgnoreCase) ||
            marker.Contains("patch exists", StringComparison.OrdinalIgnoreCase))
        {
            return "EvidenceReferenceCannotSatisfyAuthority";
        }

        return "StatusCannotGrantAuthority";
    }

    private static bool IsGuidanceAction(string action)
    {
        var normalized = action.Trim().ToLowerInvariant();
        if (normalized.StartsWith("do not ", StringComparison.OrdinalIgnoreCase))
            return true;

        return SafeActionPrefixes.Any(prefix => normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsReadOnlyOperation(string operationKind) =>
        operationKind.Contains("read", StringComparison.OrdinalIgnoreCase) ||
        operationKind.Contains("inspect", StringComparison.OrdinalIgnoreCase) ||
        operationKind.Contains("status", StringComparison.OrdinalIgnoreCase);

    private static void RequireText(string value, string issue, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add(issue);
    }

    private static bool IsEmpty(IReadOnlyList<string>? values) =>
        values is null || values.Count == 0 || values.All(string.IsNullOrWhiteSpace);

    private static bool ContainsText(IEnumerable<string?> values, string marker) =>
        values.Any(value => value?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true);

    private static IEnumerable<string?> AllTextFields(GovernedOperationStatus status)
    {
        yield return status.OperationId;
        yield return status.OperationKind;
        yield return status.Subject;

        foreach (var value in ValuesOrEmpty(status.BlockedReasons))
            yield return value;
        foreach (var value in ValuesOrEmpty(status.MissingEvidence))
            yield return value;
        foreach (var value in ValuesOrEmpty(status.NextSafeActions))
            yield return value;
        foreach (var value in ValuesOrEmpty(status.ForbiddenActions))
            yield return value;
        foreach (var value in ValuesOrEmpty(status.EvidenceRefs))
            yield return value;
        foreach (var value in ValuesOrEmpty(status.ReceiptRefs))
            yield return value;
    }

    private static IEnumerable<string> ValuesOrEmpty(IReadOnlyList<string>? values) =>
        values ?? [];
}
