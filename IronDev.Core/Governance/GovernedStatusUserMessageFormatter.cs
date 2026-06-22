namespace IronDev.Core.Governance;

public sealed record GovernedStatusUserMessage
{
    public required string OperationId { get; init; }
    public required string OperationKind { get; init; }
    public required string State { get; init; }
    public required string Subject { get; init; }

    public required IReadOnlyList<string> PlainReasons { get; init; }
    public required IReadOnlyList<string> PlainMissingEvidence { get; init; }
    public required IReadOnlyList<string> PlainNextSafeActions { get; init; }
    public required IReadOnlyList<string> PlainForbiddenActions { get; init; }
    public required IReadOnlyList<string> AuthorityWarnings { get; init; }

    public required IReadOnlyList<string> EvidenceRefs { get; init; }
    public required IReadOnlyList<string> ReceiptRefs { get; init; }

    public required bool CanApprove { get; init; }
    public required bool CanSatisfyPolicy { get; init; }
    public required bool CanExecute { get; init; }
    public required bool CanMutateSource { get; init; }
    public required bool CanContinueWorkflow { get; init; }
}

public static class GovernedStatusUserMessageFormatter
{
    public static GovernedStatusUserMessage Format(GovernedOperationStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        var validation = GovernedOperationStatusValidator.Validate(status);
        var subject = SubjectParts.Parse(status.Subject);
        var authorityWarnings = BuildAuthorityWarnings(status, subject);

        return new GovernedStatusUserMessage
        {
            OperationId = status.OperationId,
            OperationKind = status.OperationKind,
            State = status.State.ToString(),
            Subject = status.Subject,
            PlainReasons = FormatReasons(status, subject),
            PlainMissingEvidence = FormatMissingEvidence(status, subject),
            PlainNextSafeActions = FormatNextSafeActions(status, subject),
            PlainForbiddenActions = FormatForbiddenActions(status, subject),
            AuthorityWarnings = authorityWarnings,
            EvidenceRefs = Clean(status.EvidenceRefs),
            ReceiptRefs = Clean(status.ReceiptRefs),
            CanApprove = validation.Boundary.CanApprove,
            CanSatisfyPolicy = validation.Boundary.CanSatisfyPolicy,
            CanExecute = validation.Boundary.CanExecute,
            CanMutateSource = validation.Boundary.CanMutateSource,
            CanContinueWorkflow = validation.Boundary.CanContinueWorkflow
        };
    }

    private static IReadOnlyList<string> FormatReasons(
        GovernedOperationStatus status,
        SubjectParts subject)
    {
        var mapped = Clean(status.BlockedReasons)
            .Select(reason => MapReason(status, reason, subject))
            .ToList();

        if (mapped.Count == 0 && status.State == GovernedOperationState.Blocked)
            mapped.Add($"{DisplayOperation(status.OperationKind)} is blocked until the missing governed evidence is provided.");

        return Clean(mapped);
    }

    private static IReadOnlyList<string> FormatMissingEvidence(
        GovernedOperationStatus status,
        SubjectParts subject)
    {
        var missing = Clean(status.MissingEvidence)
            .Select(value => MapMissingEvidence(value, subject))
            .ToList();

        return missing.Count == 0 ? [] : Clean(missing);
    }

    private static IReadOnlyList<string> FormatNextSafeActions(
        GovernedOperationStatus status,
        SubjectParts subject)
    {
        var actions = Clean(status.NextSafeActions)
            .Select(action => MapNextAction(action, status, subject))
            .ToList();

        if (actions.Count == 0 && IsSourceApply(status))
            actions.Add(SourceApplyRequestAction(subject));

        return Clean(actions);
    }

    private static IReadOnlyList<string> FormatForbiddenActions(
        GovernedOperationStatus status,
        SubjectParts subject)
    {
        var forbidden = Clean(status.ForbiddenActions)
            .Select(MapForbiddenAction)
            .ToList();

        if (status.State == GovernedOperationState.Blocked && IsSourceApply(status))
        {
            forbidden.Add("do not apply source without explicit source-apply authority");
            forbidden.Add("do not treat patch package as source apply authority");
            forbidden.Add("do not treat validation as approval");
            forbidden.Add("do not treat freshness as authority");
        }

        if (Mentions(status, "draft-pull-request") || Mentions(status, "draft pr"))
            forbidden.Add("do not treat draft PR as ready-for-review authority");
        if (Mentions(status, "pull-request-url") || Mentions(status, "pr-url") || Mentions(status, "pr url"))
            forbidden.Add("do not treat PR URL as release candidate ref");

        forbidden.Add("do not continue workflow from status, receipt, memory, or UI text");
        return Clean(forbidden);
    }

    private static IReadOnlyList<string> BuildAuthorityWarnings(
        GovernedOperationStatus status,
        SubjectParts subject)
    {
        var warnings = new List<string>
        {
            "Status output explains governance. It does not approve, satisfy policy, execute, mutate source, or continue workflow.",
            "Next safe actions are guidance only and require their own governed authority."
        };

        if (HasRef(status, "validation") || Mentions(status, "validation passed"))
            warnings.Add("Validation evidence is not approval.");
        if (HasRef(status, "repo-freshness") || Mentions(status, "freshness"))
            warnings.Add("Freshness evidence says the repo state was checked; freshness is not authority.");
        if (HasRef(status, "patch-package") || HasRef(status, "patch-artifact"))
            warnings.Add("Patch package evidence is not source apply authority.");
        if (HasRef(status, "draft-pull-request") || Mentions(status, "draft PR"))
            warnings.Add("Draft PR evidence is not ready-for-review authority.");
        if (HasRef(status, "pull-request-url") || Mentions(status, "PR URL"))
            warnings.Add("A PR URL is not a release candidate reference.");
        if (Mentions(status, "recovery") || HasRef(status, "interrupted-run"))
            warnings.Add("Recovery diagnosis is read-only and does not resume a run.");
        if (Mentions(status, "rollback-plan") || HasRef(status, "rollback-plan"))
            warnings.Add("Rollback plan evidence is not rollback execution.");
        if (Mentions(status, "memory says") || HasRef(status, "memory"))
            warnings.Add("Memory text is not approval authority.");
        if (IsMemoryPackageStatus(status))
        {
            warnings.Add("Memory promotion package is not durable memory.");
            warnings.Add("Useful memory still needs permission before becoming durable memory.");
            warnings.Add("Memory cannot approve, satisfy policy, authorize mutation, promote itself, or continue workflow.");
        }
        if (Mentions(status, "ui says") || HasRef(status, "ui-state"))
            warnings.Add("UI text is not execution authority.");
        if (!string.IsNullOrWhiteSpace(subject.Repo) ||
            !string.IsNullOrWhiteSpace(subject.Branch) ||
            !string.IsNullOrWhiteSpace(subject.Run) ||
            !string.IsNullOrWhiteSpace(subject.Patch) ||
            !string.IsNullOrWhiteSpace(subject.Scope))
        {
            warnings.Add($"Scope: repo {ValueOrUnknown(subject.Repo)}, branch {ValueOrUnknown(subject.Branch)}, run {ValueOrUnknown(subject.Run)}, patch {ValueOrUnknown(subject.Patch)}, file scope {ValueOrUnknown(subject.Scope)}.");
        }

        return Clean(warnings);
    }

    private static string MapReason(
        GovernedOperationStatus status,
        string reason,
        SubjectParts subject)
    {
        if (IsSourceApply(status) &&
            (reason.Contains("MissingExplicitSourceApplyAuthority", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("NoBoundedAuthorityGrantForSourceApply", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("AskBeforeMutationRequiresSourceApplyApproval", StringComparison.OrdinalIgnoreCase)))
        {
            return $"Source apply is blocked because no accepted source-apply request or bounded SourceApply authority exists for repo {ValueOrUnknown(subject.Repo)}, branch {ValueOrUnknown(subject.Branch)}, run {ValueOrUnknown(subject.Run)}, patch {ValueOrUnknown(subject.Patch)}, and file scope {ValueOrUnknown(subject.Scope)}.";
        }

        if (reason.Contains("Freshness", StringComparison.OrdinalIgnoreCase))
            return $"{reason}: repo freshness must be checked, but freshness is not authority.";
        if (reason.Contains("Validation", StringComparison.OrdinalIgnoreCase))
            return $"{reason}: validation evidence must be explicit, and validation is not approval.";
        if (reason.Contains("Rollback", StringComparison.OrdinalIgnoreCase))
            return $"{reason}: rollback planning is not rollback execution.";

        return Humanize(reason);
    }

    private static string MapMissingEvidence(string value, SubjectParts subject)
    {
        if (value.StartsWith("bounded-authority-grant:SourceApply", StringComparison.OrdinalIgnoreCase))
            return $"Missing bounded SourceApply authority: a scoped permission record for repo {ValueOrUnknown(subject.Repo)}, branch {ValueOrUnknown(subject.Branch)}, run {ValueOrUnknown(subject.Run)}, patch {ValueOrUnknown(subject.Patch)}, and file scope {ValueOrUnknown(subject.Scope)}.";
        if (value.StartsWith("accepted-source-apply-request", StringComparison.OrdinalIgnoreCase))
            return $"Missing accepted source-apply request for this exact patch and file scope. Canonical ref: {value}.";
        if (value.StartsWith("policy-satisfaction", StringComparison.OrdinalIgnoreCase))
            return $"Missing policy satisfaction evidence: evidence that the required policy checks for this operation were met. Canonical ref: {value}.";
        if (value.StartsWith("dry-run", StringComparison.OrdinalIgnoreCase))
            return $"Missing dry-run evidence for this exact patch. Canonical ref: {value}.";

        return $"{Humanize(value)}. Canonical ref: {value}.";
    }

    private static string MapNextAction(
        string action,
        GovernedOperationStatus status,
        SubjectParts subject)
    {
        if (IsVague(action) && IsSourceApply(status))
            return SourceApplyRequestAction(subject);

        return action;
    }

    private static string SourceApplyRequestAction(SubjectParts subject) =>
        $"Review the patch package, then create a governed source-apply request bound to repo {ValueOrUnknown(subject.Repo)}, branch {ValueOrUnknown(subject.Branch)}, run {ValueOrUnknown(subject.Run)}, patch hash {ValueOrUnknown(subject.Patch)}, and file scope {ValueOrUnknown(subject.Scope)}.";

    private static string MapForbiddenAction(string value)
    {
        if (value.Contains("validation", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("approval", StringComparison.OrdinalIgnoreCase))
            return "do not treat validation as approval";
        if (value.Contains("freshness", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("authority", StringComparison.OrdinalIgnoreCase))
            return "do not treat freshness as authority";
        if (value.Contains("patch package", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("source apply", StringComparison.OrdinalIgnoreCase))
            return "do not treat patch package as source apply authority";

        return value;
    }

    private static bool IsSourceApply(GovernedOperationStatus status) =>
        string.Equals(status.OperationKind, "SourceApply", StringComparison.OrdinalIgnoreCase);

    private static bool IsMemoryPackageStatus(GovernedOperationStatus status) =>
        string.Equals(status.OperationKind, "Memory" + "PromotionPackage", StringComparison.OrdinalIgnoreCase);

    private static bool IsVague(string action)
    {
        var normalized = action.Trim();
        return normalized.Equals("Continue", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Request approval", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Fix issue", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Try again", StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayOperation(string operationKind) =>
        string.IsNullOrWhiteSpace(operationKind)
            ? "The operation"
            : Humanize(operationKind);

    private static bool HasRef(GovernedOperationStatus status, string prefix) =>
        ValuesOrEmpty(status.EvidenceRefs)
            .Concat(ValuesOrEmpty(status.ReceiptRefs))
            .Any(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool Mentions(GovernedOperationStatus status, string text) =>
        ValuesOrEmpty(status.BlockedReasons)
            .Concat(ValuesOrEmpty(status.MissingEvidence))
            .Concat(ValuesOrEmpty(status.NextSafeActions))
            .Concat(ValuesOrEmpty(status.ForbiddenActions))
            .Concat(ValuesOrEmpty(status.EvidenceRefs))
            .Concat(ValuesOrEmpty(status.ReceiptRefs))
            .Append(status.Subject)
            .Any(value => value.Contains(text, StringComparison.OrdinalIgnoreCase));

    private static string Humanize(string value)
    {
        var text = value.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return text;

        text = text.Replace(':', ' ').Replace('-', ' ').Replace('_', ' ');
        var builder = new List<char>(text.Length + 8);
        for (var i = 0; i < text.Length; i++)
        {
            var current = text[i];
            if (i > 0 && char.IsUpper(current) && char.IsLetterOrDigit(text[i - 1]))
                builder.Add(' ');
            builder.Add(current);
        }

        return string.Join(' ', new string(builder.ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string ValueOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value;

    private static IReadOnlyList<string> Clean(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> ValuesOrEmpty(IReadOnlyList<string>? values) =>
        values ?? [];

    private sealed record SubjectParts(string? Repo, string? Branch, string? Run, string? Patch, string? Scope)
    {
        public static SubjectParts Parse(string? subject)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in (subject ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = token.IndexOf(':');
                if (separator <= 0 || separator == token.Length - 1)
                    continue;

                values[token[..separator]] = token[(separator + 1)..];
            }

            values.TryGetValue("repo", out var repo);
            values.TryGetValue("branch", out var branch);
            values.TryGetValue("run", out var run);
            values.TryGetValue("patch", out var patch);
            values.TryGetValue("scope", out var scope);
            values.TryGetValue("file", out var file);

            return new SubjectParts(repo, branch, run, patch, scope ?? file);
        }
    }
}
