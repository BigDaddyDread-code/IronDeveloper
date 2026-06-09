using System.Security.Cryptography;
using System.Text;
using IronDev.Core.Agents.Skills;

namespace IronDev.Infrastructure.Services.Agents.Skills;

public sealed class AgentSkillMemoryContextBinder : IAgentSkillMemoryContextBinder
{
    private static readonly ISet<string> AllowedSourceKinds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            AgentSkillMemorySourceKinds.ProjectDocument,
            AgentSkillMemorySourceKinds.Decision,
            AgentSkillMemorySourceKinds.Ticket,
            AgentSkillMemorySourceKinds.RunEvidence,
            AgentSkillMemorySourceKinds.WorkspaceEvidence,
            AgentSkillMemorySourceKinds.CodeSummary,
            AgentSkillMemorySourceKinds.ManualNote,
            AgentSkillMemorySourceKinds.Unknown
        };

    private readonly IAgentSkillMemorySearchService? _memorySearchService;
    private readonly Func<DateTimeOffset> _utcNow;

    public AgentSkillMemoryContextBinder(
        IAgentSkillMemorySearchService? memorySearchService = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        _memorySearchService = memorySearchService;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<AgentSkillMemoryContext> BindAsync(
        AgentSkillMemoryContextBindingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SkillId);

        var maxItems = NormalizeMaxItems(request.MaxItems);
        var query = BuildQuery(request);
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Purpose))
            return Unavailable(request, query, "Memory context purpose was empty; memory binding was skipped.");

        if (_memorySearchService is null)
            return Unavailable(request, query, "Memory search backend was not configured; memory context is unavailable.");

        AgentSkillMemorySearchResult result;
        try
        {
            result = await _memorySearchService.SearchAsync(
                new AgentSkillMemorySearchRequest
                {
                    ProjectId = request.ProjectId,
                    SkillId = request.SkillId,
                    Query = query,
                    MaxItems = maxItems
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            return Unavailable(request, query, $"Memory search failed; memory context is unavailable. {ex.Message}");
        }

        warnings.AddRange(result.Warnings);
        if (!result.Available)
        {
            warnings.Add("Memory search backend reported unavailable; memory context is unavailable.");
            return BuildContext(request, query, false, [], warnings);
        }

        var now = _utcNow();
        var items = result.Items
            .OrderByDescending(item => item.Score ?? double.MinValue)
            .ThenByDescending(item => item.UpdatedUtc ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.ItemId, StringComparer.Ordinal)
            .Take(maxItems)
            .Select(item => MapItem(item, now))
            .ToArray();

        warnings.AddRange(items.SelectMany(item => item.Warnings));
        return BuildContext(request, query, true, items, warnings);
    }

    private static int NormalizeMaxItems(int maxItems)
    {
        if (maxItems <= 0)
            return 5;

        return Math.Min(maxItems, 10);
    }

    private AgentSkillMemoryContext Unavailable(
        AgentSkillMemoryContextBindingRequest request,
        string query,
        string warning) =>
        BuildContext(request, query, false, [], [warning]);

    private AgentSkillMemoryContext BuildContext(
        AgentSkillMemoryContextBindingRequest request,
        string query,
        bool available,
        IReadOnlyList<AgentSkillMemoryContextItem> items,
        IEnumerable<string> warnings)
    {
        var evidencePaths = items
            .SelectMany(item => item.EvidencePaths)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AgentSkillMemoryContext
        {
            MemoryContextAvailable = available,
            BindingId = BuildBindingId(request, query),
            ProjectId = request.ProjectId,
            SkillId = request.SkillId,
            Query = query,
            Items = items,
            EvidencePaths = evidencePaths,
            Warnings = warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Blockers = [],
            CanApprove = false,
            CanExecute = false,
            CanMutateSource = false,
            CanMutateWorkspace = false,
            CanWriteMemory = false,
            CanCreateTicket = false,
            CanUseExternalSystem = false
        };
    }

    private static AgentSkillMemoryContextItem MapItem(
        AgentSkillMemorySearchItem item,
        DateTimeOffset now)
    {
        var warnings = item.Warnings.ToList();
        var sourceKind = NormalizeSourceKind(item.SourceKind, warnings);
        var missingTimestamp = item.CreatedUtc is null && item.UpdatedUtc is null;
        var newestTimestamp = item.UpdatedUtc ?? item.CreatedUtc;
        var stale = missingTimestamp || newestTimestamp < now.AddDays(-90);

        if (missingTimestamp)
            warnings.Add($"Memory item '{item.ItemId}' has no timestamp and was marked stale.");
        else if (stale)
            warnings.Add($"Memory item '{item.ItemId}' is older than 90 days and was marked stale.");

        return new AgentSkillMemoryContextItem
        {
            ItemId = item.ItemId,
            SourceKind = sourceKind,
            SourceId = item.SourceId,
            SourcePath = item.SourcePath,
            Title = item.Title,
            Summary = item.Summary,
            Score = item.Score,
            CreatedUtc = item.CreatedUtc,
            UpdatedUtc = item.UpdatedUtc,
            IsStale = stale,
            IsAuthoritative = item.IsAuthoritative &&
                              !string.Equals(sourceKind, AgentSkillMemorySourceKinds.Unknown, StringComparison.Ordinal),
            Tags = item.Tags,
            EvidencePaths = item.EvidencePaths,
            Warnings = warnings
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static string NormalizeSourceKind(string sourceKind, List<string> warnings)
    {
        if (AllowedSourceKinds.Contains(sourceKind))
            return sourceKind;

        warnings.Add($"Memory item source kind '{sourceKind}' is unknown and was marked non-authoritative.");
        return AgentSkillMemorySourceKinds.Unknown;
    }

    private static string BuildQuery(AgentSkillMemoryContextBindingRequest request)
    {
        var parts = new List<string>
        {
            $"projectId={request.ProjectId}",
            $"skillId={request.SkillId}",
            $"purpose={request.Purpose}"
        };

        parts.AddRange(request.Parameters
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}"));

        return string.Join(" | ", parts);
    }

    private static string BuildBindingId(
        AgentSkillMemoryContextBindingRequest request,
        string query)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)))[..12].ToLowerInvariant();
        return Sanitize($"skill-memory-context-{request.ProjectId}-{request.SkillId}-{hash}");
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasDash = false;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            var next = char.IsLetterOrDigit(character) ? character : '-';
            if (next == '-' && previousWasDash)
                continue;

            builder.Append(next);
            previousWasDash = next == '-';
        }

        return builder.ToString().Trim('-');
    }
}
