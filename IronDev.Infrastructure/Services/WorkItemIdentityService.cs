using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.WorkItems;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Infrastructure.Services;

public sealed class WorkItemIdentityService : IWorkItemIdentityService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;

    public WorkItemIdentityService(IDbConnectionFactory connectionFactory, ICurrentTenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }

    public async Task EnsureForTicketAsync(ProjectTicket ticket, long ticketId, CancellationToken cancellationToken = default)
    {
        var contractHash = ContractHash(ticket);
        var stage = StageFor(ticket.Status);
        var testExpectations = TestExpectationsFor(ticket);

        const string sql = """
            IF OBJECT_ID(N'dbo.WorkItems', N'U') IS NULL OR OBJECT_ID(N'dbo.WorkItemContracts', N'U') IS NULL
            BEGIN
                THROW 53110, 'dbo.WorkItems and dbo.WorkItemContracts must exist before ticket-backed Work Item identity can be saved.', 1;
            END;

            IF NOT EXISTS (SELECT 1 FROM dbo.WorkItems WHERE Id = @TicketId)
            BEGIN
                INSERT INTO dbo.WorkItems
                    (Id, TenantId, ProjectId, Title, OriginKind, OriginReference, LegacyTicketId, CurrentStage, CurrentState, CreatedUtc, UpdatedUtc)
                VALUES
                    (@TicketId, @TenantId, @ProjectId, @Title, N'Ticket', @OriginReference, @TicketId, @CurrentStage, @CurrentState, SYSUTCDATETIME(), SYSUTCDATETIME());
            END
            ELSE
            BEGIN
                UPDATE dbo.WorkItems
                SET Title = @Title,
                    CurrentStage = @CurrentStage,
                    CurrentState = @CurrentState,
                    UpdatedUtc = SYSUTCDATETIME(),
                    Version = Version + 1
                WHERE Id = @TicketId
                  AND TenantId = @TenantId
                  AND ProjectId = @ProjectId;
            END;

            DECLARE @CurrentContractId BIGINT =
            (
                SELECT TOP (1) Id
                FROM dbo.WorkItemContracts
                WHERE TenantId = @TenantId
                  AND ProjectId = @ProjectId
                  AND WorkItemId = @TicketId
                ORDER BY ContractVersion DESC, Id DESC
            );

            DECLARE @CurrentHash NVARCHAR(64) =
            (
                SELECT ContractHash
                FROM dbo.WorkItemContracts
                WHERE Id = @CurrentContractId
            );

            IF @CurrentContractId IS NULL OR @CurrentHash <> @ContractHash
            BEGIN
                DECLARE @NextVersion INT =
                    COALESCE((
                        SELECT MAX(ContractVersion)
                        FROM dbo.WorkItemContracts
                        WHERE TenantId = @TenantId
                          AND ProjectId = @ProjectId
                          AND WorkItemId = @TicketId
                    ), 0) + 1;

                INSERT INTO dbo.WorkItemContracts
                    (TenantId, ProjectId, WorkItemId, ContractVersion, SourceTicketId, Title, Summary, Problem,
                     AcceptanceCriteria, TechnicalNotes, TestExpectations, LinkedFilePaths, LinkedCodeIndexEntryIds,
                     LinkedSymbols, SourceWorkshopSessionId, SourceWorkshopMessageIds, SourceDocumentVersionIds,
                     SupersedesContractId, ContractHash)
                VALUES
                    (@TenantId, @ProjectId, @TicketId, @NextVersion, @TicketId, @Title, @Summary, @Problem,
                     @AcceptanceCriteria, @TechnicalNotes, @TestExpectations, @LinkedFilePaths, @LinkedCodeIndexEntryIds,
                     @LinkedSymbols, @SourceWorkshopSessionId, @SourceWorkshopMessageIds, @SourceDocumentVersionIds,
                     @CurrentContractId, @ContractHash);

                SET @CurrentContractId = CONVERT(BIGINT, SCOPE_IDENTITY());
            END;

            UPDATE dbo.WorkItems
            SET CurrentContractId = @CurrentContractId,
                UpdatedUtc = SYSUTCDATETIME()
            WHERE Id = @TicketId
              AND TenantId = @TenantId
              AND ProjectId = @ProjectId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new
                {
                    TicketId = ticketId,
                    TenantId = _tenant.TenantId,
                    ticket.ProjectId,
                    Title = TextOr(ticket.Title, $"Work item {ticketId}"),
                    OriginReference = $"Ticket:{ticketId}",
                    CurrentStage = stage,
                    CurrentState = TextOr(ticket.Status, "Draft"),
                    ticket.Summary,
                    ticket.Problem,
                    ticket.AcceptanceCriteria,
                    ticket.TechnicalNotes,
                    TestExpectations = testExpectations,
                    ticket.LinkedFilePaths,
                    ticket.LinkedCodeIndexEntryIds,
                    ticket.LinkedSymbols,
                    SourceWorkshopSessionId = ticket.SourceChatSessionId,
                    SourceWorkshopMessageIds = ticket.SourceChatMessageId?.ToString(CultureInfo.InvariantCulture),
                    SourceDocumentVersionIds = ticket.SourceDocumentVersionId?.ToString(CultureInfo.InvariantCulture),
                    ContractHash = contractHash
                },
                transaction,
                cancellationToken: cancellationToken));

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<WorkItemIdentitySnapshot?> GetByWorkItemIdAsync(
        int projectId,
        long workItemId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (1)
                wi.Id AS WorkItemId,
                wi.TenantId,
                wi.ProjectId,
                wi.Title,
                wi.LegacyTicketId,
                wi.CurrentContractId,
                wi.CurrentStage,
                wi.CurrentState,
                wi.Version,
                c.Id AS ContractId,
                c.ContractVersion,
                c.WorkItemId AS ContractWorkItemId,
                c.SourceTicketId,
                c.Title AS ContractTitle,
                c.AcceptanceCriteria,
                c.LinkedFilePaths,
                c.SourceWorkshopSessionId,
                c.SourceWorkshopMessageIds,
                c.SourceDocumentVersionIds
            FROM dbo.WorkItems wi
            LEFT JOIN dbo.WorkItemContracts c ON c.Id = wi.CurrentContractId
            WHERE wi.TenantId = @TenantId
              AND wi.ProjectId = @ProjectId
              AND wi.Id = @WorkItemId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<WorkItemIdentityRow>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, WorkItemId = workItemId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToSnapshot(row);
    }

    public async Task<WorkItemIdentitySnapshot?> GetByLegacyTicketIdAsync(long ticketId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (1)
                wi.Id AS WorkItemId,
                wi.TenantId,
                wi.ProjectId,
                wi.Title,
                wi.LegacyTicketId,
                wi.CurrentContractId,
                wi.CurrentStage,
                wi.CurrentState,
                wi.Version,
                c.Id AS ContractId,
                c.ContractVersion,
                c.WorkItemId AS ContractWorkItemId,
                c.SourceTicketId,
                c.Title AS ContractTitle,
                c.AcceptanceCriteria,
                c.LinkedFilePaths,
                c.SourceWorkshopSessionId,
                c.SourceWorkshopMessageIds,
                c.SourceDocumentVersionIds
            FROM dbo.WorkItems wi
            LEFT JOIN dbo.WorkItemContracts c ON c.Id = wi.CurrentContractId
            WHERE wi.TenantId = @TenantId
              AND wi.LegacyTicketId = @TicketId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<WorkItemIdentityRow>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, TicketId = ticketId },
            cancellationToken: cancellationToken));
        return row is null ? null : ToSnapshot(row);
    }

    private static WorkItemIdentitySnapshot ToSnapshot(WorkItemIdentityRow row) => new()
    {
        WorkItemId = row.WorkItemId,
        TenantId = row.TenantId,
        ProjectId = row.ProjectId,
        Title = row.Title,
        LegacyTicketId = row.LegacyTicketId,
        CurrentContractId = row.CurrentContractId,
        CurrentStage = row.CurrentStage,
        CurrentState = row.CurrentState,
        Version = row.Version,
        CurrentContract = row.ContractId is long contractId
            ? new WorkItemContractSnapshot
            {
                ContractId = contractId,
                ContractVersion = row.ContractVersion,
                WorkItemId = row.ContractWorkItemId,
                SourceTicketId = row.SourceTicketId,
                Title = row.ContractTitle ?? row.Title,
                AcceptanceCriteria = row.AcceptanceCriteria,
                LinkedFilePaths = row.LinkedFilePaths,
                SourceWorkshopSessionId = row.SourceWorkshopSessionId,
                SourceWorkshopMessageId = FirstLong(row.SourceWorkshopMessageIds),
                SourceDocumentVersionId = FirstLong(row.SourceDocumentVersionIds)
            }
            : null
    };

    private static long? FirstLong(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        foreach (var part in value.Split(new[] { ',', ';', ' ', '\r', '\n', '\t', '[', ']' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

        return null;
    }

    private static string StageFor(string? status)
    {
        if (ContainsAny(status, "applied", "done", "closed")) return ProjectWorkItemStages.Done;
        if (ContainsAny(status, "approval", "review")) return ProjectWorkItemStages.Review;
        if (ContainsAny(status, "build", "progress", "failed", "blocked")) return ProjectWorkItemStages.Build;
        if (ContainsAny(status, "shape")) return ProjectWorkItemStages.Shape;
        return ProjectWorkItemStages.Ticket;
    }

    private static string ContractHash(ProjectTicket ticket)
    {
        var material = string.Join(
            "\n",
            ticket.Title,
            ticket.Summary,
            ticket.Problem,
            ticket.AcceptanceCriteria,
            ticket.TechnicalNotes,
            ticket.LinkedFilePaths,
            ticket.LinkedCodeIndexEntryIds,
            ticket.LinkedSymbols,
            ticket.UnitTests,
            ticket.IntegrationTests,
            ticket.ManualTests,
            ticket.RegressionTests,
            ticket.BuildValidation);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static string TestExpectationsFor(ProjectTicket ticket) => string.Join(
        "\n",
        $"Unit: {ticket.UnitTests ?? string.Empty}",
        $"Integration: {ticket.IntegrationTests ?? string.Empty}",
        $"Manual: {ticket.ManualTests ?? string.Empty}",
        $"Regression: {ticket.RegressionTests ?? string.Empty}",
        $"Build: {ticket.BuildValidation ?? string.Empty}");

    private static bool ContainsAny(string? value, params string[] needles) =>
        !string.IsNullOrWhiteSpace(value) && needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));

    private static string TextOr(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed class WorkItemIdentityRow
    {
        public long WorkItemId { get; init; }
        public int TenantId { get; init; }
        public int ProjectId { get; init; }
        public string Title { get; init; } = string.Empty;
        public long? LegacyTicketId { get; init; }
        public long? CurrentContractId { get; init; }
        public string CurrentStage { get; init; } = ProjectWorkItemStages.Ticket;
        public string CurrentState { get; init; } = "Draft";
        public long Version { get; init; }
        public long? ContractId { get; init; }
        public int ContractVersion { get; init; }
        public long ContractWorkItemId { get; init; }
        public long? SourceTicketId { get; init; }
        public string? ContractTitle { get; init; }
        public string? AcceptanceCriteria { get; init; }
        public string? LinkedFilePaths { get; init; }
        public long? SourceWorkshopSessionId { get; init; }
        public string? SourceWorkshopMessageIds { get; init; }
        public string? SourceDocumentVersionIds { get; init; }
    }
}
