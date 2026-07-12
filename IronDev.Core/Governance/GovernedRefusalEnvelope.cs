namespace IronDev.Core.Governance;

public sealed record GovernedRefusalEnvelope
{
    public bool Allowed { get; init; } = false;
    public required string ReasonCode { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<string> BlockedReasons { get; init; } = [];
    public IReadOnlyList<string> MissingEvidence { get; init; } = [];
    public IReadOnlyList<string> NextSafeActions { get; init; } = [];
    public IReadOnlyList<string> ForbiddenActions { get; init; } = [];
    public required string CorrelationId { get; init; }
}

public static class GovernedRefusal
{
    public static GovernedRefusalEnvelope Create(
        string reasonCode,
        string message,
        string correlationId,
        IEnumerable<string>? blockedReasons = null,
        IEnumerable<string>? missingEvidence = null,
        IEnumerable<string>? nextSafeActions = null,
        IEnumerable<string>? forbiddenActions = null) =>
        new()
        {
            Allowed = false,
            ReasonCode = Required(reasonCode, nameof(reasonCode)),
            Message = Required(message, nameof(message)),
            CorrelationId = Required(correlationId, nameof(correlationId)),
            BlockedReasons = Clean(blockedReasons),
            MissingEvidence = Clean(missingEvidence),
            NextSafeActions = Clean(nextSafeActions),
            ForbiddenActions = Clean(forbiddenActions)
        };

    private static IReadOnlyList<string> Clean(IEnumerable<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];

    private static string Required(string value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Canonical refusal values must not be blank.", name)
            : value.Trim();
}
