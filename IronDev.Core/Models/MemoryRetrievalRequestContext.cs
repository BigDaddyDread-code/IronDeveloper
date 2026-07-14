namespace IronDev.Core.Models;

public static class MemoryAuthorityClasses
{
    public const string Binding = "Binding";
    public const string StrongGuidance = "StrongGuidance";
    public const string ObservedFact = "ObservedFact";
    public const string ContextOnly = "ContextOnly";

    public static IReadOnlySet<string> ProjectChatDefaults { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Binding,
            StrongGuidance,
            ObservedFact,
            ContextOnly
        };

    public static IReadOnlySet<string> LegacyPromptEligible { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ObservedFact,
            ContextOnly
        };
}

public sealed record MemoryRetrievalRequestContext
{
    public required int TenantId { get; init; }
    public required int ProjectId { get; init; }
    public required int ActorUserId { get; init; }
    public required string Consumer { get; init; }
    public required IReadOnlySet<string> AllowedAuthorityClasses { get; init; }
    public required DateTime AsOfUtc { get; init; }

    public static MemoryRetrievalRequestContext ForProjectChat(
        int tenantId,
        int projectId,
        int actorUserId,
        string consumer) =>
        new()
        {
            TenantId = tenantId,
            ProjectId = projectId,
            ActorUserId = actorUserId,
            Consumer = consumer,
            AllowedAuthorityClasses = MemoryAuthorityClasses.ProjectChatDefaults,
            AsOfUtc = DateTime.UtcNow
        };
}
