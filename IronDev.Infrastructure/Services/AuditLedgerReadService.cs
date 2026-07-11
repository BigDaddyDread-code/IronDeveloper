using Dapper;
using IronDev.Core.Audit;
using IronDev.Data;

namespace IronDev.Infrastructure.Services;

public sealed class SqlAuditLedgerReadService : IAuditLedgerReadService
{
    private readonly IDbConnectionFactory _connections;

    public SqlAuditLedgerReadService(IDbConnectionFactory connections) => _connections = connections;

    public async Task<AuditLedgerResponse> SearchAsync(
        int tenantId,
        int currentUserId,
        AuditLedgerQuery query,
        CancellationToken cancellationToken = default)
    {
        var issues = Validate(query);
        var take = Math.Clamp(query.Take <= 0 ? 100 : query.Take, 1, 250);
        if (issues.Count > 0)
        {
            return new AuditLedgerResponse
            {
                Status = "validation_error",
                Issues = issues,
                Take = take,
                Warnings = BoundaryWarnings()
            };
        }

        using var connection = _connections.CreateConnection();
        var rows = await connection.QueryAsync<AuditLedgerRow>(new CommandDefinition(
            Sql,
            new
            {
                TenantId = tenantId,
                CurrentUserId = currentUserId,
                query.ProjectId,
                query.WorkItemId,
                Actor = Normalize(query.Actor),
                ActorLike = Like(query.Actor),
                Event = Normalize(query.Event),
                EventLike = Like(query.Event),
                FromUtc = query.FromUtc?.UtcDateTime,
                ToUtc = query.ToUtc?.UtcDateTime,
                Take = take
            },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        var items = rows.Select(ToItem).ToArray();
        return new AuditLedgerResponse
        {
            Status = "ok",
            Items = items,
            ReturnedCount = items.Length,
            Take = take,
            Warnings = BoundaryWarnings()
        };
    }

    private static List<AuditLedgerIssue> Validate(AuditLedgerQuery query)
    {
        var issues = new List<AuditLedgerIssue>();
        if (query.FromUtc.HasValue && query.ToUtc.HasValue && query.FromUtc > query.ToUtc)
        {
            issues.Add(new AuditLedgerIssue
            {
                Code = "AuditLedgerDateRangeInvalid",
                Field = "fromUtc",
                Message = "fromUtc must be earlier than or equal to toUtc."
            });
        }

        if (query.Actor?.Length > 200)
        {
            issues.Add(new AuditLedgerIssue
            {
                Code = "AuditLedgerActorFilterTooLong",
                Field = "actor",
                Message = "Actor filter must be 200 characters or fewer."
            });
        }

        if (query.Event?.Length > 200)
        {
            issues.Add(new AuditLedgerIssue
            {
                Code = "AuditLedgerEventFilterTooLong",
                Field = "event",
                Message = "Event filter must be 200 characters or fewer."
            });
        }

        return issues;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Like(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"%{value.Trim()}%";

    private static IReadOnlyList<string> BoundaryWarnings() =>
    [
        "Audit ledger is read-only traceability.",
        "Rows do not approve, continue, apply, release, deploy, or grant authority.",
        "Raw payload JSON, source content, and private reasoning are not exposed."
    ];

    private static AuditLedgerItem ToItem(AuditLedgerRow row) =>
        new()
        {
            LedgerId = row.LedgerId,
            TimeUtc = new DateTimeOffset(DateTime.SpecifyKind(row.TimeUtc, DateTimeKind.Utc)),
            ProjectId = row.ProjectId,
            ProjectName = Safe(row.ProjectName, $"Project {row.ProjectId}"),
            WorkItemId = row.WorkItemId,
            WorkItemTitle = row.WorkItemTitle,
            Source = Safe(row.Source, "Audit"),
            ActorId = Safe(row.ActorId, "unknown"),
            ActorDisplayName = Safe(row.ActorDisplayName, Safe(row.ActorId, "Unknown actor")),
            Action = Safe(row.Action, "Recorded"),
            Outcome = Safe(row.Outcome, "Recorded"),
            Summary = Safe(row.Summary, "Audit event recorded."),
            CorrelationId = string.IsNullOrWhiteSpace(row.CorrelationId) ? null : row.CorrelationId,
            EvidenceLinks = string.IsNullOrWhiteSpace(row.EvidenceHref)
                ? []
                :
                [
                    new AuditLedgerEvidenceLink
                    {
                        Label = Safe(row.EvidenceLabel, "Evidence"),
                        Href = row.EvidenceHref
                    }
                ]
        };

    private static string Safe(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private const string Sql = """
        WITH Ledger AS
        (
            SELECT
                CONCAT(N'run-event:', CONVERT(nvarchar(36), re.EventId)) AS LedgerId,
                CAST(re.TimestampUtc AS datetime2) AS TimeUtc,
                p.TenantId,
                r.ProjectId,
                p.Name AS ProjectName,
                r.TicketId AS WorkItemId,
                t.Title AS WorkItemTitle,
                N'RunEvent' AS Source,
                COALESCE(NULLIF(actor.ActorId, N''), N'system') AS ActorId,
                COALESCE(NULLIF(JSON_VALUE(re.PayloadJson, '$.approvedByActorDisplayName'), N''), u.DisplayName, NULLIF(actor.ActorId, N''), N'System') AS ActorDisplayName,
                re.EventType AS Action,
                CASE
                    WHEN re.EventType LIKE N'%Refused%' THEN N'Refused'
                    WHEN re.EventType LIKE N'%Failed%' THEN N'Failed'
                    WHEN re.EventType LIKE N'%Blocked%' THEN N'Blocked'
                    WHEN re.EventType LIKE N'%Applied%' OR re.EventType LIKE N'%Completed%' OR re.EventType LIKE N'%Unblocked%' THEN N'Succeeded'
                    ELSE N'Recorded'
                END AS Outcome,
                LEFT(REPLACE(REPLACE(re.Message, CHAR(13), N' '), CHAR(10), N' '), 500) AS Summary,
                re.RunId AS CorrelationId,
                CASE WHEN r.ProjectId IS NULL OR r.TicketId IS NULL THEN NULL ELSE CONCAT(N'/projects/', r.ProjectId, N'/work-items/', r.TicketId) END AS EvidenceHref,
                N'Work Item run' AS EvidenceLabel
            FROM dbo.RunEvents re
            INNER JOIN dbo.Runs r ON r.RunId = re.RunId
            INNER JOIN dbo.Projects p ON p.Id = r.ProjectId
            LEFT JOIN dbo.ProjectTickets t ON t.Id = r.TicketId
            OUTER APPLY
            (
                SELECT COALESCE(
                    NULLIF(JSON_VALUE(re.PayloadJson, '$.requestedByUserId'), N''),
                    NULLIF(JSON_VALUE(re.PayloadJson, '$.approvedByActorId'), N''),
                    NULLIF(JSON_VALUE(re.PayloadJson, '$.actorUserId'), N''),
                    NULLIF(JSON_VALUE(re.PayloadJson, '$.userId'), N'')
                ) AS ActorId
            ) actor
            LEFT JOIN dbo.Users u ON u.Id = TRY_CONVERT(int, actor.ActorId)

            UNION ALL

            SELECT
                CONCAT(N'accepted-approval:', CONVERT(nvarchar(36), a.AcceptedApprovalId)) AS LedgerId,
                CAST(a.AcceptedAtUtc AS datetime2) AS TimeUtc,
                p.TenantId,
                r.ProjectId,
                p.Name AS ProjectName,
                r.TicketId AS WorkItemId,
                t.Title AS WorkItemTitle,
                N'AcceptedApproval' AS Source,
                a.ApprovedByActorId AS ActorId,
                COALESCE(a.ApprovedByActorDisplayName, u.DisplayName, a.ApprovedByActorId) AS ActorDisplayName,
                N'AcceptedApprovalRecorded' AS Action,
                a.ApprovalPurpose AS Outcome,
                CONCAT(N'Accepted approval recorded for ', a.CapabilityCode, N'.') AS Summary,
                a.CorrelationId AS CorrelationId,
                CONCAT(N'/governance/accepted-approvals?targetId=', a.ApprovalTargetId) AS EvidenceHref,
                N'Accepted approval' AS EvidenceLabel
            FROM governance.AcceptedApproval a
            INNER JOIN dbo.Runs r ON r.RunId = a.ApprovalTargetId
            INNER JOIN dbo.Projects p ON p.Id = r.ProjectId
            LEFT JOIN dbo.ProjectTickets t ON t.Id = r.TicketId
            LEFT JOIN dbo.Users u ON u.Id = TRY_CONVERT(int, a.ApprovedByActorId)

            UNION ALL

            SELECT
                CONCAT(N'work-item-activity:', CONVERT(nvarchar(30), activity.Id)) AS LedgerId,
                CAST(activity.CreatedUtc AS datetime2) AS TimeUtc,
                activity.TenantId,
                activity.ProjectId,
                p.Name AS ProjectName,
                activity.WorkItemId,
                t.Title AS WorkItemTitle,
                N'WorkItemActivity' AS Source,
                COALESCE(CONVERT(nvarchar(30), activity.ActorUserId), N'system') AS ActorId,
                COALESCE(actorUser.DisplayName, CONVERT(nvarchar(30), activity.ActorUserId), N'System') AS ActorDisplayName,
                activity.EventKind AS Action,
                N'Recorded' AS Outcome,
                activity.Summary AS Summary,
                CONCAT(N'work-item:', activity.WorkItemId) AS CorrelationId,
                CONCAT(N'/projects/', activity.ProjectId, N'/work-items/', activity.WorkItemId) AS EvidenceHref,
                N'Work Item' AS EvidenceLabel
            FROM dbo.ProjectWorkItemActivity activity
            INNER JOIN dbo.Projects p ON p.Id = activity.ProjectId AND p.TenantId = activity.TenantId
            INNER JOIN dbo.ProjectTickets t ON t.Id = activity.WorkItemId
            LEFT JOIN dbo.Users actorUser ON actorUser.Id = activity.ActorUserId

            UNION ALL

            SELECT
                CONCAT(N'chat-message:', CONVERT(nvarchar(30), m.Id)) AS LedgerId,
                CAST(m.CreatedDate AS datetime2) AS TimeUtc,
                m.TenantId,
                m.ProjectId,
                p.Name AS ProjectName,
                NULL AS WorkItemId,
                NULL AS WorkItemTitle,
                N'Chat' AS Source,
                m.Role AS ActorId,
                CASE WHEN m.Role = N'assistant' THEN N'IronDev' ELSE N'User' END AS ActorDisplayName,
                N'ChatMessageRecorded' AS Action,
                N'Recorded' AS Outcome,
                CONCAT(N'Chat ', m.Role, N' message recorded in session ', CONVERT(nvarchar(30), m.ChatSessionId), N'.') AS Summary,
                CONCAT(N'chat-session:', m.ChatSessionId) AS CorrelationId,
                CONCAT(N'/projects/', m.ProjectId, N'/chat/sessions/', m.ChatSessionId) AS EvidenceHref,
                N'Chat session' AS EvidenceLabel
            FROM dbo.ChatMessages m
            INNER JOIN dbo.Projects p ON p.Id = m.ProjectId AND p.TenantId = m.TenantId

            UNION ALL

            SELECT
                CONCAT(N'document:', CONVERT(nvarchar(30), d.Id)) AS LedgerId,
                CAST(d.CreatedAtUtc AS datetime2) AS TimeUtc,
                d.TenantId,
                d.ProjectId,
                p.Name AS ProjectName,
                NULL AS WorkItemId,
                NULL AS WorkItemTitle,
                N'Document' AS Source,
                COALESCE(NULLIF(d.CreatedBy, N''), N'unknown') AS ActorId,
                COALESCE(creator.DisplayName, NULLIF(d.CreatedBy, N''), N'Unknown actor') AS ActorDisplayName,
                N'DocumentCreated' AS Action,
                d.ProcessingStatus AS Outcome,
                CONCAT(N'Document "', d.Title, N'" created.') AS Summary,
                CONCAT(N'document:', d.Id) AS CorrelationId,
                CONCAT(N'/projects/', d.ProjectId, N'/library/documents/', d.Id) AS EvidenceHref,
                N'Document' AS EvidenceLabel
            FROM dbo.ProjectDocuments d
            INNER JOIN dbo.Projects p ON p.Id = d.ProjectId AND p.TenantId = d.TenantId
            LEFT JOIN dbo.Users creator ON creator.Id = TRY_CONVERT(int, d.CreatedBy) OR creator.Email = d.CreatedBy

            UNION ALL

            SELECT
                CONCAT(N'document-version:', CONVERT(nvarchar(30), v.Id)) AS LedgerId,
                CAST(v.CreatedAtUtc AS datetime2) AS TimeUtc,
                d.TenantId,
                d.ProjectId,
                p.Name AS ProjectName,
                NULL AS WorkItemId,
                NULL AS WorkItemTitle,
                N'DocumentVersion' AS Source,
                COALESCE(NULLIF(v.CreatedBy, N''), N'unknown') AS ActorId,
                COALESCE(creator.DisplayName, NULLIF(v.CreatedBy, N''), N'Unknown actor') AS ActorDisplayName,
                N'DocumentVersionSaved' AS Action,
                v.Status AS Outcome,
                CONCAT(N'Document "', d.Title, N'" version ', v.VersionMajor, N'.', v.VersionMinor, N' saved.') AS Summary,
                CONCAT(N'document-version:', v.Id) AS CorrelationId,
                CONCAT(N'/projects/', d.ProjectId, N'/library/documents/', d.Id, N'/versions/', v.Id) AS EvidenceHref,
                N'Document version' AS EvidenceLabel
            FROM dbo.ProjectDocumentVersions v
            INNER JOIN dbo.ProjectDocuments d ON d.Id = v.DocumentId
            INNER JOIN dbo.Projects p ON p.Id = d.ProjectId AND p.TenantId = d.TenantId
            LEFT JOIN dbo.Users creator ON creator.Id = TRY_CONVERT(int, v.CreatedBy) OR creator.Email = v.CreatedBy

            UNION ALL

            SELECT
                CONCAT(N'project-member-added:', CONVERT(nvarchar(30), pm.Id)) AS LedgerId,
                CAST(pm.AddedUtc AS datetime2) AS TimeUtc,
                pm.TenantId,
                pm.ProjectId,
                p.Name AS ProjectName,
                NULL AS WorkItemId,
                NULL AS WorkItemTitle,
                N'ProjectMembership' AS Source,
                CONVERT(nvarchar(30), pm.AddedByUserId) AS ActorId,
                COALESCE(actorUser.DisplayName, CONVERT(nvarchar(30), pm.AddedByUserId)) AS ActorDisplayName,
                N'ProjectMemberAdded' AS Action,
                pm.ProjectRole AS Outcome,
                CONCAT(COALESCE(targetUser.DisplayName, targetUser.Email, CONVERT(nvarchar(30), pm.UserId)), N' joined as ', pm.ProjectRole, N'.') AS Summary,
                CONCAT(N'project:', pm.ProjectId) AS CorrelationId,
                CONCAT(N'/projects/', pm.ProjectId, N'/library/members') AS EvidenceHref,
                N'Project members' AS EvidenceLabel
            FROM dbo.ProjectMembers pm
            INNER JOIN dbo.Projects p ON p.Id = pm.ProjectId AND p.TenantId = pm.TenantId
            LEFT JOIN dbo.Users actorUser ON actorUser.Id = pm.AddedByUserId
            LEFT JOIN dbo.Users targetUser ON targetUser.Id = pm.UserId

            UNION ALL

            SELECT
                CONCAT(N'project-member-removed:', CONVERT(nvarchar(30), pm.Id)) AS LedgerId,
                CAST(pm.RemovedUtc AS datetime2) AS TimeUtc,
                pm.TenantId,
                pm.ProjectId,
                p.Name AS ProjectName,
                NULL AS WorkItemId,
                NULL AS WorkItemTitle,
                N'ProjectMembership' AS Source,
                CONVERT(nvarchar(30), pm.RemovedByUserId) AS ActorId,
                COALESCE(actorUser.DisplayName, CONVERT(nvarchar(30), pm.RemovedByUserId)) AS ActorDisplayName,
                N'ProjectMemberRemoved' AS Action,
                N'Removed' AS Outcome,
                CONCAT(COALESCE(targetUser.DisplayName, targetUser.Email, CONVERT(nvarchar(30), pm.UserId)), N' was removed from the project.') AS Summary,
                CONCAT(N'project:', pm.ProjectId) AS CorrelationId,
                CONCAT(N'/projects/', pm.ProjectId, N'/library/members') AS EvidenceHref,
                N'Project members' AS EvidenceLabel
            FROM dbo.ProjectMembers pm
            INNER JOIN dbo.Projects p ON p.Id = pm.ProjectId AND p.TenantId = pm.TenantId
            LEFT JOIN dbo.Users actorUser ON actorUser.Id = pm.RemovedByUserId
            LEFT JOIN dbo.Users targetUser ON targetUser.Id = pm.UserId
            WHERE pm.RemovedUtc IS NOT NULL
        )
        SELECT TOP (@Take)
            l.LedgerId,
            l.TimeUtc,
            l.ProjectId,
            l.ProjectName,
            l.WorkItemId,
            l.WorkItemTitle,
            l.Source,
            l.ActorId,
            l.ActorDisplayName,
            l.Action,
            l.Outcome,
            l.Summary,
            l.CorrelationId,
            l.EvidenceHref,
            l.EvidenceLabel
        FROM Ledger l
        INNER JOIN dbo.ProjectMembers access
            ON access.TenantId = l.TenantId
            AND access.ProjectId = l.ProjectId
            AND access.UserId = @CurrentUserId
            AND access.Status = N'Active'
        WHERE l.TenantId = @TenantId
          AND (@ProjectId IS NULL OR l.ProjectId = @ProjectId)
          AND (@WorkItemId IS NULL OR l.WorkItemId = @WorkItemId)
          AND (@Actor IS NULL OR l.ActorId LIKE @ActorLike OR l.ActorDisplayName LIKE @ActorLike)
          AND (@Event IS NULL OR l.Action LIKE @EventLike OR l.Source LIKE @EventLike OR l.Outcome LIKE @EventLike)
          AND (@FromUtc IS NULL OR l.TimeUtc >= @FromUtc)
          AND (@ToUtc IS NULL OR l.TimeUtc <= @ToUtc)
        ORDER BY l.TimeUtc DESC, l.LedgerId DESC;
        """;

    private sealed class AuditLedgerRow
    {
        public string LedgerId { get; init; } = string.Empty;
        public DateTime TimeUtc { get; init; }
        public int ProjectId { get; init; }
        public string ProjectName { get; init; } = string.Empty;
        public long? WorkItemId { get; init; }
        public string? WorkItemTitle { get; init; }
        public string Source { get; init; } = string.Empty;
        public string ActorId { get; init; } = string.Empty;
        public string ActorDisplayName { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Outcome { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string? CorrelationId { get; init; }
        public string? EvidenceHref { get; init; }
        public string? EvidenceLabel { get; init; }
    }
}
