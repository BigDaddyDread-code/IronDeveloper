using System.Data;
using System.Text.Json;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class SqlRollbackSupportReceiptStore : IRollbackSupportReceiptStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDbConnectionFactory _connectionFactory;

    public SqlRollbackSupportReceiptStore(IDbConnectionFactory connectionFactory) =>
        _connectionFactory = connectionFactory;

    public async Task SaveAsync(RollbackSupportReceipt receipt, CancellationToken cancellationToken = default)
    {
        var validation = RollbackSupportReceiptValidation.Validate(receipt);
        if (!validation.IsValid)
        {
            throw new ArgumentException(string.Join("; ", validation.Issues.Select(issue => $"{issue.Code}: {issue.Message}")), nameof(receipt));
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "governance.usp_RollbackSupportReceipt_Save",
            new
            {
                receipt.RollbackSupportReceiptId,
                receipt.ProjectId,
                receipt.RollbackPlanId,
                RollbackPlanHash = Normalize(receipt.RollbackPlanHash),
                receipt.RollbackGateSatisfied,
                RollbackGateEvaluationHash = Normalize(receipt.RollbackGateEvaluationHash),
                receipt.PatchArtifactId,
                PatchHash = Normalize(receipt.PatchHash),
                ChangeSetHash = Normalize(receipt.ChangeSetHash),
                receipt.ControlledDryRunRequestId,
                receipt.DryRunExecutionAuditId,
                DryRunAuditHash = Normalize(receipt.DryRunAuditHash),
                DryRunReceiptHash = Normalize(receipt.DryRunReceiptHash),
                receipt.PolicySatisfactionId,
                PolicySatisfactionHash = Normalize(receipt.PolicySatisfactionHash),
                SubjectKind = Normalize(receipt.SubjectKind),
                SubjectId = Normalize(receipt.SubjectId),
                SubjectHash = Normalize(receipt.SubjectHash),
                SourceSnapshotReference = Normalize(receipt.SourceSnapshotReference),
                SourceBaselineHash = Normalize(receipt.SourceBaselineHash),
                WorkspaceBoundaryHash = Normalize(receipt.WorkspaceBoundaryHash),
                ExpectedBranch = Normalize(receipt.ExpectedBranch),
                ExpectedCleanWorktreeHash = Normalize(receipt.ExpectedCleanWorktreeHash),
                RollbackSupportReceiptHash = Normalize(receipt.RollbackSupportReceiptHash),
                receipt.CreatedAtUtc,
                receipt.ExpiresAtUtc,
                EvidenceReferencesJson = JsonSerializer.Serialize(receipt.EvidenceReferences.Select(Normalize).ToArray(), JsonOptions),
                BoundaryMaximsJson = JsonSerializer.Serialize(receipt.BoundaryMaxims.Select(Normalize).ToArray(), JsonOptions),
                BoundaryText = Normalize(receipt.Boundary)
            },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));
    }

    public async Task<RollbackSupportReceipt?> GetAsync(Guid projectId, Guid rollbackSupportReceiptId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<RollbackSupportReceiptRow>(new CommandDefinition(
            "governance.usp_RollbackSupportReceipt_Get",
            new { ProjectId = projectId, RollbackSupportReceiptId = rollbackSupportReceiptId },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToReceipt();
    }

    public async Task<RollbackSupportReceipt?> GetByReceiptHashAsync(Guid projectId, string rollbackSupportReceiptHash, CancellationToken cancellationToken = default)
    {
        RequireText(rollbackSupportReceiptHash, nameof(rollbackSupportReceiptHash));
        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<RollbackSupportReceiptRow>(new CommandDefinition(
            "governance.usp_RollbackSupportReceipt_GetByReceiptHash",
            new { ProjectId = projectId, RollbackSupportReceiptHash = Normalize(rollbackSupportReceiptHash) },
            commandType: CommandType.StoredProcedure,
            cancellationToken: cancellationToken));

        return row?.ToReceipt();
    }

    public async Task<IReadOnlyList<RollbackSupportReceipt>> ListByPatchArtifactAsync(Guid projectId, Guid patchArtifactId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_RollbackSupportReceipt_ListByPatchArtifact", new { ProjectId = projectId, PatchArtifactId = patchArtifactId }, cancellationToken);

    public async Task<IReadOnlyList<RollbackSupportReceipt>> ListByPatchHashAsync(Guid projectId, string patchHash, CancellationToken cancellationToken = default)
    {
        RequireText(patchHash, nameof(patchHash));
        return await QueryListAsync("governance.usp_RollbackSupportReceipt_ListByPatchHash", new { ProjectId = projectId, PatchHash = Normalize(patchHash) }, cancellationToken);
    }

    public async Task<IReadOnlyList<RollbackSupportReceipt>> ListByRollbackPlanAsync(Guid projectId, Guid rollbackPlanId, CancellationToken cancellationToken = default) =>
        await QueryListAsync("governance.usp_RollbackSupportReceipt_ListByRollbackPlan", new { ProjectId = projectId, RollbackPlanId = rollbackPlanId }, cancellationToken);

    public async Task<IReadOnlyList<RollbackSupportReceipt>> ListBySourceBaselineHashAsync(Guid projectId, string sourceBaselineHash, CancellationToken cancellationToken = default)
    {
        RequireText(sourceBaselineHash, nameof(sourceBaselineHash));
        return await QueryListAsync("governance.usp_RollbackSupportReceipt_ListBySourceBaselineHash", new { ProjectId = projectId, SourceBaselineHash = Normalize(sourceBaselineHash) }, cancellationToken);
    }

    private async Task<IReadOnlyList<RollbackSupportReceipt>> QueryListAsync(string procedure, object parameters, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<RollbackSupportReceiptRow>(new CommandDefinition(
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

    private sealed class RollbackSupportReceiptRow
    {
        public Guid RollbackSupportReceiptId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid RollbackPlanId { get; init; }
        public string RollbackPlanHash { get; init; } = string.Empty;
        public bool RollbackGateSatisfied { get; init; }
        public string RollbackGateEvaluationHash { get; init; } = string.Empty;
        public Guid PatchArtifactId { get; init; }
        public string PatchHash { get; init; } = string.Empty;
        public string ChangeSetHash { get; init; } = string.Empty;
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
        public string ExpectedBranch { get; init; } = string.Empty;
        public string ExpectedCleanWorktreeHash { get; init; } = string.Empty;
        public string RollbackSupportReceiptHash { get; init; } = string.Empty;
        public DateTimeOffset CreatedAtUtc { get; init; }
        public DateTimeOffset? ExpiresAtUtc { get; init; }
        public string EvidenceReferencesJson { get; init; } = "[]";
        public string BoundaryMaximsJson { get; init; } = "[]";
        public string BoundaryText { get; init; } = string.Empty;

        public RollbackSupportReceipt ToReceipt() => new()
        {
            RollbackSupportReceiptId = RollbackSupportReceiptId,
            ProjectId = ProjectId,
            RollbackPlanId = RollbackPlanId,
            RollbackPlanHash = RollbackPlanHash,
            RollbackGateSatisfied = RollbackGateSatisfied,
            RollbackGateEvaluationHash = RollbackGateEvaluationHash,
            PatchArtifactId = PatchArtifactId,
            PatchHash = PatchHash,
            ChangeSetHash = ChangeSetHash,
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
            ExpectedBranch = ExpectedBranch,
            ExpectedCleanWorktreeHash = ExpectedCleanWorktreeHash,
            RollbackSupportReceiptHash = RollbackSupportReceiptHash,
            CreatedAtUtc = CreatedAtUtc,
            ExpiresAtUtc = ExpiresAtUtc,
            EvidenceReferences = DeserializeList(EvidenceReferencesJson),
            BoundaryMaxims = DeserializeList(BoundaryMaximsJson),
            Boundary = BoundaryText
        };
    }

    private static IReadOnlyList<string> DeserializeList(string json) =>
        JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
}
