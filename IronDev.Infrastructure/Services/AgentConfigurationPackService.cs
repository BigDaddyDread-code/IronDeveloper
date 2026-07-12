using IronDev.Core.Agents;
using IronDev.Core.AiConnections;

namespace IronDev.Infrastructure.Services;

public sealed class AgentConfigurationPackService : IAgentConfigurationPackService
{
    private static readonly SkeletonAgentRole[] PortableRoles =
    [
        SkeletonAgentRole.Analyst,
        SkeletonAgentRole.Builder,
        SkeletonAgentRole.Tester,
        SkeletonAgentRole.Critic
    ];

    private readonly ISkeletonAgentProfileService _profiles;
    private readonly IAiConnectionCatalogService _connections;

    public AgentConfigurationPackService(
        ISkeletonAgentProfileService profiles,
        IAiConnectionCatalogService connections)
    {
        _profiles = profiles;
        _connections = connections;
    }

    public async Task<AgentConfigurationPack> ExportAsync(
        int tenantId,
        int userId,
        SkeletonAgentProfileScope scope,
        CancellationToken cancellationToken = default)
    {
        var effective = await _profiles.ListEffectiveAsync(tenantId, scope.ProjectId, cancellationToken).ConfigureAwait(false);
        var connections = await _connections.ListAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
        var entries = new List<AgentConfigurationPackEntry>();
        foreach (var role in PortableRoles)
        {
            var history = await _profiles.ListHistoryAsync(role, scope, cancellationToken).ConfigureAwait(false);
            var published = history.OrderByDescending(item => item.Version).FirstOrDefault();
            if (published is null)
                continue;
            var current = effective.Single(item => item.Role == role);
            var connection = connections.FirstOrDefault(item =>
                string.Equals(item.Id, published.Values.AiConnectionId, StringComparison.OrdinalIgnoreCase));
            entries.Add(new AgentConfigurationPackEntry
            {
                Role = role,
                Values = published.Values,
                LogicalConnectionName = connection?.DisplayName ?? published.Values.AiConnectionId,
                BuiltInDefaultVersion = current.BuiltInDefaultVersion,
                SourcePublishedVersion = published.Version
            });
        }

        return new AgentConfigurationPack
        {
            PackId = Guid.NewGuid().ToString("N"),
            ExportedAtUtc = DateTimeOffset.UtcNow,
            SourceScope = scope.Layer,
            SourceTenantId = tenantId,
            SourceProjectId = scope.ProjectId,
            Profiles = entries
        };
    }

    public async Task<AgentConfigurationPackPreview> PreviewAsync(
        int tenantId,
        int userId,
        SkeletonAgentProfileScope scope,
        AgentConfigurationPack pack,
        CancellationToken cancellationToken = default)
    {
        var formatFailure = ValidateFormat(pack, scope);
        if (formatFailure is not null)
            return formatFailure;

        var connections = await _connections.ListAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
        var differences = new List<AgentConfigurationPackDifference>();
        var revisions = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<SkeletonAgentRole>();
        foreach (var entry in pack.Profiles)
        {
            if (!PortableRoles.Contains(entry.Role) || !seen.Add(entry.Role))
                return Refused(scope, "RoleInvalid", "Configuration packs may contain each model-driven role once; the deterministic Orchestrator is not portable.", pack);

            var connection = ResolveConnection(connections, entry);
            if (connection is null)
                return Refused(scope, "AiConnectionUnavailable", $"The logical AI connection '{entry.LogicalConnectionName}' for {entry.Role} is not enabled and available in the target tenant.", pack);

            var values = entry.Values with
            {
                AiConnectionId = connection.Id,
                Provider = connection.ProviderKind
            };
            var issues = _profiles.ValidateUpdate(values);
            if (issues.Count > 0)
                return Refused(scope, "ValidationFailed", $"{entry.Role}: {issues[0].Message}", pack);

            var draft = await _profiles.GetDraftAsync(entry.Role, scope, cancellationToken).ConfigureAwait(false);
            revisions[entry.Role.ToString()] = draft.Revision;
            AddDifferences(differences, entry.Role, draft.Values, values);
        }

        return new AgentConfigurationPackPreview
        {
            Succeeded = true,
            TargetScope = scope.Layer,
            TargetProjectId = scope.ProjectId,
            Differences = differences,
            ExpectedRevisions = revisions,
            DraftOnly = true,
            SourceProvenance = Provenance(pack)
        };
    }

    public async Task<AgentConfigurationPackImportOutcome> ImportAsync(
        int tenantId,
        int userId,
        SkeletonAgentProfileScope scope,
        AgentConfigurationPackImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var preview = await PreviewAsync(tenantId, userId, scope, request.Pack, cancellationToken).ConfigureAwait(false);
        if (!preview.Succeeded)
            return RefusedImport(preview);

        foreach (var expected in preview.ExpectedRevisions)
        {
            if (!request.ExpectedRevisions.TryGetValue(expected.Key, out var supplied) || supplied != expected.Value)
                return RefusedImport(preview with
                {
                    Succeeded = false,
                    Code = "StalePreview",
                    FailureReason = $"The {expected.Key} draft changed after preview. Preview the pack again before creating drafts."
                });
        }

        var connections = await _connections.ListAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
        var created = new List<SkeletonAgentProfileDraft>();
        foreach (var entry in request.Pack.Profiles)
        {
            var connection = ResolveConnection(connections, entry)!;
            var values = entry.Values with { AiConnectionId = connection.Id, Provider = connection.ProviderKind };
            var result = await _profiles.SaveDraftAsync(entry.Role, scope, new SkeletonAgentProfileDraftWriteRequest
            {
                ExpectedRevision = request.ExpectedRevisions[entry.Role.ToString()],
                AiConnectionId = values.AiConnectionId,
                Provider = values.Provider,
                Model = values.Model,
                TimeoutSeconds = values.TimeoutSeconds,
                Skill = values.Skill,
                Personality = values.Personality
            }, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded || result.Draft is null)
                return RefusedImport(preview with
                {
                    Succeeded = false,
                    Code = result.Code,
                    FailureReason = result.FailureReason
                }, created);
            created.Add(result.Draft);
        }

        return new AgentConfigurationPackImportOutcome
        {
            Succeeded = true,
            CreatedDrafts = created,
            Published = false,
            Preview = preview
        };
    }

    private static AgentConfigurationPackPreview? ValidateFormat(AgentConfigurationPack pack, SkeletonAgentProfileScope scope)
    {
        if (!string.Equals(pack.Format, AgentConfigurationPack.CurrentFormat, StringComparison.Ordinal) ||
            pack.FormatVersion != AgentConfigurationPack.CurrentFormatVersion)
        {
            return Refused(scope, "FormatUnsupported", $"Expected {AgentConfigurationPack.CurrentFormat} version {AgentConfigurationPack.CurrentFormatVersion}.", pack);
        }
        return null;
    }

    private static AiConnectionMetadata? ResolveConnection(
        IReadOnlyList<AiConnectionMetadata> connections,
        AgentConfigurationPackEntry entry) =>
        connections.FirstOrDefault(item =>
            (string.Equals(item.DisplayName, entry.LogicalConnectionName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.Id, entry.LogicalConnectionName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.Id, entry.Values.AiConnectionId, StringComparison.OrdinalIgnoreCase)) &&
            item.Enabled && item.TenantAvailable && item.ProjectAvailable);

    private static void AddDifferences(
        ICollection<AgentConfigurationPackDifference> output,
        SkeletonAgentRole role,
        SkeletonAgentProfileUpdate current,
        SkeletonAgentProfileUpdate imported)
    {
        Add("aiConnectionId", current.AiConnectionId, imported.AiConnectionId);
        Add("provider", current.Provider, imported.Provider);
        Add("model", current.Model, imported.Model);
        Add("timeoutSeconds", current.TimeoutSeconds.ToString(), imported.TimeoutSeconds.ToString());
        Add("skill", current.Skill, imported.Skill);
        Add("personality", current.Personality, imported.Personality);

        void Add(string field, string currentValue, string importedValue) => output.Add(new AgentConfigurationPackDifference
        {
            Role = role,
            Field = field,
            CurrentValue = currentValue,
            ImportedValue = importedValue,
            Changed = !string.Equals(currentValue, importedValue, StringComparison.Ordinal)
        });
    }

    private static string Provenance(AgentConfigurationPack pack) =>
        $"Pack {pack.PackId} exported {pack.ExportedAtUtc:O} from {pack.SourceScope} scope" +
        (pack.SourceProjectId is > 0 ? $" project {pack.SourceProjectId}" : string.Empty) + ".";

    private static AgentConfigurationPackPreview Refused(
        SkeletonAgentProfileScope scope,
        string code,
        string reason,
        AgentConfigurationPack pack) => new()
        {
            Succeeded = false,
            Code = code,
            FailureReason = reason,
            TargetScope = scope.Layer,
            TargetProjectId = scope.ProjectId,
            DraftOnly = true,
            SourceProvenance = Provenance(pack)
        };

    private static AgentConfigurationPackImportOutcome RefusedImport(
        AgentConfigurationPackPreview preview,
        IReadOnlyList<SkeletonAgentProfileDraft>? created = null) => new()
        {
            Succeeded = false,
            Code = preview.Code,
            FailureReason = preview.FailureReason,
            CreatedDrafts = created ?? [],
            Published = false,
            Preview = preview
        };
}
