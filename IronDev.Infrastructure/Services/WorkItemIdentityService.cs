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

    public async Task<WorkItemIdentitySnapshot?> GetByLegacyTicketIdAsync(long ticketId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (1)
                Id AS WorkItemId,
                LegacyTicketId,
                CurrentContractId,
                CurrentStage,
                CurrentState
            FROM dbo.WorkItems
            WHERE TenantId = @TenantId
              AND LegacyTicketId = @TicketId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<WorkItemIdentitySnapshot>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, TicketId = ticketId },
            cancellationToken: cancellationToken));
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
}
