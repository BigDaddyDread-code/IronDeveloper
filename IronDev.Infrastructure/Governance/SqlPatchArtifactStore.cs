using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlPatchArtifactStore : IPatchArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;

    public SqlPatchArtifactStore(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task SaveAsync(PatchArtifact patchArtifact, CancellationToken cancellationToken = default)
    {
        var validation = PatchArtifactValidation.Validate(patchArtifact);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(patchArtifact));
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "governance.usp_PatchArtifact_Save",
            new
            {
                patchArtifact.PatchArtifactId,
                patchArtifact.ProjectId,
                PatchArtifactKind = Normalize(patchArtifact.PatchArtifactKind),
                patchArtifact.ControlledDryRunRequestId,
                patchArtifact.DryRunExecutionAuditId,
                DryRunAuditHash = Normalize(patchArtifact.DryRunAuditHash),
                DryRunReceiptHash = Normalize(patchArtifact.DryRunReceiptHash),
                patchArtifact.PolicySatisfactionId,
                PolicySatisfactionHash = Normalize(patchArtifact.PolicySatisfactionHash),
                SubjectKind = Normalize(patchArtifact.SubjectKind),
                SubjectId = Normalize(patchArtifact.SubjectId),
                SubjectHash = Normalize(patchArtifact.SubjectHash),
                SourceSnapshotReference = Normalize(patchArtifact.SourceSnapshotReference),
                SourceBaselineHash = Normalize(patchArtifact.SourceBaselineHash),
                WorkspaceBoundaryHash = Normalize(patchArtifact.WorkspaceBoundaryHash),
                ValidationPlanId = Normalize(patchArtifact.ValidationPlanId),
                ValidationPlanHash = Normalize(patchArtifact.ValidationPlanHash),
                PatchHash = Normalize(patchArtifact.PatchHash),
                ChangeSetHash = Normalize(patchArtifact.ChangeSetHash),
                FileChangesJson = JsonSerializer.Serialize(patchArtifact.FileChanges.Select(change => new FileChangeDto(
                    Normalize(change.Path),
                    NormalizeNullable(change.PreviousPath),
                    Normalize(change.ChangeKind),
                    NormalizeNullable(change.BeforeContentHash),
                    NormalizeNullable(change.AfterContentHash),
                    Normalize(change.DiffHash),
                    Normalize(change.NormalizedDiff),
                    change.IsBinary)), JsonOptions),
                EvidenceReferencesJson = JsonSerializer.Serialize(patchArtifact.EvidenceReferences.Select(Normalize).ToArray(), JsonOptions),
                BoundaryMaximsJson = JsonSerializer.Serialize(patchArtifact.BoundaryMaxims.Select(Normalize).ToArray(), JsonOptions),
                BoundaryText = Normalize(patchArtifact.Boundary),
                patchArtifact.CreatedAtUtc,
                patchArtifact.ExpiresAtUtc
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<PatchArtifact?> GetAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<PatchArtifactRow>(new CommandDefinition(
            "governance.usp_PatchArtifact_Get",
            new { ProjectId = projectId, PatchArtifactId = patchArtifactId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToPatchArtifact();
    }

    public async Task<IReadOnlyList<PatchArtifact>> ListByDryRunReceiptHashAsync(Guid projectId, string dryRunReceiptHash, CancellationToken cancellationToken = default)
    {
        RequireText(dryRunReceiptHash, nameof(dryRunReceiptHash));
        return await QueryListAsync("governance.usp_PatchArtifact_ListByDryRunReceiptHash", new { ProjectId = projectId, DryRunReceiptHash = Normalize(dryRunReceiptHash) }, cancellationToken);
    }

    public async Task<IReadOnlyList<PatchArtifact>> ListByDryRunAuditHashAsync(Guid projectId, string dryRunAuditHash, CancellationToken cancellationToken = default)
    {
        RequireText(dryRunAuditHash, nameof(dryRunAuditHash));
        return await QueryListAsync("governance.usp_PatchArtifact_ListByDryRunAuditHash", new { ProjectId = projectId, DryRunAuditHash = Normalize(dryRunAuditHash) }, cancellationToken);
    }

    public async Task<IReadOnlyList<PatchArtifact>> ListByControlledDryRunRequestAsync(Guid projectId, Guid controlledDryRunRequestId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_PatchArtifact_ListByControlledDryRunRequest", new { ProjectId = projectId, ControlledDryRunRequestId = controlledDryRunRequestId }, cancellationToken);

    public async Task<IReadOnlyList<PatchArtifact>> ListBySubjectAsync(Guid projectId, string subjectKind, string subjectId, CancellationToken cancellationToken = default)
    {
        RequireText(subjectKind, nameof(subjectKind));
        RequireText(subjectId, nameof(subjectId));
        return await QueryListAsync("governance.usp_PatchArtifact_ListBySubject", new { ProjectId = projectId, SubjectKind = Normalize(subjectKind), SubjectId = Normalize(subjectId) }, cancellationToken);
    }

    public async Task<IReadOnlyList<PatchArtifact>> ListByPatchHashAsync(Guid projectId, string patchHash, CancellationToken cancellationToken = default)
    {
        RequireText(patchHash, nameof(patchHash));
        return await QueryListAsync("governance.usp_PatchArtifact_ListByPatchHash", new { ProjectId = projectId, PatchHash = Normalize(patchHash) }, cancellationToken);
    }

    public async Task<IReadOnlyList<PatchArtifact>> ListBySourceBaselineHashAsync(Guid projectId, string sourceBaselineHash, CancellationToken cancellationToken = default)
    {
        RequireText(sourceBaselineHash, nameof(sourceBaselineHash));
        return await QueryListAsync("governance.usp_PatchArtifact_ListBySourceBaselineHash", new { ProjectId = projectId, SourceBaselineHash = Normalize(sourceBaselineHash) }, cancellationToken);
    }

    private async Task<IReadOnlyList<PatchArtifact>> QueryListAsync(string procedure, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<PatchArtifactRow>(new CommandDefinition(
            procedure,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToPatchArtifact()).ToArray();
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }

    private static string Normalize(string value) => value.Trim();

    private static string? NormalizeNullable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class PatchArtifactRow
    {
        public Guid PatchArtifactId { get; init; }
        public Guid ProjectId { get; init; }
        public string PatchArtifactKind { get; init; } = string.Empty;
        public Guid ControlledDryRunRequestId { get; init; }
        public Guid DryRunExecutionAuditId { get; init; }
        public string DryRunAuditHash { get; init; } = string.Empty;
        public string DryRunReceiptHash { get; init; } = string.Empty;
        public Guid PolicySatisfactionId { get; init; }
        public string PolicySatisfactionHash { get; init; } = string.Empty;
        public string SubjectKind { get; init; } = string.Empty;
        public string SubjectId { get; init; } = string.Empty;
        public string SubjectHash { get; init; } = string.Empty;
        public string SourceSnapshotReference { get; init; } = string.Empty;
        public string SourceBaselineHash { get; init; } = string.Empty;
        public string WorkspaceBoundaryHash { get; init; } = string.Empty;
        public string ValidationPlanId { get; init; } = string.Empty;
        public string ValidationPlanHash { get; init; } = string.Empty;
        public string PatchHash { get; init; } = string.Empty;
        public string ChangeSetHash { get; init; } = string.Empty;
        public string FileChangesJson { get; init; } = "[]";
        public string EvidenceReferencesJson { get; init; } = "[]";
        public string BoundaryMaximsJson { get; init; } = "[]";
        public string BoundaryText { get; init; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? ExpiresAtUtc { get; init; }

        public PatchArtifact ToPatchArtifact() => new()
        {
            PatchArtifactId = PatchArtifactId,
            ProjectId = ProjectId,
            PatchArtifactKind = PatchArtifactKind,
            ControlledDryRunRequestId = ControlledDryRunRequestId,
            DryRunExecutionAuditId = DryRunExecutionAuditId,
            DryRunAuditHash = DryRunAuditHash,
            DryRunReceiptHash = DryRunReceiptHash,
            PolicySatisfactionId = PolicySatisfactionId,
            PolicySatisfactionHash = PolicySatisfactionHash,
            SubjectKind = SubjectKind,
            SubjectId = SubjectId,
            SubjectHash = SubjectHash,
            SourceSnapshotReference = SourceSnapshotReference,
            SourceBaselineHash = SourceBaselineHash,
            WorkspaceBoundaryHash = WorkspaceBoundaryHash,
            ValidationPlanId = ValidationPlanId,
            ValidationPlanHash = ValidationPlanHash,
            PatchHash = PatchHash,
            ChangeSetHash = ChangeSetHash,
            FileChanges = DeserializeFileChanges(FileChangesJson),
            CreatedAtUtc = CreatedAtUtc,
            ExpiresAtUtc = ExpiresAtUtc,
            EvidenceReferences = DeserializeList(EvidenceReferencesJson),
            BoundaryMaxims = DeserializeList(BoundaryMaximsJson),
            Boundary = BoundaryText
        };
    }

    private static IReadOnlyList<PatchArtifactFileChange> DeserializeFileChanges(string json) =>
        JsonSerializer.Deserialize<FileChangeDto[]>(json, JsonOptions)?.Select(change => new PatchArtifactFileChange
        {
            Path = change.Path,
            PreviousPath = change.PreviousPath,
            ChangeKind = change.ChangeKind,
            BeforeContentHash = change.BeforeContentHash,
            AfterContentHash = change.AfterContentHash,
            DiffHash = change.DiffHash,
            NormalizedDiff = change.NormalizedDiff,
            IsBinary = change.IsBinary
        }).ToArray() ?? [];

    private static IReadOnlyList<string> DeserializeList(string json) =>
        JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];

    private sealed record FileChangeDto(
        string Path,
        string? PreviousPath,
        string ChangeKind,
        string? BeforeContentHash,
        string? AfterContentHash,
        string DiffHash,
        string NormalizedDiff,
        bool IsBinary);
}
