namespace IronDev.Core.Agents;

public sealed record AgentConfigurationPack
{
    public const string CurrentFormat = "irondev-agent-configuration-pack";
    public const int CurrentFormatVersion = 1;
    public const string BoundaryText =
        "A configuration pack contains non-secret agent profile proposals only. Import creates drafts and never publishes configuration or grants authority.";

    public string Format { get; init; } = CurrentFormat;
    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public required string PackId { get; init; }
    public required DateTimeOffset ExportedAtUtc { get; init; }
    public required string SourceScope { get; init; }
    public required int SourceTenantId { get; init; }
    public int? SourceProjectId { get; init; }
    public IReadOnlyList<AgentConfigurationPackEntry> Profiles { get; init; } = [];
    public string Boundary { get; init; } = BoundaryText;
}

public sealed record AgentConfigurationPackEntry
{
    public required SkeletonAgentRole Role { get; init; }
    public required SkeletonAgentProfileUpdate Values { get; init; }
    public required string LogicalConnectionName { get; init; }
    public required string BuiltInDefaultVersion { get; init; }
    public required long SourcePublishedVersion { get; init; }
}

public sealed record AgentConfigurationPackPreviewRequest
{
    public required AgentConfigurationPack Pack { get; init; }
}

public sealed record AgentConfigurationPackImportRequest
{
    public required AgentConfigurationPack Pack { get; init; }
    public IReadOnlyDictionary<string, long> ExpectedRevisions { get; init; } = new Dictionary<string, long>();
}

public sealed record AgentConfigurationPackDifference
{
    public required SkeletonAgentRole Role { get; init; }
    public required string Field { get; init; }
    public required string CurrentValue { get; init; }
    public required string ImportedValue { get; init; }
    public required bool Changed { get; init; }
}

public sealed record AgentConfigurationPackPreview
{
    public required bool Succeeded { get; init; }
    public string Code { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public required string TargetScope { get; init; }
    public int? TargetProjectId { get; init; }
    public IReadOnlyList<AgentConfigurationPackDifference> Differences { get; init; } = [];
    public IReadOnlyDictionary<string, long> ExpectedRevisions { get; init; } = new Dictionary<string, long>();
    public required bool DraftOnly { get; init; }
    public string SourceProvenance { get; init; } = string.Empty;
    public string Boundary { get; init; } = AgentConfigurationPack.BoundaryText;
}

public sealed record AgentConfigurationPackImportOutcome
{
    public required bool Succeeded { get; init; }
    public string Code { get; init; } = string.Empty;
    public string FailureReason { get; init; } = string.Empty;
    public IReadOnlyList<SkeletonAgentProfileDraft> CreatedDrafts { get; init; } = [];
    public required bool Published { get; init; }
    public required AgentConfigurationPackPreview Preview { get; init; }
    public string Boundary { get; init; } = AgentConfigurationPack.BoundaryText;
}

public interface IAgentConfigurationPackService
{
    Task<AgentConfigurationPack> ExportAsync(
        int tenantId,
        int userId,
        SkeletonAgentProfileScope scope,
        CancellationToken cancellationToken = default);

    Task<AgentConfigurationPackPreview> PreviewAsync(
        int tenantId,
        int userId,
        SkeletonAgentProfileScope scope,
        AgentConfigurationPack pack,
        CancellationToken cancellationToken = default);

    Task<AgentConfigurationPackImportOutcome> ImportAsync(
        int tenantId,
        int userId,
        SkeletonAgentProfileScope scope,
        AgentConfigurationPackImportRequest request,
        CancellationToken cancellationToken = default);
}
