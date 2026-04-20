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

        // Ownership guard: reject inserts where the project belongs to a different tenant.
        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownerSql,
            new { ticket.ProjectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        if (owns == 0)
            throw new System.UnauthorizedAccessException(
                $"Project {ticket.ProjectId} does not belong to tenant {_tenant.TenantId}.");

        const string sql = """
            INSERT INTO dbo.ProjectTickets
                (TenantId, ProjectId, SessionId, Title, TicketType, Priority,
                 Summary, Background, Problem, AcceptanceCriteria, TechnicalNotes,
                 Status, Content, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols)
            OUTPUT inserted.Id
            VALUES
                (@TenantId, @ProjectId, @SessionId, @Title, @TicketType, @Priority,
                 @Summary, @Background, @Problem, @AcceptanceCriteria, @TechnicalNotes,
                 @Status, @Content, @LinkedFilePaths, @LinkedCodeIndexEntryIds, @LinkedSymbols);
            """;

        return await connection.QuerySingleAsync<long>(new CommandDefinition(
            sql,
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
                ticket.LinkedSymbols
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ProjectTicket>> GetRecentTicketsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, SessionId, Title, TicketType, Priority,
                Summary, Background, Problem, AcceptanceCriteria, TechnicalNotes,
                Status, Content, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols, CreatedDate
            FROM dbo.ProjectTickets
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
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
                Status, Content, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols, CreatedDate
            FROM dbo.ProjectTickets
            WHERE Id = @TicketId
              AND TenantId = @TenantId;
            """;

        using var connection = _connectionFactory.CreateConnection();

        return await connection.QuerySingleOrDefaultAsync<ProjectTicket>(new CommandDefinition(
            sql,
            new { TicketId = ticketId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));
    }
}
