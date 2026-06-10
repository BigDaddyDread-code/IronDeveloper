namespace IronDev.Core.Agents.Audit;

public interface IAgentRunAuditEnvelopeReadRepository
{
    IReadOnlyList<AgentRunAuditEnvelope> List(string projectId);
    AgentRunAuditEnvelope? Get(string projectId, string agentRunId);
}

public sealed class InMemoryAgentRunAuditEnvelopeReadRepository : IAgentRunAuditEnvelopeReadRepository
{
    private readonly IReadOnlyList<AgentRunAuditEnvelope> _envelopes;

    public InMemoryAgentRunAuditEnvelopeReadRepository()
        : this([])
    {
    }

    public InMemoryAgentRunAuditEnvelopeReadRepository(IEnumerable<AgentRunAuditEnvelope> envelopes)
    {
        _envelopes = envelopes.ToArray();
    }

    public IReadOnlyList<AgentRunAuditEnvelope> List(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            return [];

        return _envelopes
            .Where(envelope => string.Equals(envelope.Run.ProjectId, projectId, StringComparison.Ordinal))
            .OrderByDescending(envelope => envelope.Run.CreatedAtUtc)
            .ThenBy(envelope => envelope.Run.AgentRunId, StringComparer.Ordinal)
            .ToArray();
    }

    public AgentRunAuditEnvelope? Get(string projectId, string agentRunId)
    {
        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(agentRunId))
            return null;

        return _envelopes.FirstOrDefault(envelope =>
            string.Equals(envelope.Run.ProjectId, projectId, StringComparison.Ordinal) &&
            string.Equals(envelope.Run.AgentRunId, agentRunId, StringComparison.Ordinal));
    }
}

public interface IAgentRunAuditQueryService
{
    AgentRunListResponseDto ListAgentRuns(string projectId, AgentRunAuditListQuery query);
    AgentRunDetailResponseDto GetAgentRun(string projectId, string agentRunId);
    AgentRunThoughtLedgerResponseDto GetThoughtLedger(string projectId, string agentRunId);
    AgentRunCapabilitiesResponseDto GetCapabilities(string projectId, string agentRunId);
    AgentRunBoundariesResponseDto GetBoundaryDecisions(string projectId, string agentRunId);
    AgentRunOutputsResponseDto GetOutputs(string projectId, string agentRunId);
    AgentRunInputsResponseDto GetInputs(string projectId, string agentRunId);
}

public sealed class AgentRunAuditQueryService : IAgentRunAuditQueryService
{
    public const string ProjectIdRequired = "AGENT_RUN_AUDIT_PROJECT_ID_REQUIRED";
    public const string AgentRunIdRequired = "AGENT_RUN_AUDIT_RUN_ID_REQUIRED";
    public const string TakeOutOfRange = "AGENT_RUN_AUDIT_TAKE_OUT_OF_RANGE";
    public const string SkipOutOfRange = "AGENT_RUN_AUDIT_SKIP_OUT_OF_RANGE";
    public const string DateRangeInvalid = "AGENT_RUN_AUDIT_DATE_RANGE_INVALID";
    public const string AgentRunNotFound = "AGENT_RUN_AUDIT_NOT_FOUND";

    private readonly IAgentRunAuditEnvelopeReadRepository _repository;

    public AgentRunAuditQueryService(IAgentRunAuditEnvelopeReadRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public AgentRunListResponseDto ListAgentRuns(string projectId, AgentRunAuditListQuery query)
    {
        query ??= new AgentRunAuditListQuery();
        var issues = ValidateProjectAndListQuery(projectId, query);
        if (issues.Count > 0)
        {
            return new AgentRunListResponseDto
            {
                ProjectId = projectId ?? string.Empty,
                Items = [],
                TotalCount = 0,
                Issues = issues
            };
        }

        var matching = _repository.List(projectId)
            .Where(envelope => string.IsNullOrWhiteSpace(query.AgentId) || string.Equals(envelope.Run.AgentId, query.AgentId, StringComparison.Ordinal))
            .Where(envelope => query.AgentKind is null || envelope.AgentDefinitionSnapshot.Kind == query.AgentKind.Value)
            .Where(envelope => query.Status is null || envelope.Run.Status == query.Status.Value)
            .Where(envelope => query.TriggerType is null || envelope.Run.TriggerType == query.TriggerType.Value)
            .Where(envelope => query.FromUtc is null || envelope.Run.CreatedAtUtc >= query.FromUtc.Value)
            .Where(envelope => query.ToUtc is null || envelope.Run.CreatedAtUtc <= query.ToUtc.Value)
            .ToArray();

        return new AgentRunListResponseDto
        {
            ProjectId = projectId,
            Items = matching.Skip(query.Skip).Take(query.Take).Select(AgentRunAuditDtoMapper.ToListItem).ToArray(),
            TotalCount = matching.Length,
            Issues = []
        };
    }

    public AgentRunDetailResponseDto GetAgentRun(string projectId, string agentRunId)
    {
        var issues = ValidateProjectAndRun(projectId, agentRunId);
        if (issues.Count > 0)
            return Detail(projectId, agentRunId, null, issues);

        var envelope = _repository.Get(projectId, agentRunId);
        if (envelope is null)
            return Detail(projectId, agentRunId, null, [Issue(AgentRunNotFound, "not_found", "Agent run audit record was not found for this project.")]);

        return Detail(projectId, agentRunId, AgentRunAuditDtoMapper.ToDetail(envelope), []);
    }

    public AgentRunThoughtLedgerResponseDto GetThoughtLedger(string projectId, string agentRunId)
    {
        var result = GetAgentRun(projectId, agentRunId);
        return new AgentRunThoughtLedgerResponseDto
        {
            ProjectId = result.ProjectId,
            AgentRunId = result.AgentRunId,
            Items = result.Run?.ThoughtLedger ?? [],
            Issues = result.Issues
        };
    }

    public AgentRunCapabilitiesResponseDto GetCapabilities(string projectId, string agentRunId)
    {
        var result = GetAgentRun(projectId, agentRunId);
        return new AgentRunCapabilitiesResponseDto
        {
            ProjectId = result.ProjectId,
            AgentRunId = result.AgentRunId,
            Items = result.Run?.CapabilityUses ?? [],
            Issues = result.Issues
        };
    }

    public AgentRunBoundariesResponseDto GetBoundaryDecisions(string projectId, string agentRunId)
    {
        var result = GetAgentRun(projectId, agentRunId);
        return new AgentRunBoundariesResponseDto
        {
            ProjectId = result.ProjectId,
            AgentRunId = result.AgentRunId,
            Items = result.Run?.BoundaryDecisions ?? [],
            Issues = result.Issues
        };
    }

    public AgentRunOutputsResponseDto GetOutputs(string projectId, string agentRunId)
    {
        var result = GetAgentRun(projectId, agentRunId);
        return new AgentRunOutputsResponseDto
        {
            ProjectId = result.ProjectId,
            AgentRunId = result.AgentRunId,
            Items = result.Run?.Outputs ?? [],
            Issues = result.Issues
        };
    }

    public AgentRunInputsResponseDto GetInputs(string projectId, string agentRunId)
    {
        var result = GetAgentRun(projectId, agentRunId);
        return new AgentRunInputsResponseDto
        {
            ProjectId = result.ProjectId,
            AgentRunId = result.AgentRunId,
            Items = result.Run?.Inputs ?? [],
            Issues = result.Issues
        };
    }

    private static AgentRunDetailResponseDto Detail(
        string projectId,
        string agentRunId,
        AgentRunDetailDto? run,
        IReadOnlyList<AgentRunAuditQueryIssueDto> issues) =>
        new()
        {
            ProjectId = projectId ?? string.Empty,
            AgentRunId = agentRunId ?? string.Empty,
            Run = run,
            Issues = issues
        };

    private static List<AgentRunAuditQueryIssueDto> ValidateProjectAndListQuery(string projectId, AgentRunAuditListQuery query)
    {
        var issues = ValidateProject(projectId);

        if (query.Take is < 1 or > 200)
            issues.Add(Issue(TakeOutOfRange, "error", "Take must be between 1 and 200."));

        if (query.Skip < 0)
            issues.Add(Issue(SkipOutOfRange, "error", "Skip must be greater than or equal to 0."));

        if (query.FromUtc is not null && query.ToUtc is not null && query.FromUtc > query.ToUtc)
            issues.Add(Issue(DateRangeInvalid, "error", "FromUtc cannot be after ToUtc."));

        return issues;
    }

    private static List<AgentRunAuditQueryIssueDto> ValidateProjectAndRun(string projectId, string agentRunId)
    {
        var issues = ValidateProject(projectId);
        if (string.IsNullOrWhiteSpace(agentRunId))
            issues.Add(Issue(AgentRunIdRequired, "error", "Agent run id is required."));

        return issues;
    }

    private static List<AgentRunAuditQueryIssueDto> ValidateProject(string projectId)
    {
        var issues = new List<AgentRunAuditQueryIssueDto>();
        if (string.IsNullOrWhiteSpace(projectId))
            issues.Add(Issue(ProjectIdRequired, "error", "Project id is required."));

        return issues;
    }

    private static AgentRunAuditQueryIssueDto Issue(string code, string severity, string message) =>
        new()
        {
            Code = code,
            Severity = severity,
            Message = message
        };
}
