using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Data;
using IronDev.Data.Models;

namespace IronDev.Services;

public interface IProjectMemoryService
{
    Task<ProjectSummary?> GetLatestSummaryAsync(int projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectDecision>> GetRecentDecisionsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default);
    Task<ProjectDecision?> GetDecisionByIdAsync(long decisionId, CancellationToken cancellationToken = default);
    Task<long> SaveSummaryAsync(ProjectSummary summary, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<ProjectImplementationPlan>> GetRecentPlansAsync(int projectId, int take = 10, CancellationToken cancellationToken = default);
    Task<ProjectImplementationPlan?> GetPlanByIdAsync(long planId, CancellationToken cancellationToken = default);
    Task<ProjectImplementationPlan?> GetPlanByTicketIdAsync(long ticketId, CancellationToken cancellationToken = default);
    Task<long> SavePlanAsync(ProjectImplementationPlan plan, CancellationToken cancellationToken = default);

    Task<long> SaveDecisionAsync(ProjectDecision decision, CancellationToken cancellationToken = default);
}

public sealed class ProjectMemoryService : IProjectMemoryService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;

    public ProjectMemoryService(IDbConnectionFactory connectionFactory, ICurrentTenantContext tenant)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
    }

    public async Task<ProjectSummary?> GetLatestSummaryAsync(int projectId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (1)
                Id, TenantId, ProjectId, Summary, SourceChatMessageId, CreatedDate, UpdatedDate
            FROM dbo.ProjectSummaries
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
            ORDER BY CreatedDate DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ProjectSummary>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ProjectDecision>> GetRecentDecisionsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, Title, Detail, Reason, Category, Status,
                SourceChatMessageId, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols, CreatedDate
            FROM dbo.ProjectDecisions
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
            ORDER BY CreatedDate DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProjectDecision>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, Take = take },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<long> SaveSummaryAsync(ProjectSummary summary, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // Ownership guard: verify the project belongs to the current tenant.
        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownerSql,
            new { summary.ProjectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        if (owns == 0)
            throw new UnauthorizedAccessException(
                $"Project {summary.ProjectId} does not belong to tenant {_tenant.TenantId}.");

        const string sql = """
            INSERT INTO dbo.ProjectSummaries (TenantId, ProjectId, Summary, SourceChatMessageId, UpdatedDate)
            OUTPUT inserted.Id
            VALUES (@TenantId, @ProjectId, @Summary, @SourceChatMessageId, @UpdatedDate);
            """;

        return await connection.QuerySingleAsync<long>(new CommandDefinition(
            sql,
            new
            {
                TenantId = _tenant.TenantId,
                summary.ProjectId,
                summary.Summary,
                summary.SourceChatMessageId,
                summary.UpdatedDate
            },
            cancellationToken: cancellationToken));
    }

    public async Task<ProjectDecision?> GetDecisionByIdAsync(long decisionId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id, TenantId, ProjectId, Title, Detail, Reason, Category, Status,
                SourceChatMessageId, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols, CreatedDate
            FROM dbo.ProjectDecisions
            WHERE Id = @DecisionId
              AND TenantId = @TenantId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ProjectDecision>(new CommandDefinition(
            sql,
            new { DecisionId = decisionId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ProjectImplementationPlan>> GetRecentPlansAsync(int projectId, int take = 10, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, Title, Goal, Status, CreatedDate
            FROM dbo.ProjectImplementationPlans
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
            ORDER BY CreatedDate DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProjectImplementationPlan>(new CommandDefinition(
            sql,
            new { TenantId = _tenant.TenantId, ProjectId = projectId, Take = take },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<ProjectImplementationPlan?> GetPlanByIdAsync(long planId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id, TenantId, ProjectId, Title, Goal, Scope, ProposedSteps, AffectedContext, RisksNotes,
                Status, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols, SourceChatMessageId, CreatedDate
            FROM dbo.ProjectImplementationPlans
            WHERE Id = @PlanId
              AND TenantId = @TenantId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ProjectImplementationPlan>(new CommandDefinition(
            sql,
            new { PlanId = planId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));
    }

    public async Task<ProjectImplementationPlan?> GetPlanByTicketIdAsync(long ticketId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT
                Id, TenantId, ProjectId, TicketId, Title, Goal, Scope, ProposedSteps, AffectedContext, RisksNotes,
                Status, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols, SourceChatMessageId, CreatedDate
            FROM dbo.ProjectImplementationPlans
            WHERE TicketId = @TicketId
              AND TenantId = @TenantId;
            """;

        using var connection = _connectionFactory.CreateConnection();
        return await connection.QuerySingleOrDefaultAsync<ProjectImplementationPlan>(new CommandDefinition(
            sql,
            new { TicketId = ticketId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));
    }

    public async Task<long> SavePlanAsync(ProjectImplementationPlan plan, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // Ownership guard
        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownerSql,
            new { plan.ProjectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        if (owns == 0)
            throw new UnauthorizedAccessException($"Project {plan.ProjectId} does not belong to tenant {_tenant.TenantId}.");

        if (plan.Id > 0)
        {
            const string updateSql = """
                UPDATE dbo.ProjectImplementationPlans 
                SET Title = @Title, Goal = @Goal, Scope = @Scope, 
                    ProposedSteps = @ProposedSteps, AffectedContext = @AffectedContext, 
                    RisksNotes = @RisksNotes, Status = @Status,
                    LinkedFilePaths = @LinkedFilePaths,
                    LinkedCodeIndexEntryIds = @LinkedCodeIndexEntryIds,
                    LinkedSymbols = @LinkedSymbols,
                    TicketId = @TicketId,
                    UpdatedDate = SYSUTCDATETIME()
                WHERE Id = @Id AND TenantId = @TenantId AND ProjectId = @ProjectId;
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                updateSql,
                new 
                { 
                    plan.Id,
                    TenantId = _tenant.TenantId,
                    plan.ProjectId,
                    plan.Title,
                    plan.Goal,
                    plan.Scope,
                    plan.ProposedSteps,
                    plan.AffectedContext,
                    plan.RisksNotes,
                    plan.Status,
                    plan.LinkedFilePaths,
                    plan.LinkedCodeIndexEntryIds,
                    plan.LinkedSymbols,
                    plan.TicketId
                },
                cancellationToken: cancellationToken));

            return plan.Id;
        }
        else
        {
            const string insertSql = """
                INSERT INTO dbo.ProjectImplementationPlans 
                    (TenantId, ProjectId, TicketId, Title, Goal, Scope, ProposedSteps, AffectedContext, 
                     RisksNotes, Status, SourceChatMessageId, LinkedFilePaths, 
                     LinkedCodeIndexEntryIds, LinkedSymbols)
                OUTPUT inserted.Id
                VALUES 
                    (@TenantId, @ProjectId, @TicketId, @Title, @Goal, @Scope, @ProposedSteps, 
                     @AffectedContext, @RisksNotes, @Status, @SourceChatMessageId, 
                     @LinkedFilePaths, @LinkedCodeIndexEntryIds, @LinkedSymbols);
                """;

            return await connection.QuerySingleAsync<long>(new CommandDefinition(
                insertSql,
                new
                {
                    TenantId = _tenant.TenantId,
                    plan.ProjectId,
                    plan.Title,
                    plan.Goal,
                    plan.Scope,
                    plan.ProposedSteps,
                    plan.AffectedContext,
                    plan.RisksNotes,
                    plan.Status,
                    plan.SourceChatMessageId,
                    plan.LinkedFilePaths,
                    plan.LinkedCodeIndexEntryIds,
                    plan.LinkedSymbols,
                    plan.TicketId
                },
                cancellationToken: cancellationToken));
        }
    }

    public async Task<long> SaveDecisionAsync(ProjectDecision decision, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // Ownership guard: verify the project belongs to the current tenant.
        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownerSql,
            new { decision.ProjectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        if (owns == 0)
            throw new UnauthorizedAccessException(
                $"Project {decision.ProjectId} does not belong to tenant {_tenant.TenantId}.");

        if (decision.Id > 0)
        {
            // Update flow
            const string updateSql = """
                UPDATE dbo.ProjectDecisions 
                SET Title = @Title, Detail = @Detail, Reason = @Reason,
                    Category = @Category, Status = @Status,
                    LinkedFilePaths = @LinkedFilePaths,
                    LinkedCodeIndexEntryIds = @LinkedCodeIndexEntryIds,
                    LinkedSymbols = @LinkedSymbols
                WHERE Id = @Id AND TenantId = @TenantId AND ProjectId = @ProjectId;
                """;

            var rowsAffected = await connection.ExecuteAsync(new CommandDefinition(
                updateSql,
                new 
                { 
                    decision.Id,
                    TenantId = _tenant.TenantId,
                    decision.ProjectId,
                    decision.Title,
                    decision.Detail, 
                    decision.Reason,
                    decision.Category,
                    decision.Status,
                    decision.LinkedFilePaths,
                    decision.LinkedCodeIndexEntryIds,
                    decision.LinkedSymbols
                },
                cancellationToken: cancellationToken));

            if (rowsAffected == 0)
                throw new InvalidOperationException("Decision update failed or decision not found/not owned.");

            return decision.Id;
        }
        else
        {
            // Insert flow
            const string insertSql = """
                INSERT INTO dbo.ProjectDecisions 
                    (TenantId, ProjectId, Title, Detail, Reason, Category, Status,
                     SourceChatMessageId, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols)
                OUTPUT inserted.Id
                VALUES 
                    (@TenantId, @ProjectId, @Title, @Detail, @Reason, @Category, @Status,
                     @SourceChatMessageId, @LinkedFilePaths, @LinkedCodeIndexEntryIds, @LinkedSymbols);
                """;

            return await connection.QuerySingleAsync<long>(new CommandDefinition(
                insertSql,
                new
                {
                    TenantId = _tenant.TenantId,
                    decision.ProjectId,
                    decision.Title,
                    decision.Detail,
                    decision.Reason,
                    decision.Category,
                    decision.Status,
                    decision.SourceChatMessageId,
                    decision.LinkedFilePaths,
                    decision.LinkedCodeIndexEntryIds,
                    decision.LinkedSymbols
                },
                cancellationToken: cancellationToken));
        }
    }
}
