using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Services;

public interface ITicketService
{
    Task<long> SaveTicketAsync(ProjectTicket ticket, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectTicket>> GetRecentTicketsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default);
    Task<ProjectTicket?> GetTicketByIdAsync(long ticketId, CancellationToken cancellationToken = default);
    Task<bool> ArchiveTicketAsync(long ticketId, CancellationToken cancellationToken = default);
}

public sealed class TicketService : ITicketService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;

    public TicketService(IDbConnectionFactory connectionFactory, ICurrentTenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }

    public async Task<long> SaveTicketAsync(ProjectTicket ticket, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // Ownership guard: reject operations where the project belongs to a different tenant.
        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownerSql,
            new { ticket.ProjectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        if (owns == 0)
            throw new System.UnauthorizedAccessException(
                $"Project {ticket.ProjectId} does not belong to tenant {_tenant.TenantId}.");

        if (ticket.Id > 0)
        {
            // Update flow
            const string updateSql = """
                UPDATE dbo.ProjectTickets
                SET Title = @Title, TicketType = @TicketType, Priority = @Priority,
                    Summary = @Summary, Background = @Background, Problem = @Problem,
                    AcceptanceCriteria = @AcceptanceCriteria, TechnicalNotes = @TechnicalNotes,
                    Status = @Status, Content = @Content, LinkedFilePaths = @LinkedFilePaths,
                    LinkedCodeIndexEntryIds = @LinkedCodeIndexEntryIds, LinkedSymbols = @LinkedSymbols,
                    UnitTests = @UnitTests, IntegrationTests = @IntegrationTests,
                    ManualTests = @ManualTests, RegressionTests = @RegressionTests,
                    BuildValidation = @BuildValidation, ContextSummary = @ContextSummary,
                    IsGenerated = @IsGenerated, GenerationNote = @GenerationNote
                WHERE Id = @Id AND TenantId = @TenantId AND ProjectId = @ProjectId;
                """;

            var rowsAffected = await connection.ExecuteAsync(new CommandDefinition(
                updateSql,
                new
                {
                    ticket.Id,
                    TenantId = _tenant.TenantId,
                    ticket.ProjectId,
                    ticket.Title,
                    ticket.TicketType,
                    ticket.Priority,
                    ticket.Summary,
                    ticket.Background,
                    ticket.Problem,
                    ticket.AcceptanceCriteria,
                    ticket.TechnicalNotes,
                    ticket.Status,
                    ticket.Content,
                    ticket.LinkedFilePaths,
                    ticket.LinkedCodeIndexEntryIds,
                    ticket.LinkedSymbols,
                    ticket.UnitTests,
                    ticket.IntegrationTests,
                    ticket.ManualTests,
                    ticket.RegressionTests,
                    ticket.BuildValidation,
                    ticket.ContextSummary,
                    ticket.IsGenerated,
                    ticket.GenerationNote
                },
                cancellationToken: cancellationToken));
                
            if (rowsAffected == 0)
                throw new System.InvalidOperationException("Ticket update failed or ticket not found/not owned.");

            return ticket.Id;
        }
        else
        {
            // Insert flow
            const string insertSql = """
                INSERT INTO dbo.ProjectTickets
                    (TenantId, ProjectId, SessionId, Title, TicketType, Priority,
                     Summary, Background, Problem, AcceptanceCriteria, TechnicalNotes,
                     Status, Content, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols,
                     UnitTests, IntegrationTests, ManualTests, RegressionTests,
                     BuildValidation, ContextSummary, IsGenerated, GenerationNote)
                OUTPUT inserted.Id
                VALUES
                    (@TenantId, @ProjectId, @SessionId, @Title, @TicketType, @Priority,
                     @Summary, @Background, @Problem, @AcceptanceCriteria, @TechnicalNotes,
                     @Status, @Content, @LinkedFilePaths, @LinkedCodeIndexEntryIds, @LinkedSymbols,
                     @UnitTests, @IntegrationTests, @ManualTests, @RegressionTests,
                     @BuildValidation, @ContextSummary, @IsGenerated, @GenerationNote);
                """;

            return await connection.QuerySingleAsync<long>(new CommandDefinition(
                insertSql,
                new
                {
                    TenantId = _tenant.TenantId,
                    ticket.ProjectId,
                    ticket.SessionId,
                    ticket.Title,
                    ticket.TicketType,
                    ticket.Priority,
                    ticket.Summary,
                    ticket.Background,
                    ticket.Problem,
                    ticket.AcceptanceCriteria,
                    ticket.TechnicalNotes,
                    ticket.Status,
                    ticket.Content,
                    ticket.LinkedFilePaths,
                    ticket.LinkedCodeIndexEntryIds,
                    ticket.LinkedSymbols,
                    ticket.UnitTests,
                    ticket.IntegrationTests,
                    ticket.ManualTests,
                    ticket.RegressionTests,
                    ticket.BuildValidation,
                    ticket.ContextSummary,
                    ticket.IsGenerated,
                    ticket.GenerationNote
                },
                cancellationToken: cancellationToken));
        }
    }

    public async Task<IReadOnlyList<ProjectTicket>> GetRecentTicketsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, SessionId, Title, TicketType, Priority,
                Summary, Background, Problem, AcceptanceCriteria, TechnicalNotes,
                Status, Content, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols,
                UnitTests, IntegrationTests, ManualTests, RegressionTests,
                BuildValidation, ContextSummary, IsGenerated, GenerationNote, CreatedDate
            FROM dbo.ProjectTickets
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND IsDeleted = 0
            ORDER BY CreatedDate DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<ProjectTicket>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, Take = take },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<ProjectTicket?> GetTicketByIdAsync(long ticketId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id, TenantId, ProjectId, SessionId, Title, TicketType, Priority,
                Summary, Background, Problem, AcceptanceCriteria, TechnicalNotes,
                Status, Content, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols,
                UnitTests, IntegrationTests, ManualTests, RegressionTests,
                BuildValidation, ContextSummary, IsGenerated, GenerationNote, CreatedDate
            FROM dbo.ProjectTickets
                WHERE Id = @TicketId
              AND TenantId = @TenantId
              AND IsDeleted = 0;
            """;

        using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<ProjectTicket>(new CommandDefinition(
            sql,
            new { TicketId = ticketId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> ArchiveTicketAsync(long ticketId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE dbo.ProjectTickets
            SET IsDeleted = 1
            WHERE Id = @TicketId AND TenantId = @TenantId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rowsAffected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { TicketId = ticketId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        return rowsAffected > 0;
    }
}
