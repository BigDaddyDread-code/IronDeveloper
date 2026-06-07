using System.Text.Json;
using IronDev.Core.Agents;
using IronDev.Core.Interfaces;
using IronDev.Core.RunReports;

namespace IronDev.Infrastructure.Services.Agents;

public sealed class SupervisorAgentRunService : ISupervisorAgentRunService
{
    private const string AgentName = "SupervisorAgent";

    private readonly IAgentModelResolver _modelResolver;
    private readonly string _repoRoot;
    private readonly IRunReportContractReader _runReportContractReader;
    private readonly IAgentProcessRunner? _processRunner;
    private readonly IAgentLlmClient? _llmClient;

    public SupervisorAgentRunService(
        IAgentModelResolver modelResolver,
        string repoRoot,
        IRunReportContractReader runReportContractReader,
        IAgentProcessRunner? processRunner = null,
        IAgentLlmClient? llmClient = null)
    {
        _modelResolver = modelResolver ?? throw new ArgumentNullException(nameof(modelResolver));
        _repoRoot = string.IsNullOrWhiteSpace(repoRoot)
            ? throw new ArgumentException("Repository root is required.", nameof(repoRoot))
            : repoRoot;
        _runReportContractReader = runReportContractReader ?? throw new ArgumentNullException(nameof(runReportContractReader));
        _processRunner = processRunner;
        _llmClient = llmClient;
    }

    public async Task<SupervisorAgentRunResult> RunAsync(
        SupervisorAgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var definition = ResolveSupervisorDefinition();
            var llmClient = request.LiveLlm ? _llmClient ?? new AgentLlmClient() : null;
            var agent = new SupervisorAgent(
                definition,
                _modelResolver,
                _repoRoot,
                _runReportContractReader,
                llmClient,
                _processRunner);

            var agentResult = await agent.RunAsync(new AgentRequest
            {
                AgentName = AgentName,
                GoalId = request.RunId,
                DogfoodRunId = request.RunId,
                DryRunOnly = true,
                RequestedTools = definition.AllowedTools,
                Inputs = new Dictionary<string, string>
                {
                    ["project"] = request.Project,
                    ["query"] = request.Query,
                    ["plan_path"] = request.PlanPath,
                    ["live_llm"] = request.LiveLlm.ToString()
                }
            }, cancellationToken);

            return MapResult(request, agentResult);
        }
        catch (Exception ex)
        {
            return BuildFailureResult(
                request,
                "SupervisorAgent could not be run.",
                ["SupervisorAgent run service failed.", ex.Message]);
        }
    }

    private static AgentDefinition ResolveSupervisorDefinition() =>
        AgentModelDefaults.CreateDefaultDefinitions()
            .First(definition => string.Equals(definition.Name, AgentName, StringComparison.OrdinalIgnoreCase));

    private static SupervisorAgentRunResult MapResult(SupervisorAgentRunRequest request, AgentResult agentResult)
    {
        var output = TryParse(agentResult.OutputJson);
        var supervisor = ReadElement(output, "supervisor");
        var tester = ReadElement(output, "tester");
        var governance = ReadElement(tester, "governance");
        var testerCommandStatus = ReadString(tester, "commandStatus") ?? "not_available";
        var status = ResolveCommandStatus(agentResult, testerCommandStatus);
        var testerUnavailable = string.Equals(testerCommandStatus, "not_available", StringComparison.OrdinalIgnoreCase);

        var warnings = new List<string>();
        warnings.AddRange(ReadStringArray(tester, "warnings"));
        if (testerUnavailable)
            warnings.Add("SupervisorAgent tester contract status was not available; treating supervisor run as failed.");

        var errors = new List<string>();
        if (testerUnavailable)
            errors.Add("SupervisorAgent output did not include an available tester contract status.");
        if (status == "failed" && !string.IsNullOrWhiteSpace(agentResult.Summary))
            errors.Add(agentResult.Summary);

        var governanceData = new AgentRunSupervisorGovernanceData
        {
            Decision = ReadString(governance, "decision") ?? "not_available",
            ApprovalDecision = ReadString(governance, "approvalDecision") ?? "not_available",
            BlockedReason = ReadString(governance, "blockedReason"),
            RequiresHumanApproval = ReadBoolean(governance, "requiresHumanApproval")
        };
        var testerData = new AgentRunSupervisorTesterData
        {
            RunId = ReadString(tester, "runId"),
            TraceId = ReadString(tester, "traceId"),
            CommandStatus = testerCommandStatus,
            RunStatus = ReadString(tester, "runStatus") ?? "not_available",
            Governance = governanceData,
            Warnings = warnings
        };
        var evidencePaths = agentResult.EvidencePaths;
        var commandsRun = agentResult.CommandsRun;
        var decision = ReadString(supervisor, "decision") ?? "not_available";
        var decisionReason = ReadString(supervisor, "decisionReason") ?? "No supervisor decision reason was returned.";

        var data = new AgentRunSupervisorContractData
        {
            Agent = agentResult.AgentName,
            RunId = request.RunId,
            Project = request.Project,
            Query = request.Query,
            PlanPath = request.PlanPath,
            AgentStatus = ResolveAgentStatus(status),
            ExitCode = status == "succeeded" ? 0 : Math.Max(agentResult.ExitCode, 1),
            Decision = decision,
            DecisionReason = decisionReason,
            Tester = testerData,
            EvidencePaths = evidencePaths,
            CommandsRun = commandsRun,
            Warnings = warnings,
            FailurePackage = status == "succeeded"
                ? null
                : BuildFailurePackage(
                    request.RunId,
                    status,
                    decision,
                    decisionReason,
                    testerData,
                    warnings,
                    errors,
                    evidencePaths,
                    commandsRun)
        };

        return new SupervisorAgentRunResult
        {
            Status = status,
            Summary = status switch
            {
                "succeeded" => $"Supervisor run '{request.RunId}' completed successfully.",
                "blocked" => $"Supervisor run '{request.RunId}' is blocked.",
                _ => $"Supervisor run '{request.RunId}' failed."
            },
            TraceId = data.Tester.TraceId,
            Data = data,
            Errors = errors,
            Warnings = warnings,
            ExitCode = status == "succeeded" ? 0 : 1
        };
    }

    private static SupervisorAgentRunResult BuildFailureResult(
        SupervisorAgentRunRequest request,
        string summary,
        IReadOnlyList<string> errors)
    {
        var testerData = new AgentRunSupervisorTesterData
        {
            RunId = null,
            TraceId = null,
            CommandStatus = "not_available",
            RunStatus = "not_available",
            Governance = new AgentRunSupervisorGovernanceData
            {
                Decision = "not_available",
                ApprovalDecision = "not_available",
                BlockedReason = null,
                RequiresHumanApproval = false
            },
            Warnings = []
        };

        var data = new AgentRunSupervisorContractData
        {
            Agent = AgentName,
            RunId = request.RunId,
            Project = request.Project,
            Query = request.Query,
            PlanPath = request.PlanPath,
            AgentStatus = "Failed",
            ExitCode = 1,
            Decision = "not_available",
            DecisionReason = summary,
            Tester = testerData,
            EvidencePaths = [],
            CommandsRun = [],
            Warnings = errors,
            FailurePackage = BuildFailurePackage(
                request.RunId,
                "failed",
                "not_available",
                summary,
                testerData,
                errors,
                errors,
                [],
                [])
        };

        return new SupervisorAgentRunResult
        {
            Status = "failed",
            Summary = summary,
            TraceId = null,
            Data = data,
            Errors = errors,
            Warnings = errors,
            ExitCode = 1
        };
    }

    private static string ResolveCommandStatus(AgentResult agentResult, string testerCommandStatus)
    {
        if (string.Equals(testerCommandStatus, "succeeded", StringComparison.OrdinalIgnoreCase) &&
            agentResult.Status == AgentRunStatus.Succeeded &&
            agentResult.ExitCode == 0)
        {
            return "succeeded";
        }

        if (string.Equals(testerCommandStatus, "blocked", StringComparison.OrdinalIgnoreCase))
            return "blocked";

        return "failed";
    }

    private static string ResolveAgentStatus(string status) =>
        status switch
        {
            "succeeded" => "Succeeded",
            "blocked" => "Blocked",
            _ => "Failed"
        };

    private static SupervisorFailurePackage BuildFailurePackage(
        string runId,
        string status,
        string decision,
        string? decisionReason,
        AgentRunSupervisorTesterData tester,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> evidencePaths,
        IReadOnlyList<string> commandsRun) =>
        new()
        {
            RunId = runId,
            Status = status,
            Decision = decision,
            DecisionReason = decisionReason,
            TesterCommandStatus = tester.CommandStatus,
            TesterRunStatus = tester.RunStatus,
            BlockedReason = tester.Governance.BlockedReason,
            Warnings = warnings,
            Errors = errors,
            EvidencePaths = evidencePaths,
            CommandsRun = commandsRun,
            RecommendedNextAction = ResolveRecommendedNextAction(status, tester)
        };

    private static string ResolveRecommendedNextAction(
        string status,
        AgentRunSupervisorTesterData tester)
    {
        if (string.Equals(tester.CommandStatus, "not_available", StringComparison.OrdinalIgnoreCase))
            return "Inspect tester run output and restore a valid tester run-report contract before patching.";

        if (string.Equals(tester.CommandStatus, "blocked", StringComparison.OrdinalIgnoreCase) ||
            tester.Governance.RequiresHumanApproval ||
            string.Equals(status, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return "Review approval/block reason before continuing. Do not patch automatically.";
        }

        if (string.Equals(tester.CommandStatus, "failed", StringComparison.OrdinalIgnoreCase))
            return "Inspect tester evidence paths and produce a fix plan before patching.";

        return "Inspect supervisor errors, warnings, and evidence paths before patching.";
    }

    private static JsonElement? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? ReadElement(JsonElement? root, params string[] path)
    {
        if (root is null)
            return null;

        var current = root.Value;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object)
                return null;

            if (!TryGetProperty(current, segment, out current))
                return null;
        }

        return current.Clone();
    }

    private static string? ReadString(JsonElement? root, string propertyName)
    {
        if (root is null || root.Value.ValueKind != JsonValueKind.Object)
            return null;

        if (!TryGetProperty(root.Value, propertyName, out var value))
            return null;

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool ReadBoolean(JsonElement? root, string propertyName)
    {
        if (root is null || root.Value.ValueKind != JsonValueKind.Object)
            return false;

        if (!TryGetProperty(root.Value, propertyName, out var value))
            return false;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement? root, string propertyName)
    {
        if (root is null || root.Value.ValueKind != JsonValueKind.Object)
            return [];

        if (!TryGetProperty(root.Value, propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value))
            return true;

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
