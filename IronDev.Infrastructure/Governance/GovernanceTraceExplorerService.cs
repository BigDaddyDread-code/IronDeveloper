using System.Data;
using Dapper;
using IronDev.Core.Governance;
using IronDev.Data;

namespace IronDev.Infrastructure.Governance;

public sealed class GovernanceTraceExplorerService : IGovernanceTraceExplorerService
{
    private const string GetProcedure = "governance.GetGovernanceEvent";
    private const string ListForProjectProcedure = "governance.ListGovernanceEventsForProject";
    private const string ListForCorrelationProcedure = "governance.ListGovernanceEventsForCorrelation";
    private const string ListCausedByProcedure = "governance.ListGovernanceEventsCausedBy";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly GovernanceTraceExplorerValidator _validator;

    public GovernanceTraceExplorerService(IDbConnectionFactory connectionFactory)
        : this(connectionFactory, new GovernanceTraceExplorerValidator())
    {
    }

    internal GovernanceTraceExplorerService(IDbConnectionFactory connectionFactory, GovernanceTraceExplorerValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<GovernanceTraceListResponse> SearchAsync(
        GovernanceTraceQuery query,
        CancellationToken cancellationToken = default)
    {
        var issues = _validator.ValidateQuery(query);
        if (issues.Count > 0)
            return InvalidList(issues);

        var normalized = Normalize(query);
        var rows = await LoadRowsAsync(normalized, cancellationToken);
        var filtered = ApplyFilters(rows, normalized)
            .OrderByDescending(row => row.CreatedUtc)
            .ThenByDescending(row => row.EventId)
            .Take(normalized.Take)
            .Select(ToSummary)
            .ToArray();

        return new GovernanceTraceListResponse
        {
            Status = filtered.Length == 0 ? GovernanceTraceExplorerStatus.NoTraceFound : GovernanceTraceExplorerStatus.TraceListReturned,
            Traces = filtered,
            Issues = [],
            BoundaryWarnings = GovernanceTraceExplorerBoundaries.Warnings
        };
    }

    public async Task<GovernanceTraceDetailResponse> GetByTraceIdAsync(
        string traceId,
        CancellationToken cancellationToken = default)
    {
        var issues = _validator.ValidateTraceId(traceId);
        if (issues.Count > 0)
            return InvalidDetail(issues);

        using var connection = _connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<GovernanceTraceEventRow>(
            new CommandDefinition(
                GetProcedure,
                new { EventId = Guid.Parse(traceId) },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        if (row is null)
            return NotFoundDetail();

        var timeline = await BuildTimelineAsync(row, cancellationToken);
        return FoundDetail(row, timeline);
    }

    public async Task<GovernanceTraceDetailResponse> GetByCorrelationIdAsync(
        string correlationId,
        string projectReferenceId = "",
        CancellationToken cancellationToken = default)
    {
        var issues = _validator.ValidateCorrelationId(correlationId);
        if (!string.IsNullOrWhiteSpace(projectReferenceId) && !Guid.TryParse(projectReferenceId, out _))
        {
            issues = issues.Concat([
                GovernanceTraceExplorerValidator.Issue(
                    GovernanceTraceExplorerIssueKind.InvalidProjectReferenceId,
                    nameof(projectReferenceId),
                    "projectReferenceId must be a GUID.")
            ]).ToArray();
        }

        if (GovernanceTraceExplorerValidator.ContainsUnsafeText(projectReferenceId))
        {
            issues = issues.Concat([
                GovernanceTraceExplorerValidator.Issue(
                    GovernanceTraceExplorerIssueKind.UnsafeQueryText,
                    nameof(projectReferenceId),
                    "Governance trace explorer query contains unsupported trace text.")
            ]).ToArray();
        }

        if (issues.Count > 0)
            return InvalidDetail(issues);

        var query = new GovernanceTraceQuery
        {
            ProjectReferenceId = projectReferenceId,
            CorrelationId = correlationId,
            IncludeRelated = true
        };
        var list = await SearchAsync(query, cancellationToken);
        return DetailFromList(list);
    }

    public async Task<GovernanceTraceDetailResponse> GetByWorkflowRunIdAsync(
        string workflowRunId,
        string projectReferenceId = "",
        CancellationToken cancellationToken = default)
    {
        var issues = _validator.ValidateWorkflowRunId(workflowRunId, projectReferenceId);
        if (issues.Count > 0)
            return InvalidDetail(issues);

        var list = await SearchAsync(new GovernanceTraceQuery
        {
            ProjectReferenceId = projectReferenceId,
            WorkflowRunId = workflowRunId,
            IncludeRelated = true
        }, cancellationToken);

        return DetailFromList(list);
    }

    private async Task<IReadOnlyList<GovernanceTraceEventRow>> LoadRowsAsync(GovernanceTraceQuery query, CancellationToken cancellationToken)
    {
        using var connection = _connectionFactory.CreateConnection();
        var take = Math.Clamp(query.Take, 1, GovernanceTraceExplorerValidator.MaxTake);

        if (Guid.TryParse(query.CorrelationId, out var correlationId))
        {
            var rows = await connection.QueryAsync<GovernanceTraceEventRow>(
                new CommandDefinition(
                    ListForCorrelationProcedure,
                    new { CorrelationId = correlationId, Take = take },
                    commandType: CommandType.StoredProcedure,
                    cancellationToken: cancellationToken));
            return rows.ToArray();
        }

        if (Guid.TryParse(query.CausationId, out var causationId))
        {
            var rows = await connection.QueryAsync<GovernanceTraceEventRow>(
                new CommandDefinition(
                    ListCausedByProcedure,
                    new { CausationId = causationId, Take = take },
                    commandType: CommandType.StoredProcedure,
                    cancellationToken: cancellationToken));
            return rows.ToArray();
        }

        var projectId = Guid.Parse(query.ProjectReferenceId);
        var projectRows = await connection.QueryAsync<GovernanceTraceEventRow>(
            new CommandDefinition(
                ListForProjectProcedure,
                new { ProjectId = projectId, Take = take, BeforeCreatedUtc = query.ToUtc },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));
        return projectRows.ToArray();
    }

    private async Task<IReadOnlyList<GovernanceTraceEventRow>> BuildTimelineAsync(GovernanceTraceEventRow row, CancellationToken cancellationToken)
    {
        if (row.CorrelationId is null)
            return [row];

        using var connection = _connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<GovernanceTraceEventRow>(
            new CommandDefinition(
                ListForCorrelationProcedure,
                new { CorrelationId = row.CorrelationId.Value, Take = GovernanceTraceExplorerValidator.MaxTake },
                commandType: CommandType.StoredProcedure,
                cancellationToken: cancellationToken));

        var timeline = rows
            .Where(candidate => candidate.ProjectId == row.ProjectId)
            .OrderByDescending(candidate => candidate.CreatedUtc)
            .ThenByDescending(candidate => candidate.EventId)
            .ToArray();

        return timeline.Length == 0 ? [row] : timeline;
    }

    private static IEnumerable<GovernanceTraceEventRow> ApplyFilters(IEnumerable<GovernanceTraceEventRow> rows, GovernanceTraceQuery query)
    {
        var filtered = rows;

        if (Guid.TryParse(query.ProjectReferenceId, out var projectId))
            filtered = filtered.Where(row => row.ProjectId == projectId);

        if (!string.IsNullOrWhiteSpace(query.WorkflowRunId))
            filtered = filtered.Where(row => Matches(row.SubjectId, query.WorkflowRunId) || Matches(row.CorrelationId?.ToString("D"), query.WorkflowRunId));

        if (!string.IsNullOrWhiteSpace(query.WorkflowStepId))
            filtered = filtered.Where(row => Matches(row.SubjectId, query.WorkflowStepId));

        if (!string.IsNullOrWhiteSpace(query.SubjectReferenceId))
            filtered = filtered.Where(row => Matches(row.SubjectId, query.SubjectReferenceId));

        if (!string.IsNullOrWhiteSpace(query.EventKind))
            filtered = filtered.Where(row => Matches(row.EventType, query.EventKind));

        if (!string.IsNullOrWhiteSpace(query.SourceComponent))
            filtered = filtered.Where(row => Matches(row.ActorType, query.SourceComponent) || Matches(row.ActorId, query.SourceComponent));

        if (query.FromUtc.HasValue)
            filtered = filtered.Where(row => row.CreatedUtc >= query.FromUtc.Value);

        if (query.ToUtc.HasValue)
            filtered = filtered.Where(row => row.CreatedUtc <= query.ToUtc.Value);

        return filtered;
    }

    private static GovernanceTraceQuery Normalize(GovernanceTraceQuery query) =>
        query with
        {
            ProjectReferenceId = GovernanceTraceExplorerValidator.NormalizeText(query.ProjectReferenceId),
            WorkflowRunId = GovernanceTraceExplorerValidator.NormalizeText(query.WorkflowRunId),
            WorkflowStepId = GovernanceTraceExplorerValidator.NormalizeText(query.WorkflowStepId),
            CorrelationId = GovernanceTraceExplorerValidator.NormalizeText(query.CorrelationId),
            CausationId = GovernanceTraceExplorerValidator.NormalizeText(query.CausationId),
            SubjectReferenceId = GovernanceTraceExplorerValidator.NormalizeText(query.SubjectReferenceId),
            EventKind = GovernanceTraceExplorerValidator.NormalizeText(query.EventKind),
            SourceComponent = GovernanceTraceExplorerValidator.NormalizeText(query.SourceComponent),
            Take = Math.Clamp(query.Take, 1, GovernanceTraceExplorerValidator.MaxTake)
        };

    private static GovernanceTraceDetailResponse DetailFromList(GovernanceTraceListResponse list)
    {
        if (list.Status is GovernanceTraceExplorerStatus.InvalidRequest)
            return new GovernanceTraceDetailResponse
            {
                Status = GovernanceTraceExplorerStatus.InvalidRequest,
                Trace = null,
                Issues = list.Issues,
                BoundaryWarnings = GovernanceTraceExplorerBoundaries.Warnings
            };

        if (list.Traces.Count == 0)
            return NotFoundDetail();

        var summary = list.Traces[0];
        return new GovernanceTraceDetailResponse
        {
            Status = GovernanceTraceExplorerStatus.TraceFound,
            Trace = new GovernanceTraceDetail
            {
                Summary = summary,
                Timeline = list.Traces.Select(ToTimelineItem).ToArray(),
                RelatedReferences = RelatedReferences(summary),
                BoundaryWarnings = GovernanceTraceExplorerBoundaries.Warnings
            },
            Issues = [],
            BoundaryWarnings = GovernanceTraceExplorerBoundaries.Warnings
        };
    }

    private static GovernanceTraceDetailResponse FoundDetail(GovernanceTraceEventRow row, IReadOnlyList<GovernanceTraceEventRow> timeline) =>
        new()
        {
            Status = GovernanceTraceExplorerStatus.TraceFound,
            Trace = new GovernanceTraceDetail
            {
                Summary = ToSummary(row),
                Timeline = timeline.Select(ToTimelineItem).ToArray(),
                RelatedReferences = RelatedReferences(ToSummary(row)),
                BoundaryWarnings = GovernanceTraceExplorerBoundaries.Warnings
            },
            Issues = [],
            BoundaryWarnings = GovernanceTraceExplorerBoundaries.Warnings
        };

    private static GovernanceTraceListResponse InvalidList(IReadOnlyList<GovernanceTraceExplorerIssue> issues) =>
        new()
        {
            Status = GovernanceTraceExplorerStatus.InvalidRequest,
            Traces = [],
            Issues = issues,
            BoundaryWarnings = GovernanceTraceExplorerBoundaries.Warnings
        };

    private static GovernanceTraceDetailResponse InvalidDetail(IReadOnlyList<GovernanceTraceExplorerIssue> issues) =>
        new()
        {
            Status = GovernanceTraceExplorerStatus.InvalidRequest,
            Trace = null,
            Issues = issues,
            BoundaryWarnings = GovernanceTraceExplorerBoundaries.Warnings
        };

    private static GovernanceTraceDetailResponse NotFoundDetail() =>
        new()
        {
            Status = GovernanceTraceExplorerStatus.NoTraceFound,
            Trace = null,
            Issues = [],
            BoundaryWarnings = GovernanceTraceExplorerBoundaries.Warnings
        };

    private static GovernanceTraceSummary ToSummary(GovernanceTraceEventRow row)
    {
        var subject = GovernanceTraceExplorerValidator.SafeText(row.SubjectId);
        var eventKind = GovernanceTraceExplorerValidator.SafeText(row.EventType);
        var source = GovernanceTraceExplorerValidator.SafeText(string.IsNullOrWhiteSpace(row.ActorType) ? row.ActorId : row.ActorType);
        var safeSummary = $"Governance trace event '{eventKind}' recorded by '{source}' for subject '{subject}'.";

        return new GovernanceTraceSummary
        {
            TraceId = row.EventId.ToString("D"),
            ProjectReferenceId = row.ProjectId.ToString("D"),
            WorkflowRunId = SafeWorkflowReference(row, "workflow_run"),
            WorkflowStepId = SafeWorkflowReference(row, "workflow_step"),
            CorrelationId = row.CorrelationId?.ToString("D") ?? string.Empty,
            CausationId = row.CausationId?.ToString("D") ?? string.Empty,
            SubjectReferenceId = subject,
            EventKind = eventKind,
            SourceComponent = source,
            SafeSummary = GovernanceTraceExplorerValidator.ContainsUnsafeText(safeSummary) ? GovernanceTraceExplorerValidator.RedactedUnsafeText : safeSummary,
            RecordedUtc = row.CreatedUtc,
            IsReadOnlyTrace = true,
            IsAuthorityDecision = false,
            IsApproval = false,
            IsPolicySatisfaction = false,
            IsWorkflowTransition = false,
            CanApprove = false,
            CanReject = false,
            CanSatisfyPolicy = false,
            CanTransitionWorkflow = false,
            CanInvokeTool = false,
            CanDispatchAgent = false,
            CanCallModel = false,
            CanPromoteMemory = false,
            CanApplySource = false
        };
    }

    private static GovernanceTraceTimelineItem ToTimelineItem(GovernanceTraceEventRow row) =>
        ToTimelineItem(ToSummary(row));

    private static GovernanceTraceTimelineItem ToTimelineItem(GovernanceTraceSummary summary) =>
        new()
        {
            EventId = summary.TraceId,
            EventKind = summary.EventKind,
            SourceComponent = summary.SourceComponent,
            SafeSummary = summary.SafeSummary,
            RecordedUtc = summary.RecordedUtc,
            CorrelationId = summary.CorrelationId,
            CausationId = summary.CausationId,
            SubjectReferenceId = summary.SubjectReferenceId
        };

    private static IReadOnlyList<GovernanceTraceRelatedReference> RelatedReferences(GovernanceTraceSummary summary)
    {
        var references = new List<GovernanceTraceRelatedReference>();
        AddReference(references, "trace", summary.TraceId, "Governance event trace reference only.");
        AddReference(references, "project", summary.ProjectReferenceId, "Project scope reference only.");
        AddReference(references, "correlation", summary.CorrelationId, "Correlation reference only.");
        AddReference(references, "causation", summary.CausationId, "Causation reference only.");
        AddReference(references, "subject", summary.SubjectReferenceId, "Subject reference only.");
        return references;
    }

    private static void AddReference(List<GovernanceTraceRelatedReference> references, string kind, string id, string summary)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        references.Add(new GovernanceTraceRelatedReference
        {
            ReferenceKind = kind,
            ReferenceId = GovernanceTraceExplorerValidator.SafeText(id),
            SafeSummary = summary
        });
    }

    private static string SafeWorkflowReference(GovernanceTraceEventRow row, string subjectType) =>
        string.Equals(row.SubjectType, subjectType, StringComparison.OrdinalIgnoreCase)
            ? GovernanceTraceExplorerValidator.SafeText(row.SubjectId)
            : string.Empty;

    private static bool Matches(string? left, string right) =>
        string.Equals(GovernanceTraceExplorerValidator.NormalizeText(left), right, StringComparison.OrdinalIgnoreCase);

    private sealed class GovernanceTraceEventRow
    {
        public Guid EventId { get; init; }
        public Guid ProjectId { get; init; }
        public string EventType { get; init; } = string.Empty;
        public string ActorType { get; init; } = string.Empty;
        public string ActorId { get; init; } = string.Empty;
        public Guid? CorrelationId { get; init; }
        public Guid? CausationId { get; init; }
        public string? SubjectType { get; init; }
        public string? SubjectId { get; init; }
        public int PayloadVersion { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }
}
