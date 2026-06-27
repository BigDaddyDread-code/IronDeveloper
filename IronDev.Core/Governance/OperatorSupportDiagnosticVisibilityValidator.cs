using System.Text.RegularExpressions;

namespace IronDev.Core.Governance;

public static partial class OperatorSupportDiagnosticVisibilityValidator
{
    public const int MaxSafeTextLength = 360;

    private static readonly string[] UnsafeEvidenceMarkers =
    [
        "isoperator = true",
        "issupport = true",
        "operatorgranted = true",
        "supportgranted = true",
        "canrundiagnostic = true",
        "validationrefreshed = true",
        "sourcesafetyproven = true",
        "canretry = true",
        "canrollback = true",
        "canrecover = true",
        "canmutate = true",
        "canapplypatch = true",
        "cancommit = true",
        "canpush = true",
        "cancreatepullrequest = true",
        "canreadyforreview = true",
        "canmerge = true",
        "canrelease = true",
        "candeploy = true",
        "canapprove = true",
        "satisfiespolicy = true",
        "cancontinue = true",
        "accessgranted = true",
        "bypassredaction = true",
        "showrawlog = true",
        "showrawpayload = true",
        "showsecrets = true",
        "showprivatereasoning = true",
        "access granted",
        "permission granted",
        "retry authorized",
        "rollback authorized",
        "recovery authorized",
        "workflow continued",
        "validation refreshed",
        "source safety proven",
        "raw log",
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
        string.Concat("connection", "string", "="),
        string.Concat("private ", "key")
    ];

    public static OperatorSupportDiagnosticVisibilityValidationResult ValidateRequest(
        OperatorSupportDiagnosticVisibilityRequest? request)
    {
        if (request is null)
        {
            return Result(["OperatorSupportDiagnosticVisibilityRequestRequired"], []);
        }

        var issues = new List<string>();
        var unsafeRefs = new List<string>();

        AddRequiredSafeTextIssue(request.CorrelationId, "CorrelationId", issues, unsafeRefs);
        AddRoleKeyIssue(request.RequestedRoleKey, issues, unsafeRefs);
        AddKnownEnumIssue(request.RequestedSurface, RoleVisibilitySurface.Unknown, "RequestedSurfaceUnknown", issues);
        AddDefinedEnumIssue(request.RequestedMaterialKind, "RequestedMaterialKindInvalid", issues);
        AddDefinedEnumIssue(request.RequestedIntent, "RequestedIntentInvalid", issues);
        AddOptionalSafeTextIssue(request.DiagnosticEvidenceRef, "DiagnosticEvidenceRef", issues, unsafeRefs);
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

    public static bool IsActionIntent(OperatorSupportDiagnosticRequestedIntent intent) =>
        intent is not OperatorSupportDiagnosticRequestedIntent.ReadOnlyInspect and
            not OperatorSupportDiagnosticRequestedIntent.ReadOnlySummarise and
            not OperatorSupportDiagnosticRequestedIntent.Unknown;

    public static OperatorSupportDiagnosticVisibilityClassification ClassifyIntentBlock(
        OperatorSupportDiagnosticRequestedIntent intent) =>
        intent switch
        {
            OperatorSupportDiagnosticRequestedIntent.RunDiagnostic or
            OperatorSupportDiagnosticRequestedIntent.RefreshValidation or
            OperatorSupportDiagnosticRequestedIntent.ProveSourceSafety =>
                OperatorSupportDiagnosticVisibilityClassification.BlockedByDiagnosticExecutionIntent,
            OperatorSupportDiagnosticRequestedIntent.ExecuteRetry =>
                OperatorSupportDiagnosticVisibilityClassification.BlockedByRetryIntent,
            OperatorSupportDiagnosticRequestedIntent.ExecuteRollback =>
                OperatorSupportDiagnosticVisibilityClassification.BlockedByRollbackIntent,
            OperatorSupportDiagnosticRequestedIntent.ExecuteRecovery =>
                OperatorSupportDiagnosticVisibilityClassification.BlockedByRecoveryIntent,
            OperatorSupportDiagnosticRequestedIntent.MutateSource or
            OperatorSupportDiagnosticRequestedIntent.ApplyPatch or
            OperatorSupportDiagnosticRequestedIntent.Commit or
            OperatorSupportDiagnosticRequestedIntent.Push or
            OperatorSupportDiagnosticRequestedIntent.CreatePullRequest or
            OperatorSupportDiagnosticRequestedIntent.ReadyForReview or
            OperatorSupportDiagnosticRequestedIntent.Merge or
            OperatorSupportDiagnosticRequestedIntent.Release or
            OperatorSupportDiagnosticRequestedIntent.Deploy =>
                OperatorSupportDiagnosticVisibilityClassification.BlockedByMutationIntent,
            _ => OperatorSupportDiagnosticVisibilityClassification.BlockedByActionIntent
        };

    private static OperatorSupportDiagnosticVisibilityValidationResult Result(
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
