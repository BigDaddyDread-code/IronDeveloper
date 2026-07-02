namespace IronDev.Core.Governance;

public static class GovernanceEventSchemaVersions
{
    public const int LegacyUnversioned = 0;
    public const int Current = 1;
}

public enum GovernanceEventSchemaVersionClassification
{
    Current = 1,
    LegacyUnversioned = 2,
    DeprecatedReadable = 3,
    UnknownFuture = 4,
    Unsupported = 5,
    Invalid = 6
}

public sealed record GovernanceEventSchemaVersionDecision(
    GovernanceEventSchemaVersionClassification Classification,
    bool CanWrite,
    bool CanReadForDiagnostics,
    bool CanInterpretPayload,
    bool CanSatisfyAuthorityChecks,
    string Reason);

public static class GovernanceEventSchemaVersioning
{
    public static GovernanceEventSchemaVersionDecision Classify(int? payloadVersion)
    {
        if (payloadVersion is null)
            return Decision(GovernanceEventSchemaVersionClassification.Invalid, false, false, false, "Schema version is required.");

        if (payloadVersion < GovernanceEventSchemaVersions.LegacyUnversioned)
            return Decision(GovernanceEventSchemaVersionClassification.Invalid, false, false, false, "Schema version must not be negative.");

        if (payloadVersion == GovernanceEventSchemaVersions.LegacyUnversioned)
            return Decision(GovernanceEventSchemaVersionClassification.LegacyUnversioned, false, true, false, "Legacy unversioned event can be read for diagnostics only.");

        if (payloadVersion == GovernanceEventSchemaVersions.Current)
            return Decision(GovernanceEventSchemaVersionClassification.Current, true, true, true, "Current governance event schema version is explicit.");

        return Decision(GovernanceEventSchemaVersionClassification.UnknownFuture, false, true, false, "Unknown future schema version can be surfaced as metadata only.");
    }

    public static GovernanceEventSchemaVersionDecision ForNewEvent(int payloadVersion)
    {
        var decision = Classify(payloadVersion);
        return decision.CanWrite
            ? decision
            : decision with { Reason = $"{decision.Reason} New governance events must use the explicit current schema version." };
    }

    private static GovernanceEventSchemaVersionDecision Decision(
        GovernanceEventSchemaVersionClassification classification,
        bool canWrite,
        bool canReadForDiagnostics,
        bool canInterpretPayload,
        string reason) =>
        new(
            classification,
            canWrite,
            canReadForDiagnostics,
            canInterpretPayload,
            CanSatisfyAuthorityChecks: false,
            reason);
}
