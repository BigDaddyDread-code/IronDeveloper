namespace IronDev.Core.Governance;

public static class GovernedOperationStatusInspector
{
    public static GovernedOperationStatusInspectResult Inspect(GovernedOperationStatusInspectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var status = request.Status;
        var validation = ApplyInspectValidation(status, GovernedOperationStatusValidator.Validate(status));
        var validationIssues = request.IncludeValidation ? LinesOrNone(validation.Issues) : [];
        var redFlags = request.IncludeValidation ? LinesOrNone(validation.RedFlags) : [];
        var amberFlags = request.IncludeValidation ? LinesOrNone(validation.AmberFlags) : [];

        return new GovernedOperationStatusInspectResult
        {
            Status = status,
            Validation = validation,
            Summary = BuildSummary(status, validation),
            StateLines = BuildStateLines(status),
            BlockedReasonLines = LinesOrNone(status.BlockedReasons),
            MissingEvidenceLines = LinesOrNone(status.MissingEvidence),
            NextSafeActionLines = GuidanceLines(status.NextSafeActions),
            ForbiddenActionLines = LinesOrNone(status.ForbiddenActions),
            EvidenceRefLines = request.IncludeRefs ? ReferenceLines(status.EvidenceRefs) : [],
            ReceiptRefLines = request.IncludeRefs ? ReferenceLines(status.ReceiptRefs) : [],
            ValidationIssueLines = validationIssues,
            RedFlagLines = redFlags,
            AmberFlagLines = amberFlags,
            BoundaryLines = BoundaryLines(status),
            ResultLines = ResultLines(validation),
            IsValid = validation.IsValid,
            HasAuthorityRedFlags = validation.RedFlags.Count > 0
        };
    }

    public static string FormatText(GovernedOperationStatusInspectResult result)
    {
        var lines = new List<string>
        {
            $"Operation: {result.Status.OperationKind}",
            $"State: {result.Status.State}",
            $"Subject: {result.Status.Subject}",
            $"Operation id: {result.Status.OperationId}",
            $"Observed: {result.Status.ObservedAtUtc:O}"
        };

        if (result.Status.ExpiresAtUtc is not null)
            lines.Add($"Expires: {result.Status.ExpiresAtUtc:O}");

        lines.Add(string.Empty);
        lines.Add($"Summary: {result.Summary}");

        AddSection(lines, "State", result.StateLines);
        AddSection(lines, "Blocked", result.BlockedReasonLines);
        AddSection(lines, "Missing evidence", result.MissingEvidenceLines);
        AddSection(lines, "Next safe action", result.NextSafeActionLines);
        AddSection(lines, "Forbidden", result.ForbiddenActionLines);
        AddSection(lines, "Evidence refs", result.EvidenceRefLines);
        AddSection(lines, "Receipt refs", result.ReceiptRefLines);
        AddSection(lines, "Validation", result.IsValid ? ["valid"] : ["invalid"]);
        AddSection(lines, "Issues", result.ValidationIssueLines);
        AddSection(lines, "Red flags", result.RedFlagLines);
        AddSection(lines, "Amber flags", result.AmberFlagLines);
        AddSection(lines, "Boundary", result.BoundaryLines);
        AddSection(lines, "Result", result.ResultLines);

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildSummary(GovernedOperationStatus status, GovernedOperationStatusValidationResult validation)
    {
        var validity = validation.IsValid ? "valid" : "invalid";
        return $"{validity} {status.OperationKind} {status.State} status for {status.Subject}";
    }

    private static GovernedOperationStatusValidationResult ApplyInspectValidation(
        GovernedOperationStatus status,
        GovernedOperationStatusValidationResult validation)
    {
        var issues = validation.Issues.ToList();
        var redFlags = validation.RedFlags.ToList();

        if (string.Equals(status.OperationKind, "SourceApply", StringComparison.OrdinalIgnoreCase) &&
            status.State == GovernedOperationState.Eligible &&
            !HasRefPrefix(status.EvidenceRefs, "policy-satisfaction"))
        {
            issues.Add("EligibleSourceApplyPolicySatisfactionRequired");
        }

        return validation with
        {
            IsValid = issues.Count == 0 && redFlags.Count == 0,
            Issues = issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            RedFlags = redFlags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static bool HasRefPrefix(IReadOnlyList<string>? values, string prefix) =>
        ValuesOrEmpty(values).Any(value => value.StartsWith($"{prefix}:", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> BuildStateLines(GovernedOperationStatus status)
    {
        var lines = new List<string>
        {
            $"operation kind: {status.OperationKind}",
            $"operation state: {status.State}",
            $"operation id: {status.OperationId}",
            $"observed at: {status.ObservedAtUtc:O}"
        };

        if (status.ExpiresAtUtc is not null)
            lines.Add($"expires at: {status.ExpiresAtUtc:O}");

        return lines;
    }

    private static IReadOnlyList<string> GuidanceLines(IReadOnlyList<string>? values)
    {
        var lines = ValuesOrEmpty(values).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => $"{value} (guidance only)").ToArray();
        return lines.Length == 0 ? ["none"] : lines;
    }

    private static IReadOnlyList<string> ReferenceLines(IReadOnlyList<string>? values)
    {
        var lines = ValuesOrEmpty(values).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => $"{value} (reference only)").ToArray();
        return lines.Length == 0 ? ["none"] : lines;
    }

    private static IReadOnlyList<string> BoundaryLines(GovernedOperationStatus status)
    {
        var lines = new List<string>
        {
            "status inspect is read-only",
            "inspect output is not approval",
            "inspect output is not policy satisfaction",
            "inspect output is not execution authority",
            "inspect output is not evidence",
            "inspect output is not a receipt",
            "next safe actions are guidance only",
            "evidence refs and receipt refs are references only",
            "inspect does not mutate source",
            "inspect does not perform memory promotion",
            "inspect does not perform workflow continuation"
        };

        if (status.State == GovernedOperationState.Eligible)
            lines.Add("eligible status is explanation, not execution authority");

        if (status.State == GovernedOperationState.Completed)
            lines.Add("completed status is not authority for the next governed operation");

        return lines;
    }

    private static IReadOnlyList<string> ResultLines(GovernedOperationStatusValidationResult validation)
    {
        if (validation.IsValid)
            return ["status can be used as a read-only explanation only"];

        return ["status cannot be used as a trusted explanation until fixed"];
    }

    private static IReadOnlyList<string> LinesOrNone(IReadOnlyList<string>? values)
    {
        var lines = ValuesOrEmpty(values).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return lines.Length == 0 ? ["none"] : lines;
    }

    private static IEnumerable<string> ValuesOrEmpty(IReadOnlyList<string>? values) =>
        values ?? [];

    private static void AddSection(List<string> lines, string title, IReadOnlyList<string> values)
    {
        lines.Add(string.Empty);
        lines.Add($"{title}:");
        foreach (var value in values)
            lines.Add($"- {value}");
    }
}
