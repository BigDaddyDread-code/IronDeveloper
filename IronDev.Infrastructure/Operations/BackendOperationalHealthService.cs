using System.Data;
using IronDev.Core.Agents;
using IronDev.Core.Governance;
using IronDev.Core.Operations;
using IronDev.Core.Workflow;
using IronDev.Data;
using Microsoft.Extensions.Configuration;

namespace IronDev.Infrastructure.Operations;

public sealed class BackendOperationalHealthService : IBackendOperationalHealthService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IGovernanceTraceExplorerService _traceExplorer;
    private readonly IFailedWorkflowDiagnosisReportService _failedWorkflowDiagnosisReport;
    private readonly IApprovalGateDogfoodCorrelationReportService _approvalGateDogfoodCorrelationReport;
    private readonly IAgentRunHealthSummaryService _agentRunHealthSummary;
    private readonly IConfiguration _configuration;
    private readonly BackendOperationalHealthValidator _validator;

    public BackendOperationalHealthService(
        IDbConnectionFactory connectionFactory,
        IGovernanceTraceExplorerService traceExplorer,
        IFailedWorkflowDiagnosisReportService failedWorkflowDiagnosisReport,
        IApprovalGateDogfoodCorrelationReportService approvalGateDogfoodCorrelationReport,
        IAgentRunHealthSummaryService agentRunHealthSummary,
        IConfiguration configuration)
        : this(
            connectionFactory,
            traceExplorer,
            failedWorkflowDiagnosisReport,
            approvalGateDogfoodCorrelationReport,
            agentRunHealthSummary,
            configuration,
            new BackendOperationalHealthValidator())
    {
    }

    internal BackendOperationalHealthService(
        IDbConnectionFactory connectionFactory,
        IGovernanceTraceExplorerService traceExplorer,
        IFailedWorkflowDiagnosisReportService failedWorkflowDiagnosisReport,
        IApprovalGateDogfoodCorrelationReportService approvalGateDogfoodCorrelationReport,
        IAgentRunHealthSummaryService agentRunHealthSummary,
        IConfiguration configuration,
        BackendOperationalHealthValidator validator)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _traceExplorer = traceExplorer ?? throw new ArgumentNullException(nameof(traceExplorer));
        _failedWorkflowDiagnosisReport = failedWorkflowDiagnosisReport ?? throw new ArgumentNullException(nameof(failedWorkflowDiagnosisReport));
        _approvalGateDogfoodCorrelationReport = approvalGateDogfoodCorrelationReport ?? throw new ArgumentNullException(nameof(approvalGateDogfoodCorrelationReport));
        _agentRunHealthSummary = agentRunHealthSummary ?? throw new ArgumentNullException(nameof(agentRunHealthSummary));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    }

    public async Task<BackendOperationalHealthResponse> GetHealthAsync(
        BackendOperationalHealthRequest request,
        CancellationToken cancellationToken = default)
    {
        var issues = _validator.Validate(request);
        if (issues.Count > 0)
            return Invalid(issues);

        var normalized = _validator.Normalize(request);
        var now = DateTimeOffset.UtcNow;
        var checks = new List<BackendDependencyHealthCheck>
        {
            Available("api-process", BackendDependencyKind.ApiProcess, "API process accepted the read-only health request.", now)
        };

        checks.Add(CheckConfiguration(now));
        checks.Add(await CheckDatabaseAsync(now, cancellationToken));
        checks.Add(Available("governance-event-read-model", BackendDependencyKind.GovernanceEventReadModel, "Governance event read model dependency is available for read-only inspection.", now));
        checks.Add(Available("workflow-read-model", BackendDependencyKind.WorkflowReadModel, "Workflow read model dependency is available for read-only inspection.", now));
        checks.Add(Available("tool-request-read-model", BackendDependencyKind.ToolRequestReadModel, "Tool request read model dependency is available for read-only inspection.", now));
        checks.Add(Available("tool-gate-decision-read-model", BackendDependencyKind.ToolGateDecisionReadModel, "Tool gate decision read model dependency is available for read-only inspection.", now));
        checks.Add(Available("approval-decision-read-model", BackendDependencyKind.ApprovalDecisionReadModel, "Approval decision read model dependency is available for read-only inspection.", now));
        checks.Add(Available("policy-decision-read-model", BackendDependencyKind.PolicyDecisionReadModel, "Policy decision read model dependency is available for read-only inspection.", now));
        checks.Add(Available("dogfood-receipt-read-model", BackendDependencyKind.DogfoodReceiptReadModel, "Dogfood receipt read model dependency is available for read-only inspection.", now));
        checks.Add(await CheckTraceExplorerAsync(normalized, now, cancellationToken));
        checks.Add(CheckReportSurface("failed-workflow-diagnosis-report", BackendDependencyKind.FailedWorkflowDiagnosisReport, _failedWorkflowDiagnosisReport, "Failed workflow diagnosis report surface is registered.", now));
        checks.Add(CheckReportSurface("approval-gate-dogfood-correlation-report", BackendDependencyKind.ApprovalGateDogfoodCorrelationReport, _approvalGateDogfoodCorrelationReport, "Approval, gate, and dogfood correlation report surface is registered.", now));
        checks.Add(CheckReportSurface("agent-run-health-summary", BackendDependencyKind.AgentRunHealthSummary, _agentRunHealthSummary, "Agent run health summary surface is registered.", now));

        var status = Classify(checks);
        var warnings = normalized.IncludeWarnings ? BuildWarnings(checks).ToArray() : [];
        var recommendations = BuildRecommendations(status, checks).ToArray();

        return new BackendOperationalHealthResponse
        {
            Status = status,
            Report = new BackendOperationalHealthReport
            {
                ReportId = "backend-operational-health",
                Status = status,
                GeneratedUtc = now,
                ProjectReferenceId = BackendOperationalHealthValidator.SafeText(normalized.ProjectReferenceId),
                CorrelationId = BackendOperationalHealthValidator.SafeText(normalized.CorrelationId),
                SafeSummaryLines = SummaryLines(status, checks).ToArray(),
                DependencyChecks = normalized.IncludeDependencyDetails ? checks : [],
                Warnings = warnings,
                Recommendations = recommendations,
                BoundaryWarnings = BackendOperationalHealthBoundaries.Warnings,
                IsHealthReportOnly = true,
                IsReleaseReadiness = false,
                IsApproval = false,
                IsPolicySatisfaction = false,
                IsWorkflowExecution = false,
                IsBackendRepair = false,
                IsMigrationExecution = false,
                CanRestartBackend = false,
                CanRepairBackend = false,
                CanRunMigration = false,
                CanExecuteWorkflow = false,
                CanTransitionWorkflow = false,
                CanDispatchAgent = false,
                CanInvokeTool = false,
                CanCallModel = false,
                CanApproveRelease = false,
                CanSatisfyPolicy = false,
                CanPromoteMemory = false,
                CanApplySource = false,
                CanApplyPatch = false,
                CreatesGovernanceEvent = false,
                CreatesApprovalDecision = false,
                CreatesPolicyDecision = false,
                CreatesToolRequest = false,
                CreatesDogfoodReceipt = false,
                TransitionsWorkflow = false,
                CallsModel = false,
                InvokesTool = false,
                DispatchesAgent = false,
                PromotesMemory = false
            },
            Issues = [],
            BoundaryWarnings = BackendOperationalHealthBoundaries.Warnings
        };
    }

    private async Task<BackendDependencyHealthCheck> CheckDatabaseAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            if (connection.State is not ConnectionState.Open)
                connection.Open();

            cancellationToken.ThrowIfCancellationRequested();
            return Available("database-connection", BackendDependencyKind.DatabaseConnection, "Database connection opened for read-only health inspection.", now);
        }
        catch
        {
            return Unavailable("database-connection", BackendDependencyKind.DatabaseConnection, "Database connection was unavailable for read-only health inspection.", now);
        }
    }

    private async Task<BackendDependencyHealthCheck> CheckTraceExplorerAsync(
        BackendOperationalHealthRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ProjectReferenceId) && string.IsNullOrWhiteSpace(request.CorrelationId))
                return Available("governance-trace-explorer", BackendDependencyKind.GovernanceTraceExplorer, "Governance trace explorer surface is registered for read-only inspection.", now);

            var response = await _traceExplorer.SearchAsync(new GovernanceTraceQuery
            {
                ProjectReferenceId = request.ProjectReferenceId,
                CorrelationId = request.CorrelationId,
                Take = 1,
                IncludeRelated = false
            }, cancellationToken);

            return response.Status is GovernanceTraceExplorerStatus.InvalidRequest
                ? Degraded("governance-trace-explorer", BackendDependencyKind.GovernanceTraceExplorer, "Governance trace explorer returned validation issues for the supplied safe selector.", now)
                : Available("governance-trace-explorer", BackendDependencyKind.GovernanceTraceExplorer, "Governance trace explorer responded to a bounded read-only query.", now);
        }
        catch
        {
            return Unavailable("governance-trace-explorer", BackendDependencyKind.GovernanceTraceExplorer, "Governance trace explorer was unavailable for read-only health inspection.", now);
        }
    }

    private BackendDependencyHealthCheck CheckConfiguration(DateTimeOffset now)
    {
        var hasDatabaseSetting = !string.IsNullOrWhiteSpace(_configuration.GetConnectionString("IronDeveloperDb"));
        var hasJwtSetting = !string.IsNullOrWhiteSpace(_configuration["Jwt:Key"]);
        return hasDatabaseSetting && hasJwtSetting
            ? Available("configuration-presence", BackendDependencyKind.ConfigurationPresence, "Required configuration keys are present; values are not exposed.", now)
            : NotConfigured("configuration-presence", BackendDependencyKind.ConfigurationPresence, "One or more required configuration keys are not present; values are not exposed.", now);
    }

    private static BackendDependencyHealthCheck CheckReportSurface(
        string checkId,
        BackendDependencyKind kind,
        object dependency,
        string summary,
        DateTimeOffset now) =>
        dependency is null
            ? Unavailable(checkId, kind, $"{summary} Dependency resolution failed.", now)
            : Available(checkId, kind, summary, now);

    private static BackendOperationalHealthStatus Classify(IReadOnlyList<BackendDependencyHealthCheck> checks)
    {
        if (checks.Any(check => check.DependencyKind is BackendDependencyKind.DatabaseConnection && check.Status is BackendDependencyHealthStatus.Unavailable))
            return BackendOperationalHealthStatus.Unavailable;

        if (checks.Any(check => check.Status is BackendDependencyHealthStatus.Degraded or BackendDependencyHealthStatus.Unavailable or BackendDependencyHealthStatus.NotConfigured))
            return BackendOperationalHealthStatus.Degraded;

        return BackendOperationalHealthStatus.Healthy;
    }

    private static IEnumerable<BackendOperationalHealthWarning> BuildWarnings(IReadOnlyList<BackendDependencyHealthCheck> checks)
    {
        foreach (var check in checks.Where(check => check.Status is BackendDependencyHealthStatus.Degraded or BackendDependencyHealthStatus.Unavailable or BackendDependencyHealthStatus.NotConfigured))
        {
            yield return new BackendOperationalHealthWarning
            {
                WarningId = BackendOperationalHealthValidator.SafeText($"warning:{check.CheckId}"),
                Kind = check.Status switch
                {
                    BackendDependencyHealthStatus.Unavailable => BackendOperationalHealthWarningKind.DependencyUnavailable,
                    BackendDependencyHealthStatus.NotConfigured => BackendOperationalHealthWarningKind.ConfigurationMissing,
                    _ => BackendOperationalHealthWarningKind.DependencyDegraded
                },
                SafeSummary = BackendOperationalHealthValidator.SafeText($"Dependency '{check.CheckId}' needs human investigation."),
                IsEvidenceOnly = true,
                IsFailureProof = false,
                CanRepair = false
            };
        }
    }

    private static IEnumerable<BackendOperationalHealthRecommendation> BuildRecommendations(
        BackendOperationalHealthStatus status,
        IReadOnlyList<BackendDependencyHealthCheck> checks)
    {
        yield return new BackendOperationalHealthRecommendation
        {
            RecommendationId = "inspect-health-evidence",
            SafeSummary = "Inspect the safe dependency checks before taking any operational action.",
            SupportingCheckIds = checks.Select(check => check.CheckId).ToArray(),
            IsInvestigationOnly = true,
            CanMutateState = false,
            CanRestartBackend = false,
            CanRunMigration = false,
            CanExecuteWorkflow = false,
            CanApproveRelease = false
        };

        if (status is BackendOperationalHealthStatus.Degraded or BackendOperationalHealthStatus.Unavailable)
        {
            yield return new BackendOperationalHealthRecommendation
            {
                RecommendationId = "manual-operator-review",
                SafeSummary = "Use a separate governed operator review before repair, restart, migration, workflow execution, or release decisions.",
                SupportingCheckIds = checks
                    .Where(check => check.Status is not BackendDependencyHealthStatus.Available)
                    .Select(check => check.CheckId)
                    .ToArray(),
                IsInvestigationOnly = true,
                CanMutateState = false,
                CanRestartBackend = false,
                CanRunMigration = false,
                CanExecuteWorkflow = false,
                CanApproveRelease = false
            };
        }
    }

    private static IEnumerable<string> SummaryLines(BackendOperationalHealthStatus status, IReadOnlyList<BackendDependencyHealthCheck> checks)
    {
        yield return BackendOperationalHealthValidator.SafeText($"Backend operational health status is {status}.");
        yield return BackendOperationalHealthValidator.SafeText($"Read-only dependency checks returned {checks.Count(check => check.Status is BackendDependencyHealthStatus.Available)} available signal(s).");
        yield return "This health report is not release readiness, approval, repair, restart, migration execution, or workflow execution.";
    }

    private static BackendDependencyHealthCheck Available(string id, BackendDependencyKind kind, string summary, DateTimeOffset now) =>
        Check(id, kind, BackendDependencyHealthStatus.Available, summary, now);

    private static BackendDependencyHealthCheck Degraded(string id, BackendDependencyKind kind, string summary, DateTimeOffset now) =>
        Check(id, kind, BackendDependencyHealthStatus.Degraded, summary, now);

    private static BackendDependencyHealthCheck Unavailable(string id, BackendDependencyKind kind, string summary, DateTimeOffset now) =>
        Check(id, kind, BackendDependencyHealthStatus.Unavailable, summary, now);

    private static BackendDependencyHealthCheck NotConfigured(string id, BackendDependencyKind kind, string summary, DateTimeOffset now) =>
        Check(id, kind, BackendDependencyHealthStatus.NotConfigured, summary, now);

    private static BackendDependencyHealthCheck Check(
        string id,
        BackendDependencyKind kind,
        BackendDependencyHealthStatus status,
        string summary,
        DateTimeOffset now) =>
        new()
        {
            CheckId = BackendOperationalHealthValidator.SafeText(id),
            DependencyKind = kind,
            Status = status,
            SafeSummary = BackendOperationalHealthValidator.SafeText(summary),
            CheckedUtc = now,
            IsReadOnlyCheck = true,
            IsRepairAction = false,
            CanMutateDependency = false,
            CanRestartDependency = false,
            CanRunMigration = false
        };

    private static BackendOperationalHealthResponse Invalid(IReadOnlyList<BackendOperationalHealthIssue> issues) =>
        new()
        {
            Status = BackendOperationalHealthStatus.InvalidRequest,
            Report = null,
            Issues = issues,
            BoundaryWarnings = BackendOperationalHealthBoundaries.Warnings
        };
}
