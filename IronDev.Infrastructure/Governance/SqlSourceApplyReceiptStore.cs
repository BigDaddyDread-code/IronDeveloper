using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlSourceApplyReceiptStore : ISourceApplyReceiptStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;

    public SqlSourceApplyReceiptStore(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task SaveAsync(SourceApplyReceipt receipt, CancellationToken cancellationToken = default)
    {
        var validation = SourceApplyReceiptValidation.Validate(receipt);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(receipt));
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "governance.usp_SourceApplyReceipt_Save",
            new
            {
                receipt.SourceApplyReceiptId,
                receipt.ProjectId,
                receipt.ControlledSourceApplyRequestId,
                receipt.SourceApplyRequestId,
                SourceApplyRequestHash = Normalize(receipt.SourceApplyRequestHash),
                receipt.SourceApplyDryRunReceiptId,
                SourceApplyDryRunReceiptHash = Normalize(receipt.SourceApplyDryRunReceiptHash),
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
                ObservedBranch = Normalize(receipt.ObservedBranch),
                ObservedCleanWorktreeHashBeforeApply = Normalize(receipt.ObservedCleanWorktreeHashBeforeApply),
                ObservedCleanWorktreeHashAfterApply = Normalize(receipt.ObservedCleanWorktreeHashAfterApply),
                receipt.MutationOccurred,
                receipt.ApplySucceeded,
                receipt.PartialApplyOccurred,
                FileResultsJson = JsonSerializer.Serialize(receipt.FileResults.Select(ToDto).ToArray(), JsonOptions),
                IssueCodesJson = JsonSerializer.Serialize(receipt.IssueCodes.Select(Normalize).ToArray(), JsonOptions),
                receipt.AppliedAtUtc,
                SourceApplyReceiptHash = Normalize(receipt.SourceApplyReceiptHash),
                EvidenceReferencesJson = JsonSerializer.Serialize(receipt.EvidenceReferences.Select(Normalize).ToArray(), JsonOptions),
                BoundaryMaximsJson = JsonSerializer.Serialize(receipt.BoundaryMaxims.Select(Normalize).ToArray(), JsonOptions),
                BoundaryText = Normalize(receipt.Boundary)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<SourceApplyReceipt?> GetAsync(Guid projectId, Guid sourceApplyReceiptId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<SourceApplyReceiptRow>(new CommandDefinition(
            "governance.usp_SourceApplyReceipt_Get",
            new { ProjectId = projectId, SourceApplyReceiptId = sourceApplyReceiptId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToReceipt();
    }

    public async Task<SourceApplyReceipt?> GetByReceiptHashAsync(Guid projectId, string sourceApplyReceiptHash, CancellationToken cancellationToken = default)
    {
        RequireText(sourceApplyReceiptHash, nameof(sourceApplyReceiptHash));
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<SourceApplyReceiptRow>(new CommandDefinition(
            "governance.usp_SourceApplyReceipt_GetByReceiptHash",
            new { ProjectId = projectId, SourceApplyReceiptHash = Normalize(sourceApplyReceiptHash) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToReceipt();
    }

    public async Task<IReadOnlyList<SourceApplyReceipt>> ListBySourceApplyRequestAsync(Guid projectId, Guid sourceApplyRequestId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_SourceApplyReceipt_ListBySourceApplyRequest", new { ProjectId = projectId, SourceApplyRequestId = sourceApplyRequestId }, cancellationToken);

    public async Task<IReadOnlyList<SourceApplyReceipt>> ListBySourceApplyDryRunReceiptAsync(Guid projectId, Guid sourceApplyDryRunReceiptId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_SourceApplyReceipt_ListBySourceApplyDryRunReceipt", new { ProjectId = projectId, SourceApplyDryRunReceiptId = sourceApplyDryRunReceiptId }, cancellationToken);

    public async Task<IReadOnlyList<SourceApplyReceipt>> ListByPatchArtifactAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_SourceApplyReceipt_ListByPatchArtifact", new { ProjectId = projectId, PatchArtifactId = patchArtifactId }, cancellationToken);

    public async Task<IReadOnlyList<SourceApplyReceipt>> ListByRollbackSupportReceiptAsync(Guid projectId, Guid rollbackSupportReceiptId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_SourceApplyReceipt_ListByRollbackSupportReceipt", new { ProjectId = projectId, RollbackSupportReceiptId = rollbackSupportReceiptId }, cancellationToken);

    private async Task<IReadOnlyList<SourceApplyReceipt>> QueryListAsync(string procedure, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<SourceApplyReceiptRow>(new CommandDefinition(
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

    private static FileResultDto ToDto(SourceApplyReceiptFileResult result) => new(
        Normalize(result.Path),
        NormalizeNullable(result.PreviousPath),
        Normalize(result.OperationKind),
        Normalize(result.PatchArtifactChangeHash),
        Normalize(result.OperationHash),
        NormalizeNullable(result.BeforeContentHash),
        NormalizeNullable(result.AfterContentHash),
        result.PreconditionsSatisfied,
        result.MutationApplied,
        result.Created,
        result.Modified,
        result.Deleted,
        result.Renamed,
        result.Noop,
        result.IssueCodes.Select(Normalize).ToArray(),
        Normalize(result.FileResultHash));

    private sealed class SourceApplyReceiptRow
    {
        public Guid SourceApplyReceiptId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid ControlledSourceApplyRequestId { get; init; }
        public Guid SourceApplyRequestId { get; init; }
        public string SourceApplyRequestHash { get; init; } = string.Empty;
        public Guid SourceApplyDryRunReceiptId { get; init; }
        public string SourceApplyDryRunReceiptHash { get; init; } = string.Empty;
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
        public string ObservedBranch { get; init; } = string.Empty;
        public string ObservedCleanWorktreeHashBeforeApply { get; init; } = string.Empty;
        public string ObservedCleanWorktreeHashAfterApply { get; init; } = string.Empty;
        public bool MutationOccurred { get; init; }
        public bool ApplySucceeded { get; init; }
        public bool PartialApplyOccurred { get; init; }
        public string FileResultsJson { get; init; } = "[]";
        public string IssueCodesJson { get; init; } = "[]";
        public DateTimeOffset AppliedAtUtc { get; init; }
        public string SourceApplyReceiptHash { get; init; } = string.Empty;
        public string EvidenceReferencesJson { get; init; } = "[]";
        public string BoundaryMaximsJson { get; init; } = "[]";
        public string BoundaryText { get; init; } = string.Empty;

        public SourceApplyReceipt ToReceipt() => new()
        {
            SourceApplyReceiptId = SourceApplyReceiptId,
            ProjectId = ProjectId,
            ControlledSourceApplyRequestId = ControlledSourceApplyRequestId,
            SourceApplyRequestId = SourceApplyRequestId,
            SourceApplyRequestHash = SourceApplyRequestHash,
            SourceApplyDryRunReceiptId = SourceApplyDryRunReceiptId,
            SourceApplyDryRunReceiptHash = SourceApplyDryRunReceiptHash,
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
            ObservedBranch = ObservedBranch,
            ObservedCleanWorktreeHashBeforeApply = ObservedCleanWorktreeHashBeforeApply,
            ObservedCleanWorktreeHashAfterApply = ObservedCleanWorktreeHashAfterApply,
            MutationOccurred = MutationOccurred,
            ApplySucceeded = ApplySucceeded,
            PartialApplyOccurred = PartialApplyOccurred,
            FileResults = DeserializeFileResults(FileResultsJson),
            IssueCodes = DeserializeList(IssueCodesJson),
            AppliedAtUtc = AppliedAtUtc,
            SourceApplyReceiptHash = SourceApplyReceiptHash,
            EvidenceReferences = DeserializeList(EvidenceReferencesJson),
            BoundaryMaxims = DeserializeList(BoundaryMaximsJson),
            Boundary = BoundaryText
        };
    }

    private static IReadOnlyList<SourceApplyReceiptFileResult> DeserializeFileResults(string json) =>
        JsonSerializer.Deserialize<FileResultDto[]>(json, JsonOptions)?.Select(result => new SourceApplyReceiptFileResult
        {
            Path = result.Path,
            PreviousPath = result.PreviousPath,
            OperationKind = result.OperationKind,
            PatchArtifactChangeHash = result.PatchArtifactChangeHash,
            OperationHash = result.OperationHash,
            BeforeContentHash = result.BeforeContentHash,
            AfterContentHash = result.AfterContentHash,
            PreconditionsSatisfied = result.PreconditionsSatisfied,
            MutationApplied = result.MutationApplied,
            Created = result.Created,
            Modified = result.Modified,
            Deleted = result.Deleted,
            Renamed = result.Renamed,
            Noop = result.Noop,
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
        string? BeforeContentHash,
        string? AfterContentHash,
        bool PreconditionsSatisfied,
        bool MutationApplied,
        bool Created,
        bool Modified,
        bool Deleted,
        bool Renamed,
        bool Noop,
        IReadOnlyList<string> IssueCodes,
        string FileResultHash);
}
