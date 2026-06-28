using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class ApproverRoleRequestDecisionVisibilityValidator
{
    public const int MaxSafeTextLength = 360;

    private static readonly string[] UnsafeEvidenceMarkers =
    [
        "isapprover = true",
        "approvergranted = true",
        "approverassigned = true",
        "canapprove = true",
        "approvalaccepted = true",
        "approvalgranted = true",
        "approvalsatisfied = true",
        "satisfiesapprovalgate = true",
        "satisfiespolicy = true",
        "canmerge = true",
        "canmutate = true",
        "cancontinue = true",
        "canrelease = true",
        "candeploy = true",
        "bypassredaction = true",
        "showrawpayload = true",
        "showprivatereasoning = true",
        "access granted",
        "permission granted",
        "approval granted",
        "approval accepted",
        "policy satisfied",
        "merge authorized",
        "release authorized",
        "deploy authorized",
        "raw payload",
        "raw diff",
        "raw patch",
        "raw source",
        "source file content",
        "raw provider response",
        "private reasoning",
        "chain-of-thought",
        "scratchpad",
        string.Concat("bear", "er "),
        string.Concat("to", "ken", "="),
        string.Concat("sec", "ret", "="),
        string.Concat("pass", "word", "="),
        string.Concat("private ", "key")
    ];

    public static ApproverRoleRequestDecisionVisibilityValidationResult ValidateRequest(
        ApproverRoleRequestDecisionVisibilityRequest? request)
    {
        if (request is null)
        {
            return Result(["ApproverRoleRequestDecisionVisibilityRequestRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddRequiredSafeTextIssue(request.CorrelationId, "CorrelationId", issues, unsafeRefs);
        AddRoleKeyIssue(request.RequestedRoleKey, issues, unsafeRefs);
        AddKnownEnumIssue(request.RequestedSurface, RoleVisibilitySurface.Unknown, "RequestedSurfaceUnknown", issues);
        AddDefinedEnumIssue(request.RequestedMaterialKind, "RequestedMaterialKindInvalid", issues);
        AddDefinedEnumIssue(request.RequestedIntent, "RequestedIntentInvalid", issues);
        AddOptionalSafeTextIssue(request.ApproverRequestDecisionEvidenceRef, "ApproverRequestDecisionEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.RoleCatalogEvidenceRef, "RoleCatalogEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.VisibilityMatrixEvidenceRef, "VisibilityMatrixEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalPolicyEvidenceRef, "OptionalPolicyEvidenceRef", issues, unsafeRefs);
        AddOptionalSafeTextIssue(request.OptionalRedactionEvidenceRef, "OptionalRedactionEvidenceRef", issues, unsafeRefs);

        return Result(issues, unsafeRefs);
    }

    public static bool ContainsUnsafeEvidenceText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.ToLowerInvariant();
        return UnsafeEvidenceMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    public static bool IsActionIntent(ApproverRoleRequestDecisionRequestedIntent intent) =>
        intent is not ApproverRoleRequestDecisionRequestedIntent.ReadOnlyInspect and
            not ApproverRoleRequestDecisionRequestedIntent.ReadOnlySummarise and
            not ApproverRoleRequestDecisionRequestedIntent.Unknown;

    public static bool IsApprovalIntent(ApproverRoleRequestDecisionRequestedIntent intent) =>
        intent is ApproverRoleRequestDecisionRequestedIntent.CreateApproverRequest or
            ApproverRoleRequestDecisionRequestedIntent.GrantApproverRole or
            ApproverRoleRequestDecisionRequestedIntent.AssignApproverRole or
            ApproverRoleRequestDecisionRequestedIntent.ApprovalAuthority or
            ApproverRoleRequestDecisionRequestedIntent.ApprovalAcceptance;

    private static ApproverRoleRequestDecisionVisibilityValidationResult Result(
        IReadOnlyList<string> issues,
        IReadOnlyList<string> unsafeRefs) =>
        new()
        {
            IsValid = issues.Count == 0,
            Issues = issues
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static issue => issue, StringComparer.Ordinal)
                .ToArray(),
            UnsafeRefs = unsafeRefs
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static item => item, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

    private static void AddRoleKeyIssue(
        string? roleKey,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(roleKey))
        {
            issues.Add("RequestedRoleKeyRequired");
            return;
        }

        if (ContainsUnsafeEvidenceText(roleKey))
        {
            issues.Add("RequestedRoleKeyUnsafe");
            unsafeRefs.Add(roleKey);
            return;
        }

        if (!RoleCatalogValidator.IsSafeRoleId(roleKey))
        {
            issues.Add("RequestedRoleKeyInvalid");
        }
    }

    private static void AddKnownEnumIssue<TEnum>(
        TEnum value,
        TEnum unknownValue,
        string issue,
        ICollection<string> issues)
        where TEnum : struct, Enum
    {
        if (EqualityComparer<TEnum>.Default.Equals(value, unknownValue) || !Enum.IsDefined(value))
        {
            issues.Add(issue);
        }
    }

    private static void AddDefinedEnumIssue<TEnum>(
        TEnum value,
        string issue,
        ICollection<string> issues)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            issues.Add(issue);
        }
    }

    private static void AddRequiredSafeTextIssue(
        string? value,
        string issuePrefix,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{issuePrefix}Required");
            return;
        }

        AddSafeTextIssue(value, issuePrefix, issues, unsafeRefs);
    }

    private static void AddOptionalSafeTextIssue(
        string? value,
        string issuePrefix,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AddSafeTextIssue(value, issuePrefix, issues, unsafeRefs);
    }

    private static void AddSafeTextIssue(
        string value,
        string issuePrefix,
        ICollection<string> issues,
        ICollection<string> unsafeRefs)
    {
        if (ContainsUnsafeEvidenceText(value))
        {
            issues.Add($"{issuePrefix}Unsafe");
            unsafeRefs.Add(value);
            return;
        }

        if (value.Length > MaxSafeTextLength ||
            value.Any(static ch => char.IsControl(ch)) ||
            !SafeTextPattern().IsMatch(value))
        {
            issues.Add($"{issuePrefix}Invalid");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9 ._:/@+,'()-]{1,359}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTextPattern();
}
