using System.Text.Json;
using System.Text.Json.Serialization;

namespace IronDev.Core.Workbench;

public static class ProjectUnderstandingContract
{
    public const int SchemaVersion1 = 1;
    public const int SchemaVersion = SchemaVersion1;
    public const int MaximumFactValueCharacters = 4_000;
    public const int MaximumEvidenceSummaryCharacters = 1_000;
    public const int MaximumSourceMessagesPerFact = 20;

    public static readonly IReadOnlyList<string> FactKeys =
    [
        "ProductSummary",
        "PrimaryUsers",
        "Goals",
        "Constraints",
        "ApplicationType",
        "DesiredLanguage",
        "DesiredFramework",
        "DesiredDatabase",
        "DesiredTestApproach",
        "TargetPlatform",
        "DeploymentIntent"
    ];

    public static bool IsKnownFactKey(string key) =>
        FactKeys.Contains(key, StringComparer.Ordinal);
}

public static class ProjectUnderstandingFactStates
{
    public const string Unknown = "Unknown";
    public const string Inferred = "Inferred";
    public const string Confirmed = "Confirmed";
    public const string Conflicted = "Conflicted";
}

public static class ProjectUnderstandingAuthorKinds
{
    public const string Actor = "Actor";
    public const string Agent = "Agent";
}

public static class ProjectUnderstandingConflictStates
{
    public const string Open = "Open";
    public const string Resolved = "Resolved";
}

public static class ProjectRenameProposalStates
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Rejected = "Rejected";
    public const string Superseded = "Superseded";
}

public static class ProjectUnderstandingOperationKinds
{
    public const string PutFact = "PutProjectUnderstandingFact";
    public const string AcceptRename = "AcceptProjectRenameProposal";
}

public static class ProjectUnderstandingFactActions
{
    public const string Edit = "Edit";
    public const string Confirm = "Confirm";
    public const string SetLock = "SetLock";
    public const string ResolveConflict = "ResolveConflict";

    public static bool IsKnown(string action) => action is Edit or Confirm or SetLock or ResolveConflict;
}

public sealed record ProjectUnderstandingFact(
    string Key,
    string Value,
    string State,
    bool UserLocked,
    string AuthorKind,
    int? AuthorActorUserId,
    Guid? AuthorAgentRunId,
    IReadOnlyList<long> SourceMessageIds,
    string EvidenceSummary,
    long Revision);

public sealed record ProjectUnderstandingConflict(
    Guid ConflictId,
    string FactKey,
    string CurrentValue,
    string ProposedValue,
    IReadOnlyList<long> SourceMessageIds,
    string EvidenceSummary,
    Guid CreatedByAgentRunId,
    long CreatedAtRevision,
    string Status,
    long? ResolvedAtRevision = null,
    int? ResolvedByActorUserId = null);

public sealed record ProjectUnderstandingDocument(
    int SchemaVersion,
    IReadOnlyList<ProjectUnderstandingFact> Facts,
    IReadOnlyList<ProjectUnderstandingConflict> Conflicts,
    IReadOnlyList<string> OpenQuestions)
{
    public static ProjectUnderstandingDocument Empty { get; } = new(
        ProjectUnderstandingContract.SchemaVersion,
        [],
        [],
        []);
}

public static class ProjectUnderstandingDocumentCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static string EmptyJson { get; } = Serialize(ProjectUnderstandingDocument.Empty);

    public static string Serialize(ProjectUnderstandingDocument document)
    {
        Validate(document);
        var canonical = document with
        {
            Facts = document.Facts.OrderBy(value => value.Key, StringComparer.Ordinal).ToArray(),
            Conflicts = document.Conflicts.OrderBy(value => value.CreatedAtRevision)
                .ThenBy(value => value.ConflictId).ToArray(),
            OpenQuestions = document.OpenQuestions.ToArray()
        };
        return JsonSerializer.Serialize(canonical, JsonOptions);
    }

    public static ProjectUnderstandingDocument Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ProjectUnderstandingDocument.Empty;

        using (var legacyProbe = JsonDocument.Parse(json))
        {
            if (legacyProbe.RootElement.ValueKind != JsonValueKind.Object)
                throw new ProjectUnderstandingValidationException("The stored project understanding must be a JSON object.");
            // PR-01 stored an intentionally schema-less placeholder. It carried no typed fact
            // authority, so it has one explicit compatibility interpretation: an empty document.
            if (!legacyProbe.RootElement.TryGetProperty("schemaVersion", out _))
                return ProjectUnderstandingDocument.Empty;
        }

        var document = JsonSerializer.Deserialize<ProjectUnderstandingDocument>(json, JsonOptions)
            ?? throw new ProjectUnderstandingValidationException("The stored project understanding is empty.");
        Validate(document);
        return document;
    }

    public static void Validate(ProjectUnderstandingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != ProjectUnderstandingContract.SchemaVersion1)
            throw new ProjectUnderstandingValidationException(
                $"Unsupported project-understanding schema version {document.SchemaVersion}.");
        if (document.Facts is null || document.Conflicts is null || document.OpenQuestions is null)
            throw new ProjectUnderstandingValidationException("The project understanding is incomplete.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fact in document.Facts)
        {
            if (!ProjectUnderstandingContract.IsKnownFactKey(fact.Key) || !keys.Add(fact.Key))
                throw new ProjectUnderstandingValidationException("The project understanding contains an unknown or duplicate fact key.");
            if (string.IsNullOrWhiteSpace(fact.Value) || fact.Value.Length > ProjectUnderstandingContract.MaximumFactValueCharacters)
                throw new ProjectUnderstandingValidationException($"Fact '{fact.Key}' has an invalid value.");
            if (fact.State is not (ProjectUnderstandingFactStates.Inferred or ProjectUnderstandingFactStates.Confirmed or ProjectUnderstandingFactStates.Conflicted))
                throw new ProjectUnderstandingValidationException($"Fact '{fact.Key}' has an invalid state.");
            if (fact.AuthorKind is not (ProjectUnderstandingAuthorKinds.Actor or ProjectUnderstandingAuthorKinds.Agent) ||
                (fact.AuthorKind == ProjectUnderstandingAuthorKinds.Actor) == (fact.AuthorActorUserId is null) ||
                (fact.AuthorKind == ProjectUnderstandingAuthorKinds.Agent) == (fact.AuthorAgentRunId is null))
                throw new ProjectUnderstandingValidationException($"Fact '{fact.Key}' has invalid author provenance.");
            if (fact.Revision <= 0 || fact.SourceMessageIds is null ||
                fact.SourceMessageIds.Count > ProjectUnderstandingContract.MaximumSourceMessagesPerFact ||
                fact.SourceMessageIds.Any(value => value <= 0) ||
                fact.SourceMessageIds.Distinct().Count() != fact.SourceMessageIds.Count ||
                string.IsNullOrWhiteSpace(fact.EvidenceSummary) ||
                fact.EvidenceSummary.Length > ProjectUnderstandingContract.MaximumEvidenceSummaryCharacters)
                throw new ProjectUnderstandingValidationException($"Fact '{fact.Key}' has invalid evidence provenance.");
        }

        var conflictIds = new HashSet<Guid>();
        foreach (var conflict in document.Conflicts)
        {
            if (conflict.ConflictId == Guid.Empty || !conflictIds.Add(conflict.ConflictId) ||
                !ProjectUnderstandingContract.IsKnownFactKey(conflict.FactKey) ||
                string.IsNullOrWhiteSpace(conflict.CurrentValue) || string.IsNullOrWhiteSpace(conflict.ProposedValue) ||
                conflict.CurrentValue.Length > ProjectUnderstandingContract.MaximumFactValueCharacters ||
                conflict.ProposedValue.Length > ProjectUnderstandingContract.MaximumFactValueCharacters ||
                conflict.CreatedByAgentRunId == Guid.Empty || conflict.CreatedAtRevision <= 0 ||
                conflict.Status is not (ProjectUnderstandingConflictStates.Open or ProjectUnderstandingConflictStates.Resolved) ||
                conflict.SourceMessageIds is null || conflict.SourceMessageIds.Count == 0 ||
                conflict.SourceMessageIds.Count > ProjectUnderstandingContract.MaximumSourceMessagesPerFact ||
                conflict.SourceMessageIds.Any(value => value <= 0) ||
                conflict.SourceMessageIds.Distinct().Count() != conflict.SourceMessageIds.Count ||
                string.IsNullOrWhiteSpace(conflict.EvidenceSummary) ||
                conflict.EvidenceSummary.Length > ProjectUnderstandingContract.MaximumEvidenceSummaryCharacters)
                throw new ProjectUnderstandingValidationException("The project understanding contains invalid conflict provenance.");
        }
    }
}

public sealed record ProjectUnderstandingFactChange(
    string Key,
    string Value,
    string State,
    IReadOnlyList<long> SourceMessageIds,
    string EvidenceSummary);

public sealed record ProjectUnderstandingPatch(
    IReadOnlyList<ProjectUnderstandingFactChange> FactChanges,
    IReadOnlyList<string>? OpenQuestions = null);

public sealed record WorkbenchProjectRenameProposalOutput(
    string ProposedName,
    IReadOnlyList<long> SourceMessageIds,
    string EvidenceSummary);

public sealed record ProjectRenameProposalSnapshot(
    Guid ProposalId,
    string ProposedName,
    string Status,
    string BasedOnProjectName,
    long BasedOnUnderstandingRevision,
    Guid ProposedByAgentRunId,
    int InitiatingActorUserId,
    IReadOnlyList<long> SourceMessageIds,
    string EvidenceSummary,
    DateTime CreatedAtUtc);

public sealed record ProjectOperationalProjection(
    string ProjectLifecyclePhase,
    string ProjectLifecycleAuthority,
    string ExecutionReadiness,
    string ExecutionReadinessAuthority,
    object? RepositoryBinding);

public sealed record ProjectUnderstandingSnapshot(
    int ProjectId,
    int TenantId,
    string ProjectName,
    long Revision,
    IReadOnlyList<ProjectUnderstandingFact> Facts,
    IReadOnlyList<ProjectUnderstandingConflict> Conflicts,
    IReadOnlyList<string> OpenQuestions,
    ProjectRenameProposalSnapshot? PendingRenameProposal,
    ProjectOperationalProjection OperationalProjections);

public sealed record PutProjectUnderstandingFactCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ClientOperationId,
    long ExpectedUnderstandingRevision,
    string FactKey,
    string Action,
    Guid? ConflictId,
    string? Value,
    bool? UserLocked);

public sealed record PutProjectUnderstandingFactResult(
    ProjectUnderstandingSnapshot Snapshot,
    Guid ClientOperationId,
    bool IsReplay);

public sealed record AcceptProjectRenameProposalCommand(
    int TenantId,
    int ActorUserId,
    int ProjectId,
    long WorkbenchSessionId,
    long LeaseEpoch,
    Guid ProposalId,
    Guid ClientOperationId);

public sealed record AcceptProjectRenameProposalResult(
    ProjectUnderstandingSnapshot Snapshot,
    Guid ClientOperationId,
    bool IsReplay);

public interface IWorkbenchProjectUnderstandingService
{
    Task<ProjectUnderstandingSnapshot> GetAsync(
        int tenantId,
        int actorUserId,
        int projectId,
        CancellationToken cancellationToken = default);

    Task<PutProjectUnderstandingFactResult> PutFactAsync(
        PutProjectUnderstandingFactCommand command,
        CancellationToken cancellationToken = default);

    Task<AcceptProjectRenameProposalResult> AcceptRenameAsync(
        AcceptProjectRenameProposalCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class ProjectUnderstandingValidationException(string message) : Exception(message);

public sealed class ProjectUnderstandingRevisionConflictException(long currentRevision) : Exception(
    "The project understanding changed. Refresh it before saving this fact.")
{
    public const string ErrorCode = "project_understanding_revision_conflict";
    public long CurrentRevision { get; } = currentRevision;
}

public sealed class ProjectRenameProposalNotPendingException : Exception
{
    public const string ErrorCode = "project_rename_proposal_not_pending";

    public ProjectRenameProposalNotPendingException()
        : base("The project rename proposal is no longer pending.")
    {
    }
}

public sealed class ProjectUnderstandingConflictNotOpenException : Exception
{
    public const string ErrorCode = "project_understanding_conflict_not_open";

    public ProjectUnderstandingConflictNotOpenException()
        : base("The selected project-understanding conflict is no longer open for this fact.")
    {
    }
}

public sealed class ProjectRenameProposalStaleException : Exception
{
    public const string ErrorCode = "project_rename_proposal_stale";

    public ProjectRenameProposalStaleException()
        : base("The project name changed after this rename was proposed. Refresh before choosing a name.")
    {
    }
}
