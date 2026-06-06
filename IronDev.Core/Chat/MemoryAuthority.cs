namespace IronDev.Core.Chat;

public static class MemoryAuthorityLevels
{
    public const string Accepted = "Accepted";
    public const string Proposed = "Proposed";
    public const string Draft = "Draft";
    public const string ObservedFact = "ObservedFact";
    public const string RuntimeTrace = "RuntimeTrace";
    public const string TestEvidence = "TestEvidence";
    public const string Superseded = "Superseded";
    public const string Deprecated = "Deprecated";
    public const string Unknown = "Unknown";
}

public static class MemoryAuthorityNormalizer
{
    public static string FromDecisionStatus(string? status) =>
        Normalize(status) switch
        {
            "accepted" or "approved" or "committed" or "resolved" => MemoryAuthorityLevels.Accepted,
            "proposed" or "pending" or "candidate" or "open" => MemoryAuthorityLevels.Proposed,
            "draft" => MemoryAuthorityLevels.Draft,
            "superseded" => MemoryAuthorityLevels.Superseded,
            "deprecated" or "archived" or "rejected" or "cancelled" or "canceled" => MemoryAuthorityLevels.Deprecated,
            _ => MemoryAuthorityLevels.Unknown
        };

    public static string FromDocumentAuthority(string? authorityLevel, string? status = null)
    {
        var normalizedStatus = Normalize(status);
        if (normalizedStatus is "superseded")
            return MemoryAuthorityLevels.Superseded;
        if (normalizedStatus is "deprecated" or "archived" or "rejected" or "cancelled" or "canceled")
            return MemoryAuthorityLevels.Deprecated;

        return Normalize(authorityLevel) switch
        {
            "accepted" or "binding" or "required" or "blocking" or "committed" or "acceptedarchitecture" or "resolvedknowledge" => MemoryAuthorityLevels.Accepted,
            "proposed" or "proposal" or "pending" or "candidate" or "openquestion" => MemoryAuthorityLevels.Proposed,
            "draft" => MemoryAuthorityLevels.Draft,
            "observedfact" or "fact" or "contextonly" => MemoryAuthorityLevels.ObservedFact,
            "runtimetrace" or "trace" => MemoryAuthorityLevels.RuntimeTrace,
            "testevidence" or "test" => MemoryAuthorityLevels.TestEvidence,
            "superseded" => MemoryAuthorityLevels.Superseded,
            "deprecated" or "archived" or "rejected" => MemoryAuthorityLevels.Deprecated,
            _ => MemoryAuthorityLevels.Unknown
        };
    }

    public static string FromTicketState(bool isGenerated, string? status)
    {
        var normalized = Normalize(status);
        if (normalized is "superseded")
            return MemoryAuthorityLevels.Superseded;
        if (normalized is "archived" or "deprecated" or "deleted" or "rejected" or "cancelled" or "canceled")
            return MemoryAuthorityLevels.Deprecated;
        if (normalized is "accepted" or "done" or "completed" or "closed")
            return MemoryAuthorityLevels.ObservedFact;
        if (isGenerated)
            return MemoryAuthorityLevels.Draft;

        return normalized switch
        {
            "draft" => MemoryAuthorityLevels.Draft,
            "proposed" or "pending" or "candidate" => MemoryAuthorityLevels.Proposed,
            "active" or "open" or "inprogress" or "inreview" or "backlog" => MemoryAuthorityLevels.ObservedFact,
            _ => MemoryAuthorityLevels.Unknown
        };
    }

    public static string FromRuleEnforcementLevel(string? enforcementLevel) =>
        Normalize(enforcementLevel) switch
        {
            "required" or "blocking" or "binding" or "must" => MemoryAuthorityLevels.Accepted,
            "advisory" or "recommended" or "should" => MemoryAuthorityLevels.Proposed,
            "deprecated" or "archived" => MemoryAuthorityLevels.Deprecated,
            _ => MemoryAuthorityLevels.Unknown
        };

    public static string FromSemanticAuthority(string? authorityLevel) =>
        FromDocumentAuthority(authorityLevel);

    public static string RuntimeTrace => MemoryAuthorityLevels.RuntimeTrace;

    public static string TestEvidence => MemoryAuthorityLevels.TestEvidence;

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim()
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
}

public sealed record MemoryCurrentness(
    bool IsCurrent,
    string? StalenessReason = null,
    string? SupersededBySourceId = null);

public static class MemoryCurrentnessNormalizer
{
    public static MemoryCurrentness FromDecisionStatus(string? status, string? supersededBySourceId = null) =>
        Normalize(status) switch
        {
            "accepted" or "active" or "resolved" => Current(),
            "superseded" => Stale("Decision has been superseded.", supersededBySourceId),
            "deprecated" or "archived" or "rejected" or "cancelled" or "canceled" => Stale($"Decision status is {Display(status)}."),
            "draft" or "proposed" or "pending" or "candidate" or "open" => Stale("Decision is not accepted project truth."),
            _ => Stale("Decision currentness is unknown.")
        };

    public static MemoryCurrentness FromDocumentStatus(
        string? status,
        bool isLatestVersion = true,
        string? supersededBySourceId = null)
    {
        var normalized = Normalize(status);
        if (normalized is "superseded")
            return Stale("Document has been superseded.", supersededBySourceId);
        if (normalized is "deprecated" or "archived" or "rejected" or "cancelled" or "canceled")
            return Stale($"Document status is {Display(status)}.");
        if (!isLatestVersion)
            return Stale("Document is not the latest/current version.", supersededBySourceId);
        if (normalized is "active")
            return Current();

        return Stale("Document currentness is unknown.");
    }

    public static MemoryCurrentness FromTicketState(string? status, bool isDeleted = false, string? supersededBySourceId = null)
    {
        if (isDeleted)
            return Stale("Ticket has been deleted.");

        return Normalize(status) switch
        {
            "draft" or "active" or "inprogress" or "inreview" or "backlog" or "open" => Current(),
            "superseded" => Stale("Ticket has been superseded.", supersededBySourceId),
            "done" or "completed" or "closed" => Stale("Ticket is historical completed work."),
            "cancelled" or "canceled" or "rejected" or "archived" or "deprecated" => Stale($"Ticket status is {Display(status)}."),
            _ => Stale("Ticket currentness is unknown.")
        };
    }

    public static MemoryCurrentness FromRuleEnforcementLevel(string? enforcementLevel, string? supersededBySourceId = null) =>
        Normalize(enforcementLevel) switch
        {
            "required" or "blocking" or "binding" or "must" or "advisory" or "recommended" or "should" => Current(),
            "superseded" => Stale("Rule has been superseded.", supersededBySourceId),
            "deprecated" or "archived" => Stale($"Rule enforcement level is {Display(enforcementLevel)}."),
            _ => Stale("Rule currentness is unknown.")
        };

    public static MemoryCurrentness FromSemanticResult(
        bool isStale,
        MemoryCurrentness sourceCurrentness,
        string? supersededBySourceId = null)
    {
        if (isStale)
            return Stale("Semantic result or chunk is marked stale.", supersededBySourceId ?? sourceCurrentness.SupersededBySourceId);

        return sourceCurrentness.IsCurrent
            ? Current()
            : sourceCurrentness;
    }

    public static MemoryCurrentness RuntimeTrace(bool isCurrentTurn = true) =>
        isCurrentTurn
            ? Current()
            : Stale("Runtime trace is historical evidence only.");

    public static MemoryCurrentness TestEvidence(bool isCurrentRun = true) =>
        isCurrentRun
            ? Current()
            : Stale("Test evidence is from an older run.");

    private static MemoryCurrentness Current() => new(true);

    private static MemoryCurrentness Stale(string reason, string? supersededBySourceId = null) =>
        new(false, reason, supersededBySourceId);

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim()
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
}
