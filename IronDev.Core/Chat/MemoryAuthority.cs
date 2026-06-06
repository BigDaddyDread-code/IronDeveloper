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
