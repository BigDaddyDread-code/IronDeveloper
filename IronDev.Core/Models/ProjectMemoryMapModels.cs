namespace IronDev.Core.Models;

public sealed record ProjectMemoryMap(
    int ProjectId,
    string ProjectName,
    DateTimeOffset GeneratedUtc,
    ProjectMemoryMapCounts Counts,
    IReadOnlyList<ProjectMemoryMapItem> Items,
    ProjectMemorySourceGraph? SourceGraph = null)
{
    public IReadOnlyList<ProjectMemoryMapItem> Entries => Items;
    public ProjectMemorySourceGraph Graph => SourceGraph ?? ProjectMemorySourceGraph.Empty;
}

public sealed record ProjectMemoryMapCounts(
    int Total,
    int Decisions,
    int Tickets,
    int Documents,
    int Rules,
    int Current,
    int Stale);

public sealed record ProjectMemoryMapItem(
    string SourceId,
    string SourceType,
    string Title,
    string? Summary,
    string AuthorityLevel,
    bool IsCurrent,
    string? StalenessReason,
    string? SupersededBySourceId,
    string SourceStatus,
    string UsedFor,
    DateTimeOffset? CreatedUtc = null,
    DateTimeOffset? UpdatedUtc = null,
    IReadOnlyList<ProjectMemoryLink>? Links = null);

public sealed record ProjectMemoryLink(
    string LinkType,
    string TargetSourceId,
    string? TargetSourceType = null);

public static class ProjectMemoryLinkTypes
{
    public const string DerivedFrom = "DerivedFrom";
    public const string GeneratedFrom = "GeneratedFrom";
    public const string Supersedes = "Supersedes";
    public const string SupersededBy = "SupersededBy";
    public const string SourceChatMessage = "SourceChatMessage";
    public const string SourceDocumentVersion = "SourceDocumentVersion";
    public const string Implements = "Implements";
    public const string Tests = "Tests";
    public const string Explains = "Explains";
    public const string ConflictsWith = "ConflictsWith";
    public const string ParentArtefact = "ParentArtefact";
    public const string SourceEntity = "SourceEntity";
}

public sealed record ProjectMemorySourceGraph(
    IReadOnlyList<ProjectMemorySourceNode> Nodes,
    IReadOnlyList<ProjectMemorySourceEdge> Edges)
{
    public static ProjectMemorySourceGraph Empty { get; } = new([], []);
}

public sealed record ProjectMemorySourceNode(
    string SourceId,
    string SourceType,
    string Title,
    string AuthorityLevel,
    bool IsCurrent);

public sealed record ProjectMemorySourceEdge(
    string FromSourceId,
    string ToSourceId,
    string LinkType);
