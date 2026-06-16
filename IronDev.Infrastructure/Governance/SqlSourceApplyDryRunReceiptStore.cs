using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlSourceApplyDryRunReceiptStore : ISourceApplyDryRunReceiptStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;

    public SqlSourceApplyDryRunReceiptStore(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task SaveAsync(SourceApplyDryRunReceipt receipt, CancellationToken cancellationToken = default)
    {
        var validation = SourceApplyDryRunReceiptValidation.Validate(receipt);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(receipt));
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "governance.usp_SourceApplyDryRunReceipt_Save",
            new
            {
                receipt.SourceApplyDryRunReceiptId,
                receipt.ProjectId,
                receipt.SourceApplyDryRunRequestId,
                SourceApplyDryRunRequestHash = Normalize(receipt.SourceApplyDryRunRequestHash),
                receipt.DryRunSatisfied,
                DryRunResultHash = Normalize(receipt.DryRunResultHash),
                receipt.SourceApplyRequestId,
                SourceApplyRequestHash = Normalize(receipt.SourceApplyRequestHash),
                receipt.SourceApplyGateEvaluationId,
                SourceApplyGateEvaluationHash = Normalize(receipt.SourceApplyGateEvaluationHash),
                receipt.PatchArtifactId,
                PatchHash = Normalize(receipt.PatchHash),
                ChangeSetHash = Normalize(receipt.ChangeSetHash),
                receipt.RollbackSupportReceiptId,
                RollbackSupportReceiptHash = Normalize(receipt.RollbackSupportReceiptHash),
                SourceBaselineHash = Normalize(receipt.SourceBaselineHash),
                WorkspaceBoundaryHash = Normalize(receipt.WorkspaceBoundaryHash),
                ExpectedBranch = Normalize(receipt.ExpectedBranch),
                ExpectedCleanWorktreeHash = Normalize(receipt.ExpectedCleanWorktreeHash),
                FileResultsJson = JsonSerializer.Serialize(receipt.FileResults.Select(ToDto).ToArray(), JsonOptions),
                receipt.CreatedAtUtc,
                receipt.ExpiresAtUtc,
                SourceApplyDryRunReceiptHash = Normalize(receipt.SourceApplyDryRunReceiptHash),
                EvidenceReferencesJson = JsonSerializer.Serialize(receipt.EvidenceReferences.Select(Normalize).ToArray(), JsonOptions),
                BoundaryMaximsJson = JsonSerializer.Serialize(receipt.BoundaryMaxims.Select(Normalize).ToArray(), JsonOptions),
                BoundaryText = Normalize(receipt.Boundary)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<SourceApplyDryRunReceipt?> GetAsync(Guid projectId, Guid sourceApplyDryRunReceiptId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<SourceApplyDryRunReceiptRow>(new CommandDefinition(
            "governance.usp_SourceApplyDryRunReceipt_Get",
            new { ProjectId = projectId, SourceApplyDryRunReceiptId = sourceApplyDryRunReceiptId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToReceipt();
    }

    public async Task<SourceApplyDryRunReceipt?> GetByReceiptHashAsync(Guid projectId, string sourceApplyDryRunReceiptHash, CancellationToken cancellationToken = default)
    {
        RequireText(sourceApplyDryRunReceiptHash, nameof(sourceApplyDryRunReceiptHash));
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<SourceApplyDryRunReceiptRow>(new CommandDefinition(
            "governance.usp_SourceApplyDryRunReceipt_GetByReceiptHash",
            new { ProjectId = projectId, SourceApplyDryRunReceiptHash = Normalize(sourceApplyDryRunReceiptHash) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToReceipt();
    }

    public async Task<IReadOnlyList<SourceApplyDryRunReceipt>> ListBySourceApplyRequestAsync(Guid projectId, Guid sourceApplyRequestId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyRequest", new { ProjectId = projectId, SourceApplyRequestId = sourceApplyRequestId }, cancellationToken);

    public async Task<IReadOnlyList<SourceApplyDryRunReceipt>> ListBySourceApplyGateEvaluationAsync(Guid projectId, Guid sourceApplyGateEvaluationId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_SourceApplyDryRunReceipt_ListBySourceApplyGateEvaluation", new { ProjectId = projectId, SourceApplyGateEvaluationId = sourceApplyGateEvaluationId }, cancellationToken);

    public async Task<IReadOnlyList<SourceApplyDryRunReceipt>> ListByPatchArtifactAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_SourceApplyDryRunReceipt_ListByPatchArtifact", new { ProjectId = projectId, PatchArtifactId = patchArtifactId }, cancellationToken);

    public async Task<IReadOnlyList<SourceApplyDryRunReceipt>> ListByRollbackSupportReceiptAsync(Guid projectId, Guid rollbackSupportReceiptId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_SourceApplyDryRunReceipt_ListByRollbackSupportReceipt", new { ProjectId = projectId, RollbackSupportReceiptId = rollbackSupportReceiptId }, cancellationToken);

    private async Task<IReadOnlyList<SourceApplyDryRunReceipt>> QueryListAsync(string procedure, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SourceApplyDryRunReceiptRow>(new CommandDefinition(
            procedure,
            parameters,
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToReceipt()).ToArray();
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

    private static FileResultDto ToDto(SourceApplyDryRunReceiptFileResult result) => new(
        Normalize(result.Path),
        NormalizeNullable(result.PreviousPath),
        Normalize(result.OperationKind),
        Normalize(result.PatchArtifactChangeHash),
        Normalize(result.OperationHash),
        NormalizeNullable(result.ExpectedBeforeContentHash),
        NormalizeNullable(result.ExpectedAfterContentHash),
        NormalizeNullable(result.ObservedCurrentContentHash),
        result.PreconditionsSatisfied,
        result.WouldCreate,
        result.WouldModify,
        result.WouldDelete,
        result.WouldRename,
        result.WouldNoop,
        result.IssueCodes.Select(Normalize).ToArray(),
        Normalize(result.FileResultHash));

    private sealed class SourceApplyDryRunReceiptRow
    {
        public Guid SourceApplyDryRunReceiptId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid SourceApplyDryRunRequestId { get; init; }
        public string SourceApplyDryRunRequestHash { get; init; } = string.Empty;
        public bool DryRunSatisfied { get; init; }
        public string DryRunResultHash { get; init; } = string.Empty;
        public Guid SourceApplyRequestId { get; init; }
        public string SourceApplyRequestHash { get; init; } = string.Empty;
        public Guid SourceApplyGateEvaluationId { get; init; }
        public string SourceApplyGateEvaluationHash { get; init; } = string.Empty;
        public Guid PatchArtifactId { get; init; }
        public string PatchHash { get; init; } = string.Empty;
        public string ChangeSetHash { get; init; } = string.Empty;
        public Guid RollbackSupportReceiptId { get; init; }
        public string RollbackSupportReceiptHash { get; init; } = string.Empty;
        public string SourceBaselineHash { get; init; } = string.Empty;
        public string WorkspaceBoundaryHash { get; init; } = string.Empty;
        public string ExpectedBranch { get; init; } = string.Empty;
        public string ExpectedCleanWorktreeHash { get; init; } = string.Empty;
        public string FileResultsJson { get; init; } = "[]";
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? ExpiresAtUtc { get; init; }
        public string SourceApplyDryRunReceiptHash { get; init; } = string.Empty;
        public string EvidenceReferencesJson { get; init; } = "[]";
        public string BoundaryMaximsJson { get; init; } = "[]";
        public string BoundaryText { get; init; } = string.Empty;

        public SourceApplyDryRunReceipt ToReceipt() => new()
        {
            SourceApplyDryRunReceiptId = SourceApplyDryRunReceiptId,
            ProjectId = ProjectId,
            SourceApplyDryRunRequestId = SourceApplyDryRunRequestId,
            SourceApplyDryRunRequestHash = SourceApplyDryRunRequestHash,
            DryRunSatisfied = DryRunSatisfied,
            DryRunResultHash = DryRunResultHash,
            SourceApplyRequestId = SourceApplyRequestId,
            SourceApplyRequestHash = SourceApplyRequestHash,
            SourceApplyGateEvaluationId = SourceApplyGateEvaluationId,
            SourceApplyGateEvaluationHash = SourceApplyGateEvaluationHash,
            PatchArtifactId = PatchArtifactId,
            PatchHash = PatchHash,
            ChangeSetHash = ChangeSetHash,
            RollbackSupportReceiptId = RollbackSupportReceiptId,
            RollbackSupportReceiptHash = RollbackSupportReceiptHash,
            SourceBaselineHash = SourceBaselineHash,
            WorkspaceBoundaryHash = WorkspaceBoundaryHash,
            ExpectedBranch = ExpectedBranch,
            ExpectedCleanWorktreeHash = ExpectedCleanWorktreeHash,
            FileResults = DeserializeFileResults(FileResultsJson),
            CreatedAtUtc = CreatedAtUtc,
            ExpiresAtUtc = ExpiresAtUtc,
            SourceApplyDryRunReceiptHash = SourceApplyDryRunReceiptHash,
            EvidenceReferences = DeserializeList(EvidenceReferencesJson),
            BoundaryMaxims = DeserializeList(BoundaryMaximsJson),
            Boundary = BoundaryText
        };
    }

    private static IReadOnlyList<SourceApplyDryRunReceiptFileResult> DeserializeFileResults(string json) =>
        JsonSerializer.Deserialize<FileResultDto[]>(json, JsonOptions)?.Select(result => new SourceApplyDryRunReceiptFileResult
        {
            Path = result.Path,
            PreviousPath = result.PreviousPath,
            OperationKind = result.OperationKind,
            PatchArtifactChangeHash = result.PatchArtifactChangeHash,
            OperationHash = result.OperationHash,
            ExpectedBeforeContentHash = result.ExpectedBeforeContentHash,
            ExpectedAfterContentHash = result.ExpectedAfterContentHash,
            ObservedCurrentContentHash = result.ObservedCurrentContentHash,
            PreconditionsSatisfied = result.PreconditionsSatisfied,
            WouldCreate = result.WouldCreate,
            WouldModify = result.WouldModify,
            WouldDelete = result.WouldDelete,
            WouldRename = result.WouldRename,
            WouldNoop = result.WouldNoop,
            IssueCodes = result.IssueCodes,
            FileResultHash = result.FileResultHash
        }).ToArray() ?? [];

    private static IReadOnlyList<string> DeserializeList(string json) =>
        JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];

    private sealed record FileResultDto(
        string Path,
        string? PreviousPath,
        string OperationKind,
        string PatchArtifactChangeHash,
        string OperationHash,
        string? ExpectedBeforeContentHash,
        string? ExpectedAfterContentHash,
        string? ObservedCurrentContentHash,
        bool PreconditionsSatisfied,
        bool WouldCreate,
        bool WouldModify,
        bool WouldDelete,
        bool WouldRename,
        bool WouldNoop,
        IReadOnlyList<string> IssueCodes,
        string FileResultHash);
}
