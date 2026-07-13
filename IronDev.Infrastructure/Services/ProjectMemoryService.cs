using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using IronDev.Core.Auth;
using IronDev.Core.Interfaces;
using IronDev.Core.Models;
using IronDev.Data;
using IronDev.Data.Models;
using Microsoft.Data.SqlClient;

namespace IronDev.Services;

public interface IProjectMemoryService
{
    Task<ProjectSummary?> GetLatestSummaryAsync(int projectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectDecision>> GetRecentDecisionsAsync(int projectId, int take = 10, CancellationToken cancellationToken = default);
    Task<ProjectDecision?> GetDecisionByIdAsync(long decisionId, CancellationToken cancellationToken = default);
    Task<long> SaveSummaryAsync(ProjectSummary summary, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectContextDocument>> GetContextDocumentsAsync(
        int projectId,
        string? documentType = null,
        string? authorityLevel = null,
        string? status = "Active",
        int take = 100,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProjectContextDocument>> GetRelevantContextDocumentsAsync(int projectId, string query, int take = 20, CancellationToken cancellationToken = default);
    Task<ProjectContextDocument?> GetContextDocumentByIdAsync(long documentId, CancellationToken cancellationToken = default);
    Task<long> SaveContextDocumentAsync(ProjectContextDocument document, CancellationToken cancellationToken = default);
    Task<bool> ArchiveContextDocumentAsync(long documentId, CancellationToken cancellationToken = default);
    Task<ProjectObservableState?> GetObservableStateAsync(int projectId, CancellationToken cancellationToken = default);
    Task SaveObservableStateAsync(ProjectObservableState state, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<ProjectImplementationPlan>> GetRecentPlansAsync(int projectId, int take = 10, CancellationToken cancellationToken = default);
    Task<ProjectImplementationPlan?> GetPlanByIdAsync(long planId, CancellationToken cancellationToken = default);
    Task<ProjectImplementationPlan?> GetPlanByTicketIdAsync(long ticketId, CancellationToken cancellationToken = default);
    Task<long> SavePlanAsync(ProjectImplementationPlan plan, CancellationToken cancellationToken = default);

    Task<long> SaveDecisionAsync(ProjectDecision decision, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<ProjectRule>> GetProjectRulesAsync(int projectId, CancellationToken cancellationToken = default);
    Task<long> SaveProjectRuleAsync(ProjectRule rule, CancellationToken cancellationToken = default);
}

public sealed class ProjectMemoryService : IProjectMemoryService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentTenantContext _tenant;
    private readonly IArtifactSourceReferenceService _referenceService;

    public ProjectMemoryService(
        IDbConnectionFactory connectionFactory, 
        ICurrentTenantContext tenant,
        IArtifactSourceReferenceService referenceService)
    {
        _connectionFactory = connectionFactory;
        _tenant = tenant;
        _referenceService = referenceService;
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

    public async Task<IReadOnlyList<ProjectContextDocument>> GetContextDocumentsAsync(
        int projectId,
        string? documentType = null,
        string? authorityLevel = null,
        string? status = "Active",
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, DocumentType, AuthorityLevel, Status, Title, Content,
                Summary, Tags, AppliesToCapability, AppliesToArea, Source, SupersedesDocumentId,
                SourceChatMessageId, CreatedDate, UpdatedDate
            FROM dbo.ProjectContextDocuments
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND (@DocumentType IS NULL OR DocumentType = @DocumentType)
              AND (@AuthorityLevel IS NULL OR AuthorityLevel = @AuthorityLevel)
              AND (@Status IS NULL OR Status = @Status)
            ORDER BY
                CASE AuthorityLevel
                    WHEN 'Binding' THEN 1
                    WHEN 'StrongGuidance' THEN 2
                    WHEN 'ObservedFact' THEN 3
                    WHEN 'Pending' THEN 4
                    WHEN 'ContextOnly' THEN 5
                    ELSE 6
                END,
                UpdatedDate DESC,
                CreatedDate DESC;
            """;

        var rows = await connection.QueryAsync<ProjectContextDocument>(new CommandDefinition(
            sql,
            new
            {
                TenantId = _tenant.TenantId,
                ProjectId = projectId,
                DocumentType = string.IsNullOrWhiteSpace(documentType) ? null : documentType,
                AuthorityLevel = string.IsNullOrWhiteSpace(authorityLevel) ? null : authorityLevel,
                Status = string.IsNullOrWhiteSpace(status) ? null : status,
                Take = take
            },
            cancellationToken: cancellationToken));

        return rows.ToList();
    }

    public async Task<IReadOnlyList<ProjectContextDocument>> GetRelevantContextDocumentsAsync(int projectId, string query, int take = 20, CancellationToken cancellationToken = default)
    {
        var terms = ExtractSearchTerms(query).Take(8).ToList();
        if (terms.Count == 0)
            return await GetContextDocumentsAsync(projectId, take: take, cancellationToken: cancellationToken);

        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT TOP (@Take)
                Id, TenantId, ProjectId, DocumentType, AuthorityLevel, Status, Title, Content,
                Summary, Tags, AppliesToCapability, AppliesToArea, Source, SupersedesDocumentId,
                SourceChatMessageId, CreatedDate, UpdatedDate
            FROM dbo.ProjectContextDocuments
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
              AND Status NOT IN ('Archived', 'Superseded', 'Processing', 'ProcessingFailed')
              AND (
                  Title LIKE @Pattern OR Content LIKE @Pattern OR Summary LIKE @Pattern
                  OR Tags LIKE @Pattern OR AppliesToCapability LIKE @Pattern OR AppliesToArea LIKE @Pattern
              )
            ORDER BY
                CASE AuthorityLevel
                    WHEN 'Binding' THEN 0
                    WHEN 'StrongGuidance' THEN 1
                    WHEN 'ObservedFact' THEN 2
                    WHEN 'Pending' THEN 3
                    ELSE 4
                END,
                CASE DocumentType
                    WHEN 'ArchitectureDecision' THEN 0
                    WHEN 'ProjectStandard' THEN 1
                    WHEN 'Constraint' THEN 2
                    WHEN 'ProjectFact' THEN 3
                    WHEN 'OpenQuestion' THEN 4
                    ELSE 5
                END,
                UpdatedDate DESC,
                CreatedDate DESC;
            """;

        var documents = new List<ProjectContextDocument>();
        foreach (var term in terms)
        {
            var rows = await connection.QueryAsync<ProjectContextDocument>(new CommandDefinition(
                sql,
                new { TenantId = _tenant.TenantId, ProjectId = projectId, Take = take, Pattern = $"%{term}%" },
                cancellationToken: cancellationToken));
            documents.AddRange(rows);
        }

        return documents
            .GroupBy(d => d.Id)
            .Select(g => g.First())
            .Take(take)
            .ToList();
    }

    public async Task<ProjectContextDocument?> GetContextDocumentByIdAsync(long documentId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                Id, TenantId, ProjectId, DocumentType, AuthorityLevel, Status, Title, Content,
                Summary, Tags, AppliesToCapability, AppliesToArea, Source, SupersedesDocumentId,
                SourceChatMessageId, CreatedDate, UpdatedDate
            FROM dbo.ProjectContextDocuments
            WHERE Id = @DocumentId
              AND TenantId = @TenantId;
            """;

        return await connection.QuerySingleOrDefaultAsync<ProjectContextDocument>(new CommandDefinition(
            sql,
            new { DocumentId = documentId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));
    }

    public async Task<long> SaveContextDocumentAsync(ProjectContextDocument document, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        await EnsureProjectOwnershipAsync(connection, document.ProjectId, cancellationToken);

        if (document.Id > 0)
        {
            const string updateSql = """
                UPDATE dbo.ProjectContextDocuments
                SET DocumentType = @DocumentType,
                    AuthorityLevel = @AuthorityLevel,
                    Status = @Status,
                    Title = @Title,
                    Content = @Content,
                    Summary = @Summary,
                    Tags = @Tags,
                    AppliesToCapability = @AppliesToCapability,
                    AppliesToArea = @AppliesToArea,
                    Source = @Source,
                    SupersedesDocumentId = @SupersedesDocumentId,
                    SourceChatMessageId = @SourceChatMessageId,
                    UpdatedDate = SYSUTCDATETIME()
                WHERE Id = @Id
                  AND TenantId = @TenantId
                  AND ProjectId = @ProjectId;
                """;

            var rowsAffected = await connection.ExecuteAsync(new CommandDefinition(
                updateSql,
                new
                {
                    document.Id,
                    TenantId = _tenant.TenantId,
                    document.ProjectId,
                    document.DocumentType,
                    document.AuthorityLevel,
                    document.Status,
                    document.Title,
                    document.Content,
                    document.Summary,
                    document.Tags,
                    document.AppliesToCapability,
                    document.AppliesToArea,
                    document.Source,
                    document.SupersedesDocumentId,
                    document.SourceChatMessageId
                },
                cancellationToken: cancellationToken));

            if (rowsAffected == 0)
                throw new InvalidOperationException("Context document update failed or document not found/not owned.");

            return document.Id;
        }

        const string insertSql = """
            INSERT INTO dbo.ProjectContextDocuments
                (TenantId, ProjectId, DocumentType, AuthorityLevel, Status, Title, Content, Summary,
                 Tags, AppliesToCapability, AppliesToArea, Source, SupersedesDocumentId, SourceChatMessageId, UpdatedDate)
            OUTPUT inserted.Id
            VALUES
                (@TenantId, @ProjectId, @DocumentType, @AuthorityLevel, @Status, @Title, @Content, @Summary,
                 @Tags, @AppliesToCapability, @AppliesToArea, @Source, @SupersedesDocumentId, @SourceChatMessageId, SYSUTCDATETIME());
            """;

        return await connection.QuerySingleAsync<long>(new CommandDefinition(
            insertSql,
            new
            {
                TenantId = _tenant.TenantId,
                document.ProjectId,
                document.DocumentType,
                document.AuthorityLevel,
                document.Status,
                document.Title,
                document.Content,
                document.Summary,
                document.Tags,
                document.AppliesToCapability,
                document.AppliesToArea,
                document.Source,
                document.SupersedesDocumentId,
                document.SourceChatMessageId
            },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> ArchiveContextDocumentAsync(long documentId, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        const string sql = """
            UPDATE dbo.ProjectContextDocuments
            SET Status = 'Archived',
                UpdatedDate = SYSUTCDATETIME()
            WHERE Id = @DocumentId
              AND TenantId = @TenantId;
            """;

        var rowsAffected = await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new { DocumentId = documentId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        return rowsAffected > 0;
    }

    public async Task<ProjectObservableState?> GetObservableStateAsync(int projectId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT TOP (1)
                Id, TenantId, ProjectId, ActiveCapability, ActiveMilestone, CurrentFocus,
                BuildReadiness, IndexStatus, BuilderMode, OpenBlockers, LastRecommendation,
                CurrentTargetPath, KnownCurrentGaps, SnapshotJson, UpdatedDate
            FROM dbo.ProjectObservableStates
            WHERE TenantId = @TenantId AND ProjectId = @ProjectId
            ORDER BY UpdatedDate DESC;
            """;

        using var connection = _connectionFactory.CreateConnection();
        try
        {
            return await connection.QuerySingleOrDefaultAsync<ProjectObservableState>(new CommandDefinition(
                sql,
                new { TenantId = _tenant.TenantId, ProjectId = projectId },
                cancellationToken: cancellationToken));
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            return null;
        }
    }

    public async Task SaveObservableStateAsync(ProjectObservableState state, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await EnsureProjectOwnershipAsync(connection, state.ProjectId, cancellationToken);

        const string sql = """
            IF EXISTS (SELECT 1 FROM dbo.ProjectObservableStates WHERE TenantId = @TenantId AND ProjectId = @ProjectId)
            BEGIN
                UPDATE dbo.ProjectObservableStates
                SET ActiveCapability = @ActiveCapability,
                    ActiveMilestone = @ActiveMilestone,
                    CurrentFocus = @CurrentFocus,
                    BuildReadiness = @BuildReadiness,
                    IndexStatus = @IndexStatus,
                    BuilderMode = @BuilderMode,
                    OpenBlockers = @OpenBlockers,
                    LastRecommendation = @LastRecommendation,
                    CurrentTargetPath = @CurrentTargetPath,
                    KnownCurrentGaps = @KnownCurrentGaps,
                    SnapshotJson = @SnapshotJson,
                    UpdatedDate = SYSUTCDATETIME()
                WHERE TenantId = @TenantId AND ProjectId = @ProjectId;
            END
            ELSE
            BEGIN
                INSERT INTO dbo.ProjectObservableStates
                    (TenantId, ProjectId, ActiveCapability, ActiveMilestone, CurrentFocus, BuildReadiness,
                     IndexStatus, BuilderMode, OpenBlockers, LastRecommendation, CurrentTargetPath,
                     KnownCurrentGaps, SnapshotJson)
                VALUES
                    (@TenantId, @ProjectId, @ActiveCapability, @ActiveMilestone, @CurrentFocus, @BuildReadiness,
                     @IndexStatus, @BuilderMode, @OpenBlockers, @LastRecommendation, @CurrentTargetPath,
                     @KnownCurrentGaps, @SnapshotJson);
            END
            """;

        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            new
            {
                TenantId = _tenant.TenantId,
                state.ProjectId,
                state.ActiveCapability,
                state.ActiveMilestone,
                state.CurrentFocus,
                state.BuildReadiness,
                state.IndexStatus,
                state.BuilderMode,
                state.OpenBlockers,
                state.LastRecommendation,
                state.CurrentTargetPath,
                state.KnownCurrentGaps,
                state.SnapshotJson
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

            var savedId = await connection.QuerySingleAsync<long>(new CommandDefinition(
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

            // Record traceability references
            if (plan.SourceChatMessageId.HasValue)
            {
                await _referenceService.RecordReferenceAsync(new ArtifactSourceReference
                {
                    TenantId = _tenant.TenantId,
                    ProjectId = plan.ProjectId,
                    ArtifactType = "ImplementationPlan",
                    ArtifactId = savedId,
                    SourceType = "ChatMessage",
                    SourceId = plan.SourceChatMessageId.Value,
                    ReferenceType = "CreatedFrom",
                    Summary = $"Plan '{plan.Title}' created from chat message."
                }, cancellationToken);
            }

            if (plan.TicketId.HasValue)
            {
                await _referenceService.RecordReferenceAsync(new ArtifactSourceReference
                {
                    TenantId = _tenant.TenantId,
                    ProjectId = plan.ProjectId,
                    ArtifactType = "ImplementationPlan",
                    ArtifactId = savedId,
                    SourceType = "Ticket",
                    SourceId = plan.TicketId.Value,
                    ReferenceType = "BasedOn",
                    Summary = $"Plan '{plan.Title}' based on ticket #{plan.TicketId}."
                }, cancellationToken);
            }

            return savedId;
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
            // Insert or Update by Title flow (Upsert logic for tests/UX)
            const string upsertSql = """
                IF EXISTS (SELECT 1 FROM dbo.ProjectDecisions WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND Title = @Title)
                BEGIN
                    UPDATE dbo.ProjectDecisions 
                    SET Detail = @Detail, Reason = @Reason, Category = @Category, Status = @Status,
                        LinkedFilePaths = @LinkedFilePaths, LinkedCodeIndexEntryIds = @LinkedCodeIndexEntryIds, LinkedSymbols = @LinkedSymbols
                    WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND Title = @Title;
                    
                    SELECT Id FROM dbo.ProjectDecisions WHERE TenantId = @TenantId AND ProjectId = @ProjectId AND Title = @Title;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.ProjectDecisions 
                        (TenantId, ProjectId, Title, Detail, Reason, Category, Status,
                         SourceChatMessageId, LinkedFilePaths, LinkedCodeIndexEntryIds, LinkedSymbols)
                    OUTPUT inserted.Id
                    VALUES 
                        (@TenantId, @ProjectId, @Title, @Detail, @Reason, @Category, @Status,
                         @SourceChatMessageId, @LinkedFilePaths, @LinkedCodeIndexEntryIds, @LinkedSymbols);
                END
                """;

            var savedId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                upsertSql,
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

            // Record traceability reference
            if (decision.SourceChatMessageId.HasValue)
            {
                await _referenceService.RecordReferenceAsync(new ArtifactSourceReference
                {
                    TenantId = _tenant.TenantId,
                    ProjectId = decision.ProjectId,
                    ArtifactType = "Decision",
                    ArtifactId = savedId,
                    SourceType = "ChatMessage",
                    SourceId = decision.SourceChatMessageId.Value,
                    ReferenceType = "CreatedFrom",
                    Summary = $"Decision '{decision.Title}' created from chat message."
                }, cancellationToken);
            }

            return savedId;
        }
    }

    public async Task<IReadOnlyList<ProjectRule>> GetProjectRulesAsync(int projectId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT 
                Id, TenantId, ProjectId, Name, Type, Description, 
                EnforcementLevel, AppliesTo, ValidationHint, CreatedDate, UpdatedDate
            FROM dbo.ProjectRules
            WHERE TenantId = @TenantId
              AND ProjectId = @ProjectId
            ORDER BY CreatedDate DESC;
            """;

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            var rows = await connection.QueryAsync<ProjectRule>(new CommandDefinition(
                sql,
                new { TenantId = _tenant.TenantId, ProjectId = projectId },
                cancellationToken: cancellationToken));

            return rows.ToList();
        }
        catch (SqlException ex) when (ex.Number == 208 || ex.Message.Contains("dbo.ProjectRules", StringComparison.OrdinalIgnoreCase))
        {
            // dbo.ProjectRules table has not been migrated yet — return empty.
            // This is safe: context building continues without rules.
            System.Diagnostics.Trace.WriteLine(
                $"[ProjectMemoryService] dbo.ProjectRules not found (SQL {ex.Number}); returning empty rule list.");
            return Array.Empty<ProjectRule>();
        }
    }

    public async Task<long> SaveProjectRuleAsync(ProjectRule rule, CancellationToken cancellationToken = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // Ownership guard
        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownerSql,
            new { rule.ProjectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        if (owns == 0)
            throw new UnauthorizedAccessException($"Project {rule.ProjectId} does not belong to tenant {_tenant.TenantId}.");

        if (rule.Id > 0)
        {
            const string updateSql = """
                UPDATE dbo.ProjectRules 
                SET Name = @Name, Type = @Type, Description = @Description, 
                    EnforcementLevel = @EnforcementLevel, AppliesTo = @AppliesTo, 
                    ValidationHint = @ValidationHint,
                    UpdatedDate = SYSUTCDATETIME()
                WHERE Id = @Id AND TenantId = @TenantId AND ProjectId = @ProjectId;
                """;

            await connection.ExecuteAsync(new CommandDefinition(
                updateSql,
                new 
                { 
                    rule.Id,
                    TenantId = _tenant.TenantId,
                    rule.ProjectId,
                    rule.Name,
                    rule.Type,
                    rule.Description,
                    rule.EnforcementLevel,
                    rule.AppliesTo,
                    rule.ValidationHint
                },
                cancellationToken: cancellationToken));

            return rule.Id;
        }
        else
        {
            const string insertSql = """
                INSERT INTO dbo.ProjectRules 
                    (TenantId, ProjectId, Name, Type, Description, 
                     EnforcementLevel, AppliesTo, ValidationHint)
                OUTPUT inserted.Id
                VALUES 
                    (@TenantId, @ProjectId, @Name, @Type, @Description, 
                     @EnforcementLevel, @AppliesTo, @ValidationHint);
                """;

            return await connection.QuerySingleAsync<long>(new CommandDefinition(
                insertSql,
                new
                {
                    TenantId = _tenant.TenantId,
                    rule.ProjectId,
                    rule.Name,
                    rule.Type,
                    rule.Description,
                    rule.EnforcementLevel,
                    rule.AppliesTo,
                    rule.ValidationHint
                },
            cancellationToken: cancellationToken));
        }
    }

    private async Task EnsureProjectOwnershipAsync(System.Data.IDbConnection connection, int projectId, CancellationToken cancellationToken)
    {
        const string ownerSql = "SELECT COUNT(1) FROM dbo.Projects WHERE Id = @ProjectId AND TenantId = @TenantId";
        var owns = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            ownerSql,
            new { ProjectId = projectId, TenantId = _tenant.TenantId },
            cancellationToken: cancellationToken));

        if (owns == 0)
            throw new UnauthorizedAccessException($"Project {projectId} does not belong to tenant {_tenant.TenantId}.");
    }

    private static IEnumerable<string> ExtractSearchTerms(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var terms = text
            .Split([' ', '\t', '\r', '\n', ',', '.', ';', ':', '/', '\\', '-', '_', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var term in terms)
            yield return term;
    }
}
