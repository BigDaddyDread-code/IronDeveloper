using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlRollbackExecutionReceiptStore : IRollbackExecutionReceiptStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;

    public SqlRollbackExecutionReceiptStore(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task SaveAsync(RollbackExecutionReceipt receipt, CancellationToken cancellationToken = default)
    {
        var validation = RollbackExecutionReceiptValidation.Validate(receipt);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(receipt));
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "governance.usp_RollbackExecutionReceipt_Save",
            new
            {
                receipt.RollbackExecutionReceiptId,
                receipt.ProjectId,
                receipt.ControlledRollbackExecutionRequestId,
                receipt.RollbackPlanId,
                RollbackPlanHash = Normalize(receipt.RollbackPlanHash),
                receipt.RollbackSupportReceiptId,
                RollbackSupportReceiptHash = Normalize(receipt.RollbackSupportReceiptHash),
                receipt.SourceApplyRequestId,
                SourceApplyRequestHash = Normalize(receipt.SourceApplyRequestHash),
                receipt.SourceApplyReceiptId,
                SourceApplyReceiptHash = Normalize(receipt.SourceApplyReceiptHash),
                receipt.PatchArtifactId,
                PatchHash = Normalize(receipt.PatchHash),
                ChangeSetHash = Normalize(receipt.ChangeSetHash),
                SourceBaselineHash = Normalize(receipt.SourceBaselineHash),
                WorkspaceBoundaryHash = Normalize(receipt.WorkspaceBoundaryHash),
                ExpectedBranch = Normalize(receipt.ExpectedBranch),
                ExpectedCleanWorktreeHash = Normalize(receipt.ExpectedCleanWorktreeHash),
                ObservedBranch = Normalize(receipt.ObservedBranch),
                ObservedSourceBaselineHash = Normalize(receipt.ObservedSourceBaselineHash),
                ObservedCleanWorktreeHashBeforeRollback = Normalize(receipt.ObservedCleanWorktreeHashBeforeRollback),
                ObservedCleanWorktreeHashAfterRollback = Normalize(receipt.ObservedCleanWorktreeHashAfterRollback),
                receipt.MutationOccurred,
                receipt.RollbackSucceeded,
                receipt.PartialRollbackOccurred,
                FileResultsJson = JsonSerializer.Serialize(receipt.FileResults.Select(ToDto).ToArray(), JsonOptions),
                IssueCodesJson = JsonSerializer.Serialize(receipt.IssueCodes.Select(Normalize).ToArray(), JsonOptions),
                receipt.RolledBackAtUtc,
                RollbackExecutionReceiptHash = Normalize(receipt.RollbackExecutionReceiptHash),
                EvidenceReferencesJson = JsonSerializer.Serialize(receipt.EvidenceReferences.Select(Normalize).ToArray(), JsonOptions),
                BoundaryMaximsJson = JsonSerializer.Serialize(receipt.BoundaryMaxims.Select(Normalize).ToArray(), JsonOptions),
                BoundaryText = Normalize(receipt.Boundary)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<RollbackExecutionReceipt?> GetAsync(Guid projectId, Guid rollbackExecutionReceiptId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<RollbackExecutionReceiptRow>(new CommandDefinition(
            "governance.usp_RollbackExecutionReceipt_Get",
            new { ProjectId = projectId, RollbackExecutionReceiptId = rollbackExecutionReceiptId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToReceipt();
    }

    public async Task<RollbackExecutionReceipt?> GetByReceiptHashAsync(Guid projectId, string rollbackExecutionReceiptHash, CancellationToken cancellationToken = default)
    {
        RequireText(rollbackExecutionReceiptHash, nameof(rollbackExecutionReceiptHash));
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<RollbackExecutionReceiptRow>(new CommandDefinition(
            "governance.usp_RollbackExecutionReceipt_GetByReceiptHash",
            new { ProjectId = projectId, RollbackExecutionReceiptHash = Normalize(rollbackExecutionReceiptHash) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToReceipt();
    }

    public async Task<IReadOnlyList<RollbackExecutionReceipt>> ListBySourceApplyReceiptAsync(Guid projectId, Guid sourceApplyReceiptId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_RollbackExecutionReceipt_ListBySourceApplyReceipt", new { ProjectId = projectId, SourceApplyReceiptId = sourceApplyReceiptId }, cancellationToken);

    public async Task<IReadOnlyList<RollbackExecutionReceipt>> ListByRollbackPlanAsync(Guid projectId, Guid rollbackPlanId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_RollbackExecutionReceipt_ListByRollbackPlan", new { ProjectId = projectId, RollbackPlanId = rollbackPlanId }, cancellationToken);

    public async Task<IReadOnlyList<RollbackExecutionReceipt>> ListByRollbackSupportReceiptAsync(Guid projectId, Guid rollbackSupportReceiptId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_RollbackExecutionReceipt_ListByRollbackSupportReceipt", new { ProjectId = projectId, RollbackSupportReceiptId = rollbackSupportReceiptId }, cancellationToken);

    public async Task<IReadOnlyList<RollbackExecutionReceipt>> ListByPatchArtifactAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_RollbackExecutionReceipt_ListByPatchArtifact", new { ProjectId = projectId, PatchArtifactId = patchArtifactId }, cancellationToken);

    private async Task<IReadOnlyList<RollbackExecutionReceipt>> QueryListAsync(string procedure, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<RollbackExecutionReceiptRow>(new CommandDefinition(
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

    private static FileResultDto ToDto(RollbackExecutionReceiptFileResult result) => new(
        Normalize(result.Path),
        NormalizeNullable(result.PreviousPath),
        Normalize(result.OperationKind),
        Normalize(result.PatchArtifactChangeHash),
        Normalize(result.RollbackActionHash),
        NormalizeNullable(result.BeforeContentHash),
        NormalizeNullable(result.AfterContentHash),
        result.PreconditionsSatisfied,
        result.MutationApplied,
        result.Restored,
        result.Deleted,
        result.Recreated,
        result.RenamedBack,
        result.Noop,
        result.IssueCodes.Select(Normalize).ToArray(),
        Normalize(result.FileResultHash));

    private sealed class RollbackExecutionReceiptRow
    {
        public Guid RollbackExecutionReceiptId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid ControlledRollbackExecutionRequestId { get; init; }
        public Guid RollbackPlanId { get; init; }
        public string RollbackPlanHash { get; init; } = string.Empty;
        public Guid RollbackSupportReceiptId { get; init; }
        public string RollbackSupportReceiptHash { get; init; } = string.Empty;
        public Guid SourceApplyRequestId { get; init; }
        public string SourceApplyRequestHash { get; init; } = string.Empty;
        public Guid SourceApplyReceiptId { get; init; }
        public string SourceApplyReceiptHash { get; init; } = string.Empty;
        public Guid PatchArtifactId { get; init; }
        public string PatchHash { get; init; } = string.Empty;
        public string ChangeSetHash { get; init; } = string.Empty;
        public string SourceBaselineHash { get; init; } = string.Empty;
        public string WorkspaceBoundaryHash { get; init; } = string.Empty;
        public string ExpectedBranch { get; init; } = string.Empty;
        public string ExpectedCleanWorktreeHash { get; init; } = string.Empty;
        public string ObservedBranch { get; init; } = string.Empty;
        public string ObservedSourceBaselineHash { get; init; } = string.Empty;
        public string ObservedCleanWorktreeHashBeforeRollback { get; init; } = string.Empty;
        public string ObservedCleanWorktreeHashAfterRollback { get; init; } = string.Empty;
        public bool MutationOccurred { get; init; }
        public bool RollbackSucceeded { get; init; }
        public bool PartialRollbackOccurred { get; init; }
        public string FileResultsJson { get; init; } = "[]";
        public string IssueCodesJson { get; init; } = "[]";
        public DateTimeOffset RolledBackAtUtc { get; init; }
        public string RollbackExecutionReceiptHash { get; init; } = string.Empty;
        public string EvidenceReferencesJson { get; init; } = "[]";
        public string BoundaryMaximsJson { get; init; } = "[]";
        public string BoundaryText { get; init; } = string.Empty;

        public RollbackExecutionReceipt ToReceipt() => new()
        {
            RollbackExecutionReceiptId = RollbackExecutionReceiptId,
            ProjectId = ProjectId,
            ControlledRollbackExecutionRequestId = ControlledRollbackExecutionRequestId,
            RollbackPlanId = RollbackPlanId,
            RollbackPlanHash = RollbackPlanHash,
            RollbackSupportReceiptId = RollbackSupportReceiptId,
            RollbackSupportReceiptHash = RollbackSupportReceiptHash,
            SourceApplyRequestId = SourceApplyRequestId,
            SourceApplyRequestHash = SourceApplyRequestHash,
            SourceApplyReceiptId = SourceApplyReceiptId,
            SourceApplyReceiptHash = SourceApplyReceiptHash,
            PatchArtifactId = PatchArtifactId,
            PatchHash = PatchHash,
            ChangeSetHash = ChangeSetHash,
            SourceBaselineHash = SourceBaselineHash,
            WorkspaceBoundaryHash = WorkspaceBoundaryHash,
            ExpectedBranch = ExpectedBranch,
            ExpectedCleanWorktreeHash = ExpectedCleanWorktreeHash,
            ObservedBranch = ObservedBranch,
            ObservedSourceBaselineHash = ObservedSourceBaselineHash,
            ObservedCleanWorktreeHashBeforeRollback = ObservedCleanWorktreeHashBeforeRollback,
            ObservedCleanWorktreeHashAfterRollback = ObservedCleanWorktreeHashAfterRollback,
            MutationOccurred = MutationOccurred,
            RollbackSucceeded = RollbackSucceeded,
            PartialRollbackOccurred = PartialRollbackOccurred,
            FileResults = DeserializeFileResults(FileResultsJson),
            IssueCodes = DeserializeList(IssueCodesJson),
            RolledBackAtUtc = RolledBackAtUtc,
            RollbackExecutionReceiptHash = RollbackExecutionReceiptHash,
            EvidenceReferences = DeserializeList(EvidenceReferencesJson),
            BoundaryMaxims = DeserializeList(BoundaryMaximsJson),
            Boundary = BoundaryText
        };
    }

    private static IReadOnlyList<RollbackExecutionReceiptFileResult> DeserializeFileResults(string json) =>
        JsonSerializer.Deserialize<FileResultDto[]>(json, JsonOptions)?.Select(result => new RollbackExecutionReceiptFileResult
        {
            Path = result.Path,
            PreviousPath = result.PreviousPath,
            OperationKind = result.OperationKind,
            PatchArtifactChangeHash = result.PatchArtifactChangeHash,
            RollbackActionHash = result.RollbackActionHash,
            BeforeContentHash = result.BeforeContentHash,
            AfterContentHash = result.AfterContentHash,
            PreconditionsSatisfied = result.PreconditionsSatisfied,
            MutationApplied = result.MutationApplied,
            Restored = result.Restored,
            Deleted = result.Deleted,
            Recreated = result.Recreated,
            RenamedBack = result.RenamedBack,
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
        string RollbackActionHash,
        string? BeforeContentHash,
        string? AfterContentHash,
        bool PreconditionsSatisfied,
        bool MutationApplied,
        bool Restored,
        bool Deleted,
        bool Recreated,
        bool RenamedBack,
        bool Noop,
        IReadOnlyList<string> IssueCodes,
        string FileResultHash);
}
